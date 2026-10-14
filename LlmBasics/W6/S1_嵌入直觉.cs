using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

// W6 第1节：建立向量相似度的直觉。
// 真调 embedding 模型，把几组短语转成向量，算余弦相似度，验证"语义相近→向量相近"。
// 重点不是打印完整结构（那是第2节），而是用真实数字建立"意思=距离"的直觉。
public static class W6S1_嵌入直觉
{
    // 余弦相似度：只看方向（夹角），不看长度。范围 [-1,1]，1=同向最相似，0=垂直不相关。
    static double Cosine(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var va = a.Span; var vb = b.Span;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < va.Length; i++) { dot += va[i] * vb[i]; na += va[i] * va[i]; nb += vb[i] * vb[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static async Task Run(string baseUrl, string apiKey, string embeddingModel)
    {
        IEmbeddingGenerator<string, Embedding<float>> gen =
            new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
                .GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

        // 三组对照：
        // 组1（语义 vs 字面，验证预测2）：A/B 意思相同字面不同，C 完全无关
        // 组2（基础直觉）：猫/猫咪 近义，猫/汽车 无关
        // 组3（一词多义，验证预测3）：同一个"苹果"，品牌语境 vs 水果语境
        var phrases = new[]
        {
            "我喜欢吃苹果",        // A 水果
            "我爱吃apple",         // B 同义、字面不同
            "今天股市大跌",        // C 无关
            "猫",
            "猫咪",
            "汽车",
            "苹果手机多少钱",      // 苹果=品牌
            "吃苹果对身体好",      // 苹果=水果
            // 练习三组（情感相反/跨语言/长度悬殊）
            "我很喜欢这部电影",
            "我很讨厌这部电影",
            "你好，世界",
            "hello world",
            "一只可爱的猫在阳光下睡觉",
        };

        var resp = await gen.GenerateAsync(phrases);
        var vec = new Dictionary<string, Embedding<float>>();
        for (int i = 0; i < phrases.Length; i++) vec[phrases[i]] = resp[i];

        var sample = vec["猫"].Vector;
        Console.WriteLine($"embedding 模型: {embeddingModel}");
        Console.WriteLine($"向量维度: {vec["猫"].Dimensions}（即每个文本被表示成 {vec["猫"].Dimensions} 个浮点数）");
        Console.WriteLine($"\"猫\" 的向量前 6 维: [{string.Join(", ", sample.Slice(0, 6).ToArray().Select(x => x.ToString("F4")))}] …（共 {sample.Length} 维，只展示开头）\n");

        void Sim(string x, string y, string note) =>
            Console.WriteLine($"  cos(\"{x}\", \"{y}\") = {Cosine(vec[x].Vector, vec[y].Vector):F4}   {note}");

        Console.WriteLine("组1 · 语义 vs 字面（预测2：sim(A,B) 应 > sim(A,C)）");
        Sim("我喜欢吃苹果", "我爱吃apple", "← 意思相同、字面几乎不重合");
        Sim("我喜欢吃苹果", "今天股市大跌", "← 完全无关");
        Sim("我爱吃apple", "今天股市大跌", "← 完全无关");

        Console.WriteLine("\n组2 · 基础直觉（近义 vs 无关）");
        Sim("猫", "猫咪", "← 近义");
        Sim("猫", "汽车", "← 无关");

        Console.WriteLine("\n组3 · 一词多义（预测3：\"苹果\" 向量是否一样？）");
        Sim("苹果手机多少钱", "吃苹果对身体好", "← 同一个词\"苹果\"，品牌 vs 水果语境");
        Sim("苹果手机多少钱", "我喜欢吃苹果", "← 品牌语境 vs 水果语境");
        Sim("吃苹果对身体好", "我喜欢吃苹果", "← 都是水果语境（对照）");

        Console.WriteLine("\n练习 · 三组对照（预测：1低 2高 3中）");
        Sim("我很喜欢这部电影", "我很讨厌这部电影", "← 练习1 情感相反、话题相同");
        Sim("你好，世界", "hello world", "← 练习2 跨语言");
        Sim("猫", "一只可爱的猫在阳光下睡觉", "← 练习3 长度悬殊、话题相同");

        Console.WriteLine("\n解读：组1 若 sim(A,B) 明显 > sim(A,C)，说明嵌入抓的是\"意思\"不是\"字面\"；");
        Console.WriteLine("组3 若 品牌vs水果 的相似度 < 水果vs水果，说明同一个词的向量随上下文变（预测3\"一样\"是错的）。");
    }
}
