using Microsoft.Extensions.AI;

// W4 第四节：token 统计 + 上下文截断
// 两件事：①真调一次，把 usage 各字段挖出来看；②本地估算 token + 按预算截断历史。
public static class W4S4_token统计与截断
{
    // 粗略估算：中文/混合文本约 2 字符 ≈ 1 token，另加 4 摊 role/分隔开销。
    // 注意：这是"估算"不是"实测"——真正的 token 数只有 API 返回的 usage 才准（见 Part A 对照）。
    static int EstimateTokens(string? s) => s is null ? 0 : (int)Math.Ceiling(s.Length / 2.0);
    static int EstimateTokens(ChatMessage m) => EstimateTokens(m.Text) + 4;

    public static async Task Run(IChatClient client)
    {
        // ===== Part A：真调一次，看真实 usage（含思考 token、缓存 token）=====
        Console.WriteLine("===== Part A：真实 token 统计 =====");
        var prompt = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个简洁的助手。"),
            new(ChatRole.User, "用一句话解释什么是上下文窗口。")
        };
        int estInput = prompt.Sum(EstimateTokens);
        var resp = await client.GetResponseAsync(prompt);
        var u = resp.Usage;
        Console.WriteLine($"本地估算输入 token（粗）: {estInput}");
        if (u is not null)
        {
            Console.WriteLine($"API 实测 → 输入 {u.InputTokenCount} / 输出 {u.OutputTokenCount} / 总计 {u.TotalTokenCount}");
            Console.WriteLine($"            其中 思考 {u.ReasoningTokenCount} / 缓存命中输入 {u.CachedInputTokenCount}");
        }
        Console.WriteLine($"[对照] 估算 {estInput} vs 实测输入 {u?.InputTokenCount}：估算只够用来做截断决策，计费以实测为准\n");

        // ===== Part B：上下文截断（纯本地，不调 API）=====
        Console.WriteLine("===== Part B：上下文截断 =====");
        // 造一段 10 轮的历史：第 0 句藏了一个"早期事实"，后面是填充对话
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个助手。"),
            new(ChatRole.User, "记住：我叫小明，我最喜欢的编程语言是 C#。"),   // 早期关键事实
            new(ChatRole.Assistant, "好的，小明，记住了。"),
        };
        for (int i = 1; i <= 6; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"这是第 {i} 个填充问题，内容随便写点东西占位。"));
            history.Add(new ChatMessage(ChatRole.Assistant, $"这是第 {i} 个填充回答，模型说了一些无关紧要的话。"));
        }

        int total = history.Sum(EstimateTokens);
        Console.WriteLine($"截断前：{history.Count} 条消息，估算 {total} token");

        const int budget = 120;   // 故意设个小预算，逼它丢历史
        var kept = TruncateToTokenBudget(history, budget);
        int keptTokens = kept.Sum(EstimateTokens);
        Console.WriteLine($"预算 {budget} token → 截断后保留 {kept.Count} 条，估算 {keptTokens} token\n");

        Console.WriteLine("--- 保留的消息 ---");
        foreach (var m in kept) Console.WriteLine($"  [{m.Role}] {Trunc(m.Text, 30)}");

        bool factKept = kept.Any(m => m.Text?.Contains("小明") == true);
        Console.WriteLine($"\n[关键观察] 早期事实\"我叫小明/喜欢C#\"是否还在? → {(factKept ? "在" : "❌ 被截断了！")}");
    }

    // 截断策略：system 永远保留；其余从"最新"往"最旧"塞，塞不下就停（保留最近对话）
    static List<ChatMessage> TruncateToTokenBudget(List<ChatMessage> history, int budget)
    {
        var system = history.FirstOrDefault(m => m.Role == ChatRole.System);
        var turns = history.Where(m => m.Role != ChatRole.System).ToList();

        var kept = new List<ChatMessage>();
        int used = 0;
        if (system is not null) { kept.Add(system); used += EstimateTokens(system); }

        var recent = new List<ChatMessage>();
        for (int i = turns.Count - 1; i >= 0; i--)   // 从最新往旧
        {
            int cost = EstimateTokens(turns[i]);
            if (used + cost > budget) break;          // 超预算，停
            used += cost;
            recent.Add(turns[i]);
        }
        recent.Reverse();                             // 恢复时间顺序
        kept.AddRange(recent);
        return kept;
    }

    static string Trunc(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");
}
