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
//   dotnet run -- w4s1   （默认，W4 第一节：多轮对话/无状态）
//   dotnet run -- w3s6   （回看 W3 第六节：异常处理）
var target = (args.Length > 0 ? args[0] : "w4s1").ToLowerInvariant();

switch (target)
{
    case "w3s6":
        await W3S6_异常处理.Run(baseUrl, model, apiKey);
        break;

    case "w4s1":
        IChatClient client = new OpenAIClient(new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
            .GetChatClient(model).AsIChatClient();
        await W4S1_多轮对话.Run(client);
        break;

    default:
        Console.WriteLine($"未知目标 '{target}'。可用: w3s6, w4s1");
        break;
}
