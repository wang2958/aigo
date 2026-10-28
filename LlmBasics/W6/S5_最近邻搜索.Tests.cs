public static class W6S5_最近邻搜索测试
{
    public static void Run()
    {
        int total = 0, failed = 0;
        void Check(string name, Action test)
        {
            total++;
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
        }
        void Equal(double expected, double actual)
        {
            if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-9)
                throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("无效输入未被拒绝");
        }

        var cases = new (string Name, float[] A, float[] B, double Expected)[]
        {
            ("二维手算A", new float[] { 1, 0 }, new float[] { 3, 4 }, 0.6),
            ("二维手算B", new float[] { 1, 0 }, new float[] { 4, 3 }, 0.8),
            ("放大十倍不改变余弦", new float[] { 1, 0 }, new float[] { 40, 30 }, 0.8),
            ("正交为零", new float[] { 1, 0 }, new float[] { 0, 5 }, 0),
            ("反向为负一", new float[] { 1, 0 }, new float[] { -2, 0 }, -1),
            ("相同向量为一", new float[] { 3, 4 }, new float[] { 3, 4 }, 1),
            ("先转double避免float乘法溢出", new float[] { float.MaxValue, 0 }, new float[] { float.MaxValue, 0 }, 1),
            ("小非零向量不下溢为零", new float[] { float.Epsilon, 0 }, new float[] { float.Epsilon, 0 }, 1)
        };
        foreach (var test in cases)
            Check(test.Name, () => Equal(test.Expected, W6S5_最近邻搜索.Cosine(test.A, test.B)));

        Check("拒绝维度不同", () => Reject(() => W6S5_最近邻搜索.Cosine(new float[] { 1 }, new float[] { 1, 0 })));
        Check("拒绝空向量", () => Reject(() => W6S5_最近邻搜索.Cosine(Array.Empty<float>(), Array.Empty<float>())));
        Check("拒绝零向量", () => Reject(() => W6S5_最近邻搜索.Cosine(new float[] { 0, 0 }, new float[] { 1, 0 })));
        Check("拒绝NaN", () => Reject(() => W6S5_最近邻搜索.Cosine(new float[] { float.NaN }, new float[] { 1 })));
        Check("拒绝无穷值", () => Reject(() => W6S5_最近邻搜索.Cosine(new float[] { 1 }, new float[] { float.PositiveInfinity })));

        var chunks = new[]
        {
            new W6S5_最近邻搜索.IndexedChunk(1, "A原文", new float[] { 3, 4 }),
            new W6S5_最近邻搜索.IndexedChunk(2, "B原文", new float[] { 4, 3 }),
            new W6S5_最近邻搜索.IndexedChunk(3, "C原文", new float[] { 0, 5 })
        };
        Check("Top2顺序与原文对应", () =>
        {
            var results = W6S5_最近邻搜索.Search(new float[] { 1, 0 }, chunks, 2);
            if (string.Join("|", results.Select(r => $"{r.Id}:{r.Text}")) != "2:B原文|1:A原文")
                throw new InvalidOperationException("排序、数量或原文映射错误");
            Equal(0.8, results[0].Score);
            Equal(0.6, results[1].Score);
        });
        Check("全部负分仍返回Top2而非假设相关", () =>
        {
            var results = W6S5_最近邻搜索.Search(new float[] { -1, -1 }, chunks, 2);
            if (string.Join(",", results.Select(r => r.Id)) != "3,1" || results.Any(r => r.Score >= 0))
                throw new InvalidOperationException("不应过滤负分，且同分应保留输入顺序");
        });
        Check("K大于块数返回所有块", () => Equal(3, W6S5_最近邻搜索.Search(new float[] { 1, 0 }, chunks, 10).Count));
        Check("K为零返回空", () => Equal(0, W6S5_最近邻搜索.Search(new float[] { 1, 0 }, chunks, 0).Count));
        Check("空库返回空", () => Equal(0, W6S5_最近邻搜索.Search(new float[] { 1, 0 }, Array.Empty<W6S5_最近邻搜索.IndexedChunk>(), 2).Count));
        Check("拒绝负K", () => Reject(() => W6S5_最近邻搜索.Search(new float[] { 1, 0 }, chunks, -1)));
        Console.WriteLine($"共 {total} 项，失败 {failed} 项。");
        if (failed > 0) throw new InvalidOperationException("最近邻搜索测试失败。");
    }
}
