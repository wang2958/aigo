using Microsoft.Extensions.AI;

// W5 第五节：多工具编排 + 观察并行调用
// 在第3/4节单工具的基础上，注册多个工具、用 while 多轮循环，观察模型是"一次并行发多个调用"还是"一轮一个"。
public static class W5S5_多工具并行
{
    // 两个（假）工具：查天气 + 查当前时间
    static string GetWeather(string city) => city switch
    {
        "北京" => "北京：晴，22~28℃，东南风 3 级，空气质量良。",
        "上海" => "上海：多云转小雨，24~29℃，湿度大，空气质量优。",
        _ => $"{city}：暂无该城市天气数据。"
    };
    static string GetCurrentTime() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public static async Task Run(IChatClient client)
    {
        // 工具白名单（name → AIFunction）：分发只认注册过的，防幻觉工具名（承接第3节扩展5）
        var tools = new Dictionary<string, AIFunction>
        {
            ["get_weather"] = AIFunctionFactory.Create((string city) => GetWeather(city), "get_weather", "查询指定城市的当前天气", null),
            ["get_current_time"] = AIFunctionFactory.Create(() => GetCurrentTime(), "get_current_time", "获取当前的日期和时间", null),
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是助手。需要实时信息（时间/天气）时调用工具，不要编造。"),
            new(ChatRole.User, "现在几点了？再帮我查北京和上海的天气，比较哪个更适合出门跑步。")
        };
        var options = new ChatOptions { Tools = tools.Values.Cast<AITool>().ToList() };

        // while 多轮循环：模型可能这轮要工具、拿到结果后下轮还要别的工具。加最大轮数护栏（W12 的种子）
        const int maxRounds = 5;
        for (int round = 1; round <= maxRounds; round++)
        {
            var resp = await client.GetResponseAsync(messages, options);
            var calls = resp.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

            if (calls.Count == 0)
            {
                Console.WriteLine($"\n--- 第 {round} 轮：模型不再要工具，给出最终回答 ---\n{resp.Text}");
                return;
            }

            Console.WriteLine($"\n[第 {round} 轮] 模型这一次返回 {calls.Count} 个工具调用 → {(calls.Count > 1 ? "并行！" : "单个")}");
            messages.AddRange(resp.Messages);   // 保留 assistant 发起工具调用的那一轮

            foreach (var call in calls)
            {
                var args = call.Arguments ?? new Dictionary<string, object?>();
                Console.WriteLine($"  调用 {call.Name}({string.Join(",", args.Select(kv => $"{kv.Key}={kv.Value}"))})");

                if (!tools.TryGetValue(call.Name, out var fn))   // 白名单：未知工具名不反射乱调
                {
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, $"[错误] 未知工具 '{call.Name}'")]));
                    continue;
                }
                var result = await fn.InvokeAsync(new AIFunctionArguments(args));
                Console.WriteLine($"    → {result}");
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result ?? "")]));
            }
        }
        Console.WriteLine($"[达到最大轮数 {maxRounds}，强制停止]");
    }
}
