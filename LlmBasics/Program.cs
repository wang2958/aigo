using System.ClientModel;                 // ClientResultException / ApiKeyCredential
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();
var baseUrl = config["Qwen:BaseUrl"]!;
var model = config["Qwen:Model"]!;
var apiKey = config["Qwen:ApiKey"]!;

// 第六节：异常处理 —— 分层捕获，把失败翻译成可读信息
static IChatClient BuildClient(string key, string url, string mdl) =>
    new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri(url) })
        .GetChatClient(mdl).AsIChatClient();

static async Task SafeCall(string label, Func<Task> call)
{
    Console.WriteLine($"\n--- {label} ---");
    try
    {
        await call();
        Console.WriteLine("✅ 成功（本不该成功）");
    }
    catch (ClientResultException ex)   // OpenAI SDK 把 HTTP 错误包成这个
    {
        Console.WriteLine($"❌ ClientResultException | HTTP {ex.Status}");
        Console.WriteLine($"   消息: {ex.Message}");
    }
    catch (HttpRequestException ex)   // 网络层：DNS/连接/超时
    {
        Console.WriteLine($"❌ HttpRequestException（网络层）: {ex.Message}");
    }
    catch (Exception ex)              // 兜底：别让它裸崩
    {
        Console.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
    }
}

// 故意触发两类错误，观察真实返回
await SafeCall("错误模型名 qwen-does-not-exist", async () =>
{
    var r = await BuildClient(apiKey, baseUrl, "qwen-does-not-exist")
        .GetResponseAsync(new ChatMessage(ChatRole.User, "hi"));
    Console.WriteLine(r.Text);
});

await SafeCall("错误 API key", async () =>
{
    var r = await BuildClient("sk-this-key-is-invalid", baseUrl, model)
        .GetResponseAsync(new ChatMessage(ChatRole.User, "hi"));
    Console.WriteLine(r.Text);
});
