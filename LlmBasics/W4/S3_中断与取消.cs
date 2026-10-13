using System.Diagnostics;
using Microsoft.Extensions.AI;

// W4 第三节：流式下的中断与取消（CancellationToken）
// 目标：让用户能"中途喊停"，并且取消要真正传到 HTTP 层关闭连接——否则服务器还在生成、还在烧 token。
public static class W4S3_中断与取消
{
    public static async Task Run(IChatClient client)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个助手。"),
            new(ChatRole.User, "写一篇尽量长的短文（300字以上），详细介绍什么是递归、它的应用和注意事项。")
        };

        using var cts = new CancellationTokenSource();
        // 模拟用户在 3500ms 后按下"停止"（推理模型前 ~3s 在思考，故留到正文开始流之后再取消）
        cts.CancelAfter(TimeSpan.FromMilliseconds(3500));

        int chars = 0;
        var sw = Stopwatch.StartNew();
        Console.WriteLine("=== 流式生成中，3500ms 后自动取消 ===\n");
        try
        {
            // 关键：把 cts.Token 一路传进去，取消才能贯穿到 HTTP 层
            await foreach (var update in client.GetStreamingResponseAsync(history, cancellationToken: cts.Token))
            {
                Console.Write(update.Text);
                chars += update.Text?.Length ?? 0;
            }
            Console.WriteLine($"\n\n[正常结束] 未被取消，共 {chars} 字，耗时 {sw.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException)
        {
            // 取消会在这里以异常形式冒出——这是预期的，不是错误
            Console.WriteLine($"\n\n[已取消] 在 {sw.ElapsedMilliseconds} ms、已收到 {chars} 字时中断");
            Console.WriteLine("[已取消] 连接已关闭，服务器应停止生成（不再计费）");
        }
    }
}
