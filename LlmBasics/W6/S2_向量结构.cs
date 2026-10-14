using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

// W6 第2节：调 embedding API，打印向量看结构。
// 看四件事：多少维 / 占多少字节 / 返回的向量是否已归一化(长度=1) / 同一段话调两次是否逐位相同。
public static class W6S2_向量结构
{
    static double L2Norm(ReadOnlyMemory<float> v)
    {
        double sum = 0;
        foreach (var x in v.Span) sum += x * x;
        return Math.Sqrt(sum);
    }

    public static async Task Run(string baseUrl, string apiKey, string embeddingModel)
    {
        IEmbeddingGenerator<string, Embedding<float>> gen =
            new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
                .GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

        const string text = "向量数据库用来做语义检索";

        // 同一段话调两次，看是否逐位相同（确定性/可缓存性）
        var r1 = await gen.GenerateAsync(new[] { text });
        var r2 = await gen.GenerateAsync(new[] { text });
        var v1 = r1[0].Vector;
        var v2 = r2[0].Vector;

        Console.WriteLine($"文本: \"{text}\"");
        Console.WriteLine($"维度: {v1.Length}（每个文本 = {v1.Length} 个 float）");
        Console.WriteLine($"字节: {v1.Length * 4} B = {v1.Length * 4 / 1024.0:F1} KB（float=4字节；一万条≈{v1.Length * 4 / 1024.0 * 10000 / 1024.0:F0} MB）");

        // 结构1：返回的向量是否已归一化（长度=1）？
        Console.WriteLine($"\n返回向量的 L2 长度 = {L2Norm(v1):F6}");
        Console.WriteLine(L2Norm(v1) is > 0.999 and < 1.001
            ? "→ 长度≈1：模型返回的已是归一化向量（只保留方向）。向量库可用内积代替余弦，检索更快。"
            : "→ 长度≠1：未归一化。比方向时需自己除长度（或入库前先归一化）。");

        // 结构2：分量分布（均值应接近0，取值小幅度）
        var arr = v1.ToArray();
        Console.WriteLine($"\n分量分布: min={arr.Min():F4}, max={arr.Max():F4}, mean={arr.Average():F4}");
        Console.WriteLine($"前 10 维: [{string.Join(", ", arr.Take(10).Select(x => x.ToString("F4")))}]");

        // 结构3：同一段话两次调用是否逐位相同 / 有多接近
        bool same = v1.Span.SequenceEqual(v2.Span);
        double cos12 = Cosine(v1, v2);
        double maxDiff = 0;
        for (int i = 0; i < v1.Length; i++) maxDiff = Math.Max(maxDiff, Math.Abs(v1.Span[i] - v2.Span[i]));

        Console.WriteLine($"\n同一段话调两次：逐位(bit)相同? {same}");
        Console.WriteLine($"  但两次向量的余弦 = {cos12:F6}，最大单分量差 = {maxDiff:E2}");
        if (same)
            Console.WriteLine("→ 完全确定性：可安全缓存。");
        else if (cos12 > 0.99)
            Console.WriteLine("→ bit 不同但余弦≈0.999：服务端运行间微小差异(GPU批处理/舍入)，非真随机。");
        else
            Console.WriteLine("→ 真非确定性：同输入两次差异明显。");
        Console.WriteLine("  尺度对比：同话两次≈0.999 ＞ 同义不同字面≈0.89 ＞ 无关≈0.3~0.5。");
        Console.WriteLine("  运行间差异远小于文本间差异 → 检索意义上可视为确定性，缓存/预计算安全；");
        Console.WriteLine("  但判'是否同一段话'要用余弦阈值，别用 bit 相等或 cos>0.9999 这种过严尺子。");
    }

    static double Cosine(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a.Span[i] * b.Span[i]; na += a.Span[i] * a.Span[i]; nb += b.Span[i] * b.Span[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
