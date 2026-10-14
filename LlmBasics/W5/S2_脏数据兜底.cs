using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

// W5 第二节：处理模型返回脏数据的兜底
// 承接第一节的"合法≠完整≠正确"：即使有 schema，也要有分层防线。
// 纵深防御：① 剥离 markdown 围栏 → ② 反序列化(try-catch) → ③ 业务校验 → ④ 失败回喂重试 → ⑤ 最终兜底返回 null，绝不编数据。
public static class W5S2_脏数据兜底
{
    public enum Priority { Low, Medium, High, Urgent }
    public record TicketInfo(string Title, Priority Priority, string[] Tags, string Summary);

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task Run(IChatClient client)
    {
        // 五个"脏样本"，覆盖真实翻车点（本地、确定性，不花 API）
        var samples = new (string Label, string Raw)[]
        {
            ("A 干净JSON", """{"Title":"登录500","Priority":"Urgent","Tags":["auth","db"],"Summary":"生产登录接口持续500"}"""),
            ("B markdown围栏", "```json\n{\"Title\":\"磁盘满\",\"Priority\":\"High\",\"Tags\":[\"ops\"],\"Summary\":\"磁盘占用100%\"}\n```"),
            ("C 枚举越界", """{"Title":"网络慢","Priority":"非常紧急","Tags":["net"],"Summary":"延迟很高"}"""),
            ("D 截断JSON", """{"Title":"数据库","Priority":"High","Tags":["db" """.TrimEnd()),
            ("E 缺必填字段", """{"Title":"缺摘要","Priority":"Low","Tags":[]}"""),
        };

        Console.WriteLine("===== Part 1：分层解析器 vs 5 个脏样本 =====");
        foreach (var (label, raw) in samples)
        {
            Console.WriteLine($"\n--- 样本 {label} ---");
            var (ok, ticket, err) = TryParseTicket(raw);
            if (ok) Console.WriteLine($"✅ 通过 → {ticket!.Title} / {ticket.Priority} / [{string.Join(",", ticket.Tags)}]");
            else Console.WriteLine($"❌ 拦下 → {err}");
        }

        // Part 2：回喂重试（真实调用）—— 把非法样本 C 交给模型自己修正
        Console.WriteLine("\n\n===== Part 2：回喂重试（把错误告诉模型，让它修正）=====");
        var bad = samples[2].Raw;   // C 枚举越界
        var (ok2, fixedTicket, err2) = await RetryWithFeedback(client, bad, maxTries: 2);
        if (ok2) Console.WriteLine($"✅ 修正成功 → {fixedTicket!.Title} / {fixedTicket.Priority}");
        else Console.WriteLine($"❌ 重试后仍失败 → {err2}（兜底：返回 null，绝不编造）");
    }

    // 分层兜底解析：围栏剥离 → 反序列化 → 业务校验
    static (bool ok, TicketInfo? ticket, string? error) TryParseTicket(string raw)
    {
        var json = StripFence(raw);                                   // ① 剥围栏
        TicketInfo? t;
        try { t = JsonSerializer.Deserialize<TicketInfo>(json, JsonOpts); }   // ② 反序列化
        catch (JsonException ex) { return (false, null, $"JSON解析失败(截断/语法/非法枚举): {ex.Message}"); }
        if (t is null) return (false, null, "解析得到 null");

        // ③ 业务校验（schema 管不到的语义/完整性）
        if (string.IsNullOrWhiteSpace(t.Title)) return (false, t, "Title 为空");
        if (string.IsNullOrWhiteSpace(t.Summary)) return (false, t, "Summary 缺失/为空");
        if (t.Tags is null || t.Tags.Length == 0) return (false, t, "Tags 为空");
        return (true, t, null);
    }

    // ④ 回喂重试：解析失败就把"具体错误"发回模型，要求只返回修正后的 JSON
    static async Task<(bool, TicketInfo?, string?)> RetryWithFeedback(IChatClient client, string bad, int maxTries)
    {
        var cur = bad;
        string? lastErr = null;
        for (int i = 0; i < maxTries; i++)
        {
            var (ok, t, err) = TryParseTicket(cur);
            if (ok) return (true, t, null);
            lastErr = err;
            Console.WriteLine($"  第{i + 1}次失败({err})，回喂模型修正…");
            var msgs = new List<ChatMessage>
            {
                new(ChatRole.System, "你是JSON修正器。只输出修正后的合法JSON，不要任何解释或围栏。" +
                                     "字段必须含 Title/Priority/Tags/Summary；Priority 只能是 Low/Medium/High/Urgent 之一。"),
                new(ChatRole.User, $"下面这段JSON有问题（{err}），请修正后返回：\n{cur}")
            };
            var r = await client.GetResponseAsync(msgs);
            cur = r.Text ?? "";
        }
        var final = TryParseTicket(cur);   // ⑤ 最终兜底：还失败就如实返回，绝不编数据
        return final;
    }

    static string StripFence(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            int end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0) s = s[..end];
        }
        return s.Trim();
    }
}
