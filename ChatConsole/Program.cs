using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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

IChatClient chat = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
    .GetChatClient(model)
    .AsIChatClient();

// ── W4 核心认知：模型是"无状态"的 ──────────────────────────
// 服务端不记得上一轮。每次调用都要把完整历史 messages[] 重新发过去，
// 它才"看起来记得"。history 就是由客户端自己维护的"记忆"。
var history = new List<ChatMessage>
{
    new(ChatRole.System, "你是简洁的助手，回答控制在两句以内。")
};

const int MaxHistoryTokens = 800;   // 简单上下文预算（估算值）

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("\n[Ctrl+C 已请求取消]"); };

Console.WriteLine("连续对话 | /exit 退出 · /clear 清空 · /stat 看历史");

while (true)
{
    Console.Write("\n你> ");
    var line = Console.ReadLine();
    if (line is null) break;                 // 管道输入 EOF
    line = line.Trim();
    if (line.Length == 0) continue;
    if (line is "/exit" or "/quit") break;
    if (line == "/clear") { history.RemoveRange(1, history.Count - 1); Console.WriteLine("[历史已清空，仅留 system]"); continue; }
    if (line == "/stat") { Console.WriteLine($"[历史 {history.Count} 条，约 {EstimateTokens(history)} tokens(估)]"); continue; }

    history.Add(new ChatMessage(ChatRole.User, line));
    Truncate(history, MaxHistoryTokens);      // 超预算就丢最旧对话，保留 system

    Console.Write("AI> ");
    var answer = new StringBuilder();
    try
    {
        await StreamAsync(chat, history, answer, cts.Token);   // 流式(SSE)，边到边打印
        Console.WriteLine();
        history.Add(new ChatMessage(ChatRole.Assistant, answer.ToString()));
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\n[本轮已取消]");
        history.RemoveAt(history.Count - 1);  // 回滚这条没答完的 user 消息
    }
}

// ── 估算 token（粗略：统一按 字符数×0.6，仅用于演示截断阈值）──
static int EstimateTokens(List<ChatMessage> msgs)
    => (int)(msgs.Sum(m => m.Text?.Length ?? 0) * 0.6);

// ── 上下文截断：超预算就从最旧对话丢起，永远保留 [0] 的 system ──
static void Truncate(List<ChatMessage> msgs, int budget)
{
    while (EstimateTokens(msgs) > budget && msgs.Count > 2)
        msgs.RemoveAt(1);   // index 0 是 system，不动
}

// ── 流式 + 异常重试：429 限流 / 5xx → 指数退避(1s,2s,4s) ──
// 注意：流式重试的固有问题——已打印到控制台的字符无法撤回，只能清 sink 重来。
//       429/5xx 通常在"建流前"就抛，此时还没输出，重试是干净的。
static async Task StreamAsync(IChatClient client, List<ChatMessage> msgs, StringBuilder sink,
    CancellationToken ct, int maxRetry = 3)
{
    for (int attempt = 0; ; attempt++)
    {
        try
        {
            await foreach (var u in client.GetStreamingResponseAsync(msgs, cancellationToken: ct))
            {
                var t = u.Text;
                if (!string.IsNullOrEmpty(t)) { Console.Write(t); sink.Append(t); }
            }
            return;
        }
        catch (ClientResultException ex) when ((ex.Status == 429 || ex.Status >= 500) && attempt < maxRetry)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            Console.Write($"\n[{ex.Status} 重试 {attempt + 1}/{maxRetry}，退避 {delay.TotalSeconds}s]");
            sink.Clear();
            await Task.Delay(delay, ct);
        }
    }
}
