using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

public static class W6S5_最近邻搜索
{
    public sealed record IndexedChunk(int Id, string Text, ReadOnlyMemory<float> Vector);
    public sealed record SearchResult(int Id, string Text, double Score);

    public static double Cosine(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
            throw new ArgumentException("向量必须非空且维度相同。");

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double x = a.Span[i], y = b.Span[i];
            if (!double.IsFinite(x) || !double.IsFinite(y))
                throw new ArgumentException("向量分量必须为有限值。");
            dot += x * y;
            normA += x * x;
            normB += y * y;
        }
        if (normA == 0 || normB == 0)
            throw new ArgumentException("零向量没有可用于余弦计算的方向。");

        return Math.Clamp(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)), -1, 1);
    }

    public static List<SearchResult> Search(ReadOnlyMemory<float> query, IReadOnlyList<IndexedChunk> chunks, int topK)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topK);
        if (topK == 0) return new List<SearchResult>();

        var scored = new List<SearchResult>();
        foreach (var chunk in chunks)
        {
            double score = Cosine(query, chunk.Vector);
            scored.Add(new SearchResult(chunk.Id, chunk.Text, score));
        }
        return scored.OrderByDescending(result => result.Score).Take(topK).ToList();
    }

    public static async Task Run(string baseUrl, string apiKey, string embeddingModel)
    {
        Console.WriteLine("=== W6 第5节：余弦相似度与暴力最近邻搜索 ===");
        Console.WriteLine($"二维手算验证：[1,0] 与 [40,30] 的余弦 = {Cosine(new float[] { 1, 0 }, new float[] { 40, 30 }):F4}");
        Console.WriteLine($"真实 embedding 模型：{embeddingModel}");
        Console.WriteLine("本例按主题手工分为4段，固定候选内容，只观察搜索；不调用聊天模型。\n");

        using IEmbeddingGenerator<string, Embedding<float>> generator =
            new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
                .GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();
        var texts = new[]
        {
            "退款到账时间：退款将在审核通过后七个工作日内原路退回。如果超过约定时间仍未到账，请提供订单号联系在线客服。",
            "退货包装要求：退货商品应保留完整包装与配件，寄回前请拍照记录外观，并妥善保存快递单号。",
            "物流查询方式：发货后可在订单详情查看承运公司与运单号码。运输途中长时间停滞时，可以联系承运公司核实。",
            "账号安全说明：客服不会索取登录密码或短信验证码，请勿向陌生人提供这些信息。"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var embeddings = await generator.GenerateAsync(texts, cancellationToken: timeout.Token);
        if (embeddings.Count != texts.Length)
            throw new InvalidOperationException("embedding 返回数量与文档块数不一致。");
        var chunks = new List<IndexedChunk>();
        for (int i = 0; i < texts.Length; i++)
            chunks.Add(new IndexedChunk(i + 1, texts[i], embeddings[i].Vector));
        Console.WriteLine($"内存索引：{chunks.Count} 块，维度 {chunks[0].Vector.Length}；文档向量仅生成一次，本次运行内复用。\n");

        const int topK = 2;
        foreach (string question in new[] { "退款多久能到账？", "月球到地球有多远？" })
        {
            Console.WriteLine($"问题：{question}");
            var query = await generator.GenerateAsync(new[] { question }, cancellationToken: timeout.Token);
            if (query.Count != 1)
                throw new InvalidOperationException("embedding 返回数量与问题数量不一致。");
            var results = Search(query[0].Vector, chunks, topK);
            var ranked = Search(query[0].Vector, chunks, chunks.Count);
            Console.WriteLine("教学对照：额外在本地计算完整排名（无阈值，不重复调用模型）：");
            for (int i = 0; i < ranked.Count; i++)
            {
                var result = ranked[i];
                Console.WriteLine($"  第{i + 1}名 块{result.Id} cos={result.Score:F4}");
            }
            Console.WriteLine($"Top-{topK} 实际返回 {results.Count} 块原文：");
            foreach (var result in results)
                Console.WriteLine($"  块{result.Id}：{result.Text}");
            Console.WriteLine("这些分数是本次API结果，不是正确率。\n");
        }
        Console.WriteLine("无阈值的Top-K总能选出候选（块数足够时），文档外问题也不例外；排名靠前不等于包含答案。");
        Console.WriteLine("本节只做检索，不生成回答，不根据这两个问题设定通用阈值。");
    }
}
