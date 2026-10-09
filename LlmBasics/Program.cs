using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;          // IChatClient 抽象
using OpenAI;                           // OpenAIClient
using System.ClientModel;                 // ApiKeyCredential

Console.OutputEncoding = Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();

var baseUrl = config["Qwen:BaseUrl"]!;
var model = config["Qwen:Model"]!;
var apiKey = config["Qwen:ApiKey"]!;

const string Question = "用一句话说明 IChatClient 相比裸 HttpClient 的好处";

// ══════════════════════════════════════════════════════════
// 写法 A：裸 HttpClient —— 什么都能看见，但什么都得自己拼
// ══════════════════════════════════════════════════════════
Console.WriteLine("########## A. 裸 HttpClient ##########");
{
    var body = JsonSerializer.Serialize(new
    {
        model,
        messages = new[] { new { role = "user", content = Question } }
    });
    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    var resp = await http.PostAsync($"{baseUrl}/chat/completions", content);
    var raw = await resp.Content.ReadAsStringAsync();

    // 想要答案，得自己挖 JSON 路径
    using var doc = JsonDocument.Parse(raw);
    var root = doc.RootElement;
    var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    var total = root.GetProperty("usage").GetProperty("total_tokens").GetInt32();
    Console.WriteLine($"HTTP {(int)resp.StatusCode} | total_tokens={total}（手动从 JSON 挖）");
    Console.WriteLine($"答案: {text}");
}

// ══════════════════════════════════════════════════════════
// 写法 B：IChatClient 抽象 —— 强类型、可换供应商、可加中间件
// ══════════════════════════════════════════════════════════
Console.WriteLine("\n########## B. IChatClient 抽象 ##########");
{
    // OpenAI 兼容端点：把 Endpoint 指向中转地址即可
    var openai = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

    IChatClient chat = openai.GetChatClient(model).AsIChatClient();

    // 一行拿到强类型结果，不用手挖 JSON
    var response = await chat.GetResponseAsync(new ChatMessage(ChatRole.User, Question));

    Console.WriteLine($"答案: {response.Text}");
    Console.WriteLine($"usage: in={response.Usage?.InputTokenCount} out={response.Usage?.OutputTokenCount} total={response.Usage?.TotalTokenCount}（强类型，直接点出来）");
    Console.WriteLine($"finish: {response.FinishReason} | model: {response.ModelId}");
}

// ══════════════════════════════════════════════════════════
// 写法 C：异常处理 —— 故意触发「模型名错」「key 错」，看返回什么
// ══════════════════════════════════════════════════════════
Console.WriteLine("\n########## C. 异常处理 ##########");

// 通用调用+兜底：把不同类型的失败翻译成可读信息
static async Task SafeCall(string label, Func<Task> call)
{
    Console.WriteLine($"\n--- {label} ---");
    try
    {
        await call();
        Console.WriteLine("✅ 成功（本不该成功）");
    }
    catch (ClientResultException ex)   // OpenAI SDK 的 HTTP 错误封装
    {
        Console.WriteLine($"❌ ClientResultException | HTTP {ex.Status}");
        Console.WriteLine($"   消息: {ex.Message}");
    }
    catch (HttpRequestException ex)   // 网络层（DNS/连接/超时）
    {
        Console.WriteLine($"❌ HttpRequestException（网络层）: {ex.Message}");
    }
    catch (Exception ex)              // 兜底：别让它裸崩
    {
        Console.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
    }
}

// C1：错误的模型名
await SafeCall("错误模型名 qwen-does-not-exist", async () =>
{
    var openai = new OpenAIClient(new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
    IChatClient chat = openai.GetChatClient("qwen-does-not-exist").AsIChatClient();
    var r = await chat.GetResponseAsync("hi");
    Console.WriteLine(r.Text);
});

// C2：错误的 key
await SafeCall("错误 API key", async () =>
{
    var openai = new OpenAIClient(new ApiKeyCredential("sk-this-key-is-invalid"),
        new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
    IChatClient chat = openai.GetChatClient(model).AsIChatClient();
    var r = await chat.GetResponseAsync("hi");
    Console.WriteLine(r.Text);
});
