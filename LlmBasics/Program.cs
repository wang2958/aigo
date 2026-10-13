using System.Text;
using System.ClientModel;                 // ApiKeyCredential
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

// 装配：读配置（appsettings 非密 + user-secrets 里的 key），构建一个 IChatClient
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();
var baseUrl = config["Qwen:BaseUrl"]!;
var model = config["Qwen:Model"]!;
var apiKey = config["Qwen:ApiKey"]!;

// 分发：跑哪一节由命令行参数决定
//   dotnet run -- w4s1                （默认，W4 第一节：多轮对话/无状态）
//   dotnet run -- w4s2                （W4 第二节：流式输出 SSE，用 appsettings 里的模型）
//   dotnet run -- w4s2 qwen3.8-flash  （第二个参数=临时覆盖模型，方便跨模型对比）
//   dotnet run -- w3s6                （回看 W3 第六节：异常处理）
var target = (args.Length > 0 ? args[0] : "w4s1").ToLowerInvariant();
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

    default:
        Console.WriteLine($"未知目标 '{target}'。可用: w3s6, w4s1, w4s2, w4s2b, w4s2c, w4s3");
        break;
}
