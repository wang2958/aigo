public static class W6S4_切块器
{
    public sealed record Chunk(int Start, string Text)
    {
        public int End => Start + Text.Length;
    }

    public static List<Chunk> Split(string text, int chunkSize, int overlap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        if (overlap < 0 || overlap >= chunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlap), "重叠长度必须满足 0 <= overlap < chunkSize。");

        var chunks = new List<Chunk>();
        int step = chunkSize - overlap;
        for (int start = 0; start < text.Length; start += step)
        {
            int length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(new Chunk(start, text.Substring(start, length)));
            if (start + length == text.Length) break;
        }
        return chunks;
    }

    public static void Run()
    {
        const string answer = "退款将在审核通过后七个工作日内原路退回。";
        const string document =
            "售后服务说明：本店提供订单查询、物流跟踪与退换货服务。申请售后前，请先登录购买时使用的账号，在订单列表中找到对应商品。客服会核对订单状态，并根据商品实际情况说明处理流程。"
            + answer
            + "如果超过约定时间仍未到账，请提供订单号联系在线客服，不要重复提交退款申请。退货商品应保留完整包装与配件，寄回前请拍照记录外观，并妥善保存快递单号。"
            + "物流查询说明：发货后可在订单详情查看承运公司与运单号码。运输途中出现长时间停滞时，可以联系承运公司核实，也可以由客服协助查询。"
            + "账号安全说明：客服不会索取登录密码或短信验证码，请勿向陌生人提供这些信息。修改收货地址需要在发货前完成，发货后请联系人工客服确认是否可以拦截。";
        var settings = new[] { (Name: "A", Size: 100, Overlap: 0), (Name: "B", Size: 100, Overlap: 20), (Name: "C", Size: 200, Overlap: 0) };
        int answerStart = document.IndexOf(answer, StringComparison.Ordinal);

        Console.WriteLine("=== W6 第4节：固定长度切块（纯本地，不调用模型）===");
        Console.WriteLine("单位为 C# char（UTF-16 代码单元），不是 token；此样本只用普通中文与标点。");
        Console.WriteLine("这是刻意让答案跨过位置100的教学样本，不代表真实文档上的检索效果。");
        Console.WriteLine($"原文长度：{document.Length}；答案范围：[{answerStart}, {answerStart + answer.Length})");
        Console.WriteLine($"观察句：{answer}\n原文：\n{document}");

        foreach (var setting in settings)
        {
            var chunks = Split(document, setting.Size, setting.Overlap);
            int total = chunks.Sum(c => c.Text.Length);
            int extra = total - document.Length;
            int intact = chunks.Count(c => c.Text.Contains(answer, StringComparison.Ordinal));
            Console.WriteLine($"\n--- {setting.Name}：size={setting.Size}，overlap={setting.Overlap}，步长={setting.Size - setting.Overlap} ---");
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                Console.WriteLine($"块{i + 1} [{chunk.Start}, {chunk.End})，长度={chunk.Text.Length}");
                Console.WriteLine(chunk.Text);
            }
            Console.WriteLine($"统计：{chunks.Count} 块；合计 {total} char；额外重复 {extra} char（{(double)extra / document.Length:P1}）");
            Console.WriteLine($"完整包含观察句的块数：{intact}");
        }

        Console.WriteLine("\nA/B 只改变重叠，A/C 只改变块大小；保住句子不等于检索命中。");
        Console.WriteLine("重复字符数不是 token 计费数，也不是向量库的总存储量。");
    }
}
