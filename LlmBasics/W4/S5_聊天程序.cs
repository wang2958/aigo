using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// W4 第五节：能连续对话的命令行聊天程序（S1~S4 集大成）
// 因为要"显示思考(reasoning_content)"，IChatClient 抽象拿不到 → 走裸调 HTTP + 手动解析 SSE。
// 组装：S1 维护 history / S2 流式逐字 / S3 Ctrl+C 取消本轮 / S4 token 统计 + 滑动窗口截断。
public static class W4S5_聊天程序
{
    record ChatMsg(string Role, string Content);

    const int TokenBudget = 2000;   // 滑动窗口预算（粗估 token，非精确）

    // 粗略估算（沿用 S4，够做截断决策即可，计费以 API usage 为准）
    static int Est(ChatMsg m) => (int)Math.Ceiling((m.Content?.Length ?? 0) / 2.0) + 4;

    static List<ChatMsg> Truncate(List<ChatMsg> history, int budget)
    {
        var system = history.FirstOrDefault(m => m.Role == "system");
        var turns = history.Where(m => m.Role != "system").ToList();
        var kept = new List<ChatMsg>();
        int used = 0;
        if (system is not null) { kept.Add(system); used += Est(system); }
        var recent = new List<ChatMsg>();
        for (int i = turns.Count - 1; i >= 0; i--)   // 从最新往旧
        {
            int cost = Est(turns[i]);
            if (used + cost > budget) break;
            used += cost;
            recent.Add(turns[i]);
        }
        recent.Reverse();
        kept.AddRange(recent);
        return kept;
    }

    public static async Task Run(string baseUrl, string apiKey, string model)
    {
        var history = new List<ChatMsg>
        {
            new("system", "你是一个乐于助人的中文助手，回答简洁。")
        };

        // 累计统计（S4）
        long totIn = 0, totOut = 0, totReason = 0; int turns = 0;

        // Ctrl+C：只取消"本轮生成"，不退出程序（S3）
        CancellationTokenSource? turnCts = null;
        Console.CancelKeyPress += (_, e) =>
        {
            if (turnCts is not null) { e.Cancel = true; turnCts.Cancel(); }
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"=== 命令行聊天（模型 {model}）===");
        Console.WriteLine("命令：/exit 退出 · /clear 清空历史 · /stats 看累计token · /help 帮助");
        Console.WriteLine("提示：生成中按 Ctrl+C 可取消本轮。\n");

        while (true)
        {
            Console.Write("你> ");
            var line = Console.ReadLine();
            if (line is null) break;              // 管道输入结束 / EOF
            line = line.Trim();
            if (line.Length == 0) continue;

            // 斜杠命令
            if (line.StartsWith("/"))
            {
                switch (line.ToLowerInvariant())
                {
                    case "/exit": case "/quit":
                        Console.WriteLine($"再见。本次共 {turns} 轮，累计 token：入 {totIn} / 出 {totOut} / 思考 {totReason}。");
                        return;
                    case "/clear":
                        history = new List<ChatMsg> { history[0] };   // 只留 system
                        Console.WriteLine("[已清空历史，保留 system]\n");
                        continue;
                    case "/stats":
                        Console.WriteLine($"[统计] {turns} 轮 · 历史 {history.Count} 条(估 {history.Sum(Est)} token) · 累计 入{totIn}/出{totOut}/思考{totReason}\n");
                        continue;
                    case "/help":
                        Console.WriteLine("命令：/exit /clear /stats /help\n");
                        continue;
                    default:
                        Console.WriteLine($"[未知命令 {line}，当成普通消息发送]\n");
                        break;
                }
            }

            // S1：加入用户消息 → S4：发送前截断
            history.Add(new ChatMsg("user", line));
            int before = history.Count;
            history = Truncate(history, TokenBudget);
            if (history.Count < before)
                Console.WriteLine($"[上下文截断：{before} → {history.Count} 条]");

            // S3：本轮取消令牌
            turnCts = new CancellationTokenSource();
            var answer = new StringBuilder();
            Console.Write("AI> ");
            try
            {
                await StreamOnce(http, baseUrl, model, history, turnCts.Token,
                    onReason: t => { Console.ForegroundColor = ConsoleColor.DarkYellow; Console.Write(t); Console.ResetColor(); },
                    onContent: t => { Console.ForegroundColor = ConsoleColor.Green; Console.Write(t); Console.ResetColor(); answer.Append(t); },
                    onUsage: (i, o, r) => { totIn += i; totOut += o; totReason += r; });
                turns++;
                // S1：把 AI 回复塞回历史（即使被取消，也保留已生成的部分，维持连贯）
                history.Add(new ChatMsg("assistant", answer.ToString()));
                Console.WriteLine();
            }
            catch (OperationCanceledException)
            {
                history.Add(new ChatMsg("assistant", answer.ToString()));  // 保留半截
                Console.WriteLine("\n[本轮已取消，半截回复已保留进历史]");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"\n[网络错误] {ex.Message}");
            }
            finally
            {
                turnCts.Dispose();
                turnCts = null;
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    // 裸调流式：解析 SSE，分别回调 思考/正文/usage（S2b 的路子 + 取消令牌贯穿）
    static async Task StreamOnce(HttpClient http, string baseUrl, string model,
        List<ChatMsg> history, CancellationToken ct,
        Action<string> onReason, Action<string> onContent, Action<long, long, long> onUsage)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            messages = history.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        // ct 贯穿到 SendAsync：取消才能真正 abort 连接、停生成计费（S3）
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        bool answered = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:")) continue;
            var payload = line.Substring("data:".Length).Trim();
            if (payload == "[DONE]") break;

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                long i = GetLong(u, "prompt_tokens"), o = GetLong(u, "completion_tokens");
                long r = u.TryGetProperty("completion_tokens_details", out var d) ? GetLong(d, "reasoning_tokens") : 0;
                onUsage(i, o, r);
            }
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");

            if (delta.TryGetProperty("reasoning_content", out var rc)
                && rc.ValueKind == JsonValueKind.String && rc.GetString() is { Length: > 0 } think)
                onReason(think);

            if (delta.TryGetProperty("content", out var c)
                && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } txt)
            {
                if (!answered) { Console.WriteLine(); Console.Write("    "); answered = true; } // 思考→正文换行
                onContent(txt);
            }
        }
    }

    static long GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
