public static class W6S4_切块器测试
{
    public static void Run()
    {
        var cases = new (string Name, string Text, int Size, int Overlap, string Expected)[]
        {
            ("空文本不产生块", "", 4, 0, ""),
            ("短文本不补齐", "abc", 4, 0, "0:abc"),
            ("恰好一块不产生尾块", "abcd", 4, 2, "0:abcd"),
            ("无重叠保留短尾块", "abcdefghij", 4, 0, "0:abcd|4:efgh|8:ij"),
            ("重叠按步长前进并在末尾停止", "abcdefghij", 4, 1, "0:abcd|3:defg|6:ghij"),
            ("重叠保留短尾块", "abcdefghi", 4, 1, "0:abcd|3:defg|6:ghi"),
            ("最大合法重叠仍前进", "abcde", 3, 2, "0:abc|1:bcd|2:cde"),
            ("普通中文按char切分", "退款需要原订单号", 4, 1, "0:退款需要|3:要原订单|6:单号")
        };
        int failed = 0;
        foreach (var test in cases)
        {
            var chunks = W6S4_切块器.Split(test.Text, test.Size, test.Overlap);
            string actual = string.Join("|", chunks.Select(c => $"{c.Start}:{c.Text}"));
            bool passed = actual == test.Expected && chunks.All(c => c.End <= test.Text.Length);
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {test.Name}");
            if (!passed)
            {
                failed++;
                Console.WriteLine($"  期望={test.Expected}，实际={actual}");
            }
        }

        foreach (var (size, overlap) in new[] { (0, 0), (-1, 0), (4, -1), (4, 4), (4, 5) })
        {
            try
            {
                W6S4_切块器.Split("abc", size, overlap);
                Console.WriteLine($"FAIL 非法参数 size={size}, overlap={overlap} 未被拒绝");
                failed++;
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine($"PASS 拒绝非法参数 size={size}, overlap={overlap}");
            }
        }

        Console.WriteLine($"共 {cases.Length + 5} 项，失败 {failed} 项。");
        if (failed > 0) throw new InvalidOperationException("切块测试失败。");
    }
}
