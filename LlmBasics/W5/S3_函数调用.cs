using Microsoft.Extensions.AI;

// W5 第三节：函数调用原理 —— tools 定义 / 模型如何决定 / 如何执行回填
// 手写工具循环（不用自动的 FunctionInvokingChatClient），看清每一步在发生什么。
public static class W5S3_函数调用
{
    public static async Task Run(IChatClient client)
    {
        // ① 定义工具：从一个普通 C# lambda 生成 AIFunction（自动带 name/description/参数 schema）
        var getWeather = AIFunctionFactory.Create((string city) => $"{city}：今天晴，气温 22~28℃，东南风 3 级，空气质量良。",
            "get_weather",
            "查询指定城市的当前天气",
            null);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个助手。需要天气信息时，调用提供的工具，不要自己编。"),
            new(ChatRole.User, "北京今天天气怎么样？适合跑步吗？")
        };

        var options = new ChatOptions { Tools = [getWeather] };

        // ② 第一次调用：模型不直接回答，而是"决定"要不要调工具
        Console.WriteLine("=== 第一次调用：看模型决定什么 ===");
        var resp = await client.GetResponseAsync(messages, options);
        var calls = resp.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

        if (calls.Count == 0)
        {
            Console.WriteLine($"模型没调工具，直接回答：{resp.Text}");
            return;
        }

        foreach (var call in calls)
        {
            var args = call.Arguments ?? new Dictionary<string, object?>();
            Console.WriteLine($"模型请求调用工具 → {call.CallId} : {call.Name}");
            Console.WriteLine($"  参数: {string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        // ③ 执行回填：先把 assistant"发起工具调用"的那一轮加回历史，再执行、把结果作为 tool 消息塞回
        messages.AddRange(resp.Messages);
        foreach (var call in calls)
        {
            var args = call.Arguments ?? new Dictionary<string, object?>();
            var result = await getWeather.InvokeAsync(new AIFunctionArguments(args));
            Console.WriteLine($"  → 本地执行结果: {result}");
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result ?? "")]));
        }

        // ④ 第二次调用：模型拿到工具结果，生成最终自然语言回答
        Console.WriteLine("\n=== 第二次调用：模型用工具结果作答 ===");
        var final = await client.GetResponseAsync(messages, options);
        Console.WriteLine(final.Text);
    }
}
