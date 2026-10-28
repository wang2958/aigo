using System.Text;
using System.ClientModel;                 // ApiKeyCredential
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

var target = (args.Length > 0 ? args[0] : "w6s4").ToLowerInvariant();
if (target == "w6s4")
{
    W6S4_切块器.Run();
    return;
}
if (target == "w6s4-test")
{
    W6S4_切块器测试.Run();
    return;
}

// 装配：读配置（appsettings 非密 + user-secrets 里的 key），构建一个 IChatClient
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();
var baseUrl = config["Qwen:BaseUrl"]!;
var model = config["Qwen:Model"]!;
var apiKey = config["Qwen:ApiKey"]!;

// 分发：跑哪一节由命令行参数决定，可选第二个参数临时覆盖模型
//   dotnet run                    （默认跑最新一节）
//   dotnet run -- w5s3            （指定节：w3s6 / w4s1..w4s6 / w5s1 / w5s2 / w5s3）
//   dotnet run -- w4s2 qwen3.8-flash  （第二个参数=临时覆盖模型，跨模型对比）
if (args.Length > 1) model = args[1];   // args[1] 存在则覆盖模型
Console.WriteLine($"[config] 本次使用模型: {model}");

// W4 各节共用一个正常装配的 IChatClient（W3S6 自己造错误 client，不用这个）
IChatClient client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
                        .GetChatClient(model).AsIChatClient();

switch (target)
{
    case "w3s6":
        await W3S6_异常处理.Run(baseUrl, model, apiKey);
        break;

    case "w4s1":
        await W4S1_多轮对话.Run(client);
        break;

    case "w4s2":
        await W4S2_流式输出.Run(client);
        break;

    case "w4s2b":
        await W4S2b_思考可见.Probe(baseUrl, apiKey, model);
        break;

    case "w4s2c":
        await W4S2b_思考可见.Run(baseUrl, apiKey, model);
        break;

    case "w4s3":
        await W4S3_中断与取消.Run(client);
        break;

    case "w4s4":
        await W4S4_token统计与截断.Run(client);
        break;

    case "w4s5":
        await W4S5_聊天程序.Run(baseUrl, apiKey, model);
        break;

    case "w4s6":
        await W4S6_异常与重试.Run();
        break;

    case "w5s1":
        await W5S1_严格JSON.Run(client);
        break;

    case "w5s2":
        await W5S2_脏数据兜底.Run(client);
        break;

    case "w5s3":
        await W5S3_函数调用.Run(client);
        break;

    case "w5s4":
        await W5S4_真实工具_读文件.Run(client);
        break;

    case "w5s5":
        await W5S5_多工具并行.Run(client);
        break;

    case "w5s6":
        await W5S6_强制调用.Run(client);
        break;

    case "w6s1":
        await W6S1_嵌入直觉.Run(baseUrl, apiKey, "qwen3.7-text-embedding-flash");
        break;

    case "w6s2":
        await W6S2_向量结构.Run(baseUrl, apiKey, "qwen3.7-text-embedding-flash");
        break;

    default:
        Console.WriteLine($"未知目标 '{target}'。可用: w3s6, w4s1..w4s6, w5s1..w5s6, w6s1, w6s2, w6s4, w6s4-test");
        break;
}
