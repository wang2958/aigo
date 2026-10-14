using Microsoft.Extensions.AI;

// W5 第6节：tool_choice（Microsoft.Extensions.AI 里叫 ChatOptions.ToolMode）
// 目的：对比 Auto / None / RequireAny / RequireSpecific 四档，看"强制程度"如何改变模型行为。
// 四档是一条"自由度递减、确定性递增"的谱系：强制 = 拿灵活性换确定性。
public static class W5S6_强制调用
{
    // —— 复用上两节的两个工具（假数据，够演示即可）——
    static string GetWeather(string city) => city switch
    {
        "北京" => "北京：晴，22~28℃，东南风 3 级，空气质量良。",
        "上海" => "上海：多云转小雨，24~29℃，湿度大，空气质量优。",
        _ => $"{city}：暂无该城市天气数据。"
    };
    static string GetCurrentTime() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    static Dictionary<string, AIFunction> BuildTools() => new()
    {
        ["get_weather"] = AIFunctionFactory.Create((string city) => GetWeather(city), "get_weather", "查询指定城市的当前天气", null),
        ["get_current_time"] = AIFunctionFactory.Create(() => GetCurrentTime(), "get_current_time", "获取当前的日期和时间", null),
    };

    // 跑一个档位：给定 ToolMode + 用户问题，看模型是"要工具"还是"直接答"
    static async Task Probe(IChatClient client, string label, ChatToolMode mode, string userMsg)
    {
        var tools = BuildTools();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是助手。"),
            new(ChatRole.User, userMsg),
        };
        // 关键：ToolMode 就是 tool_choice 旋钮
        var options = new ChatOptions
        {
            Tools = tools.Values.Cast<AITool>().ToList(),
            ToolMode = mode,
        };

        Console.WriteLine($"\n===== {label} =====");
        Console.WriteLine($"问题：{userMsg}");

        // 容错：某些供应商在特定模式下不支持某些 tool_choice 档位（实测 Qwen 思考模式拒绝 required/object）。
        // 一档炸了不该拖垮整个对比——把异常也当成一种"实测结果"打印出来。
        ChatResponse resp;
        try
        {
            resp = await client.GetResponseAsync(messages, options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"模型行为：请求被拒绝 → {ex.GetType().Name}");
            Console.WriteLine($"供应商返回：{Trunc(ex.Message.Replace("\n", " "))}");
            Console.WriteLine("→ 这就是抽象泄漏：ChatToolMode 枚举编译通过，但底层协议在运行时被供应商 400。");
            return;
        }

        var calls = resp.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

        if (calls.Count == 0)
        {
            Console.WriteLine($"模型行为：直接回答（没要工具）\n回答：{Trunc(resp.Text)}");
        }
        else
        {
            Console.WriteLine($"模型行为：请求工具调用 → {string.Join(", ", calls.Select(c => $"{c.Name}({FmtArgs(c)})"))}");
            // 执行一次，让模型给最终答案
            messages.AddRange(resp.Messages);
            foreach (var c in calls)
            {
                var args = c.Arguments ?? new Dictionary<string, object?>();
                object? result = tools.TryGetValue(c.Name, out var fn)
                    ? await fn.InvokeAsync(new AIFunctionArguments(args))
                    : $"[错误] 未知工具 '{c.Name}'";
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(c.CallId, result ?? "")]));
            }
            var final = await client.GetResponseAsync(messages, new ChatOptions { Tools = options.Tools }); // 第二轮放开，让它答
            Console.WriteLine($"最终回答：{Trunc(final.Text)}");
        }
    }

    static string FmtArgs(FunctionCallContent c) =>
        c.Arguments is null ? "" : string.Join(",", c.Arguments.Select(kv => $"{kv.Key}={kv.Value}"));
    static string Trunc(string s) => s.Length <= 120 ? s.Replace("\n", " ") : s.Replace("\n", " ")[..120] + "…";

    public static async Task Run(IChatClient client)
    {
        Console.WriteLine("tool_choice（ToolMode）四档对比：同一个天气问题，看强制程度如何改变模型行为\n");

        // 档位1：Auto（默认）——模型自己决定。问天气，它大概率会调 get_weather。
        await Probe(client, "① Auto：工具可选，模型自由决定", ChatToolMode.Auto, "北京今天天气怎么样？");

        // 档位2：None——工具禁用。即使给了 get_weather，也不许调，只能凭"记忆"答（大概率编造）。
        await Probe(client, "② None：禁用工具，即使给了也不许调", ChatToolMode.None, "北京今天天气怎么样？");

        // 档位3：RequireAny——必须调工具，但调哪个随它。堵死"凭记忆瞎编"。
        await Probe(client, "③ RequireAny：必须调某个工具", ChatToolMode.RequireAny, "北京今天天气怎么样？");

        // 档位4：RequireSpecific——必须调指定工具。哪怕问的是天气，也强制它去调 get_current_time。
        await Probe(client, "④ RequireSpecific(get_current_time)：强制调指定工具", ChatToolMode.RequireSpecific("get_current_time"), "北京今天天气怎么样？");

        Console.WriteLine("\n===== 对比结论（2026-10-14 实测 qwen3.8-max）=====");
        Console.WriteLine("① Auto：模型自己决定调 get_weather，答对天气。");
        Console.WriteLine("② None：禁用工具后，模型没编造，诚实说'拿不到实时数据'并给替代查询途径。");
        Console.WriteLine("        （但这是模型自觉，不是结构保证——换个它'记得'的问题照样会自信作答。）");
        Console.WriteLine("③④ RequireAny / RequireSpecific：均 HTTP 400 被拒！");
        Console.WriteLine("        Qwen 思考模式不支持 tool_choice=required/object，只支持 auto/none。");
        Console.WriteLine("        → '强制调用'在这个推理模型上物理做不了；抽象层枚举编译通过≠供应商运行时支持。");
    }
}
