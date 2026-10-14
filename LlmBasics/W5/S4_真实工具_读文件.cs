using Microsoft.Extensions.AI;

// W5 第四节：实现 1 个真实工具（读文件）
// 相比第3节的假天气 lambda，这里工具真的碰磁盘。两个真实工程点：
//   ① 路径穿越防护：模型是"不可信输入源"，它给的 path 必须锁死在沙箱目录内（承接第3节扩展5）
//   ② 大文件截断：别把整个文件塞进上下文，token 会爆（承接 W4 第4节 token 预算）
public static class W5S4_真实工具_读文件
{
    // 沙箱根目录：工具只允许读这里面的文件
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ai-docs"));
    const int MaxChars = 1500;

    // 真实工具实现：安全地读文件。注意——越界/不存在时"返回错误字符串"而不是抛异常，
    // 因为这个返回值会被回填给模型，让模型看到"[拒绝]…"从而自己应对。
    static string ReadFileSafely(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "[错误] path 为空";

        // ① 路径穿越防护：解析成绝对路径后，必须仍在 Root 之内
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return $"[拒绝] 路径越界：'{relativePath}' 解析到 '{full}'，不在允许目录 (ai-docs) 内。";

        if (!File.Exists(full))
            return $"[错误] 文件不存在：{relativePath}";

        // ② 大文件截断
        var text = File.ReadAllText(full);
        if (text.Length > MaxChars)
            return text[..MaxChars] + $"\n…[已截断：原文 {text.Length} 字，只返回前 {MaxChars} 字]";
        return text;
    }

    public static async Task Run(IChatClient client)
    {
        // ===== Part 1：安全演示（本地、确定性，不经模型）——看路径穿越被挡 =====
        Console.WriteLine($"===== Part 1：路径穿越防护（沙箱根 = {Root}）=====");
        foreach (var p in new[] { "进度.md", "../qwen.key.tmp", "../../Windows/win.ini", "阶段产出/不存在的文件.md" })
        {
            var r = ReadFileSafely(p);
            Console.WriteLine($"\n  read_file(\"{p}\")\n    → {Preview(r, 80)}");
        }

        // ===== Part 2：接进工具循环，让模型用自然语言驱动读文件 =====
        Console.WriteLine("\n\n===== Part 2：模型调用 read_file 回答真实问题 =====");
        var tool = AIFunctionFactory.Create(
            (string path) => ReadFileSafely(path),
            "read_file",
            "读取 ai-docs 目录下某个文件的内容。path 是相对 ai-docs 的相对路径，例如 '进度.md'。",
            null);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个助手。需要文件内容时调用 read_file 工具，不要自己编造文件内容。"),
            new(ChatRole.User, "读一下 进度.md，告诉我：现在学到第几周？累计完成了多少个任务？")
        };
        var options = new ChatOptions { Tools = [tool] };

        var resp = await client.GetResponseAsync(messages, options);
        var calls = resp.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        if (calls.Count == 0) { Console.WriteLine($"模型没调工具：{resp.Text}"); return; }

        messages.AddRange(resp.Messages);
        foreach (var call in calls)
        {
            var args = call.Arguments ?? new Dictionary<string, object?>();
            Console.WriteLine($"  [工具调用] {call.Name}({string.Join(",", args.Select(kv => $"{kv.Key}={kv.Value}"))})");
            var result = await tool.InvokeAsync(new AIFunctionArguments(args));
            Console.WriteLine($"  [工具返回] {Preview(result?.ToString(), 80)}");
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result ?? "")]));
        }

        var final = await client.GetResponseAsync(messages, options);
        Console.WriteLine($"\n--- 模型最终回答 ---\n{final.Text}");
    }

    static string Preview(string? s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        s = s.Replace("\n", " ").Replace("\r", " ");
        return s.Length <= n ? s : s[..n] + "…";
    }
}
