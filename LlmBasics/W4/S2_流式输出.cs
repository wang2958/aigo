using System.Diagnostics;
using Microsoft.Extensions.AI;

// W4 第二节：流式输出（SSE）—— 边生成边打印，而不是等整段生成完再一次性返回
public static class W4S2_流式输出
{
    public static async Task Run(IChatClient client)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个简洁的助手。"),
            new(ChatRole.User, "用三句话解释什么是流式输出，以及它为什么能改善用户体验。")
        };

        // 对照组：非流式（GetResponseAsync）——服务器生成完整段后一次性返回
        Console.WriteLine("=== 非流式：一次性拿到整段（对照） ===");
        var sw = Stopwatch.StartNew();
        var full = await client.GetResponseAsync(history);
        sw.Stop();
        Console.WriteLine(full.Text);
        Console.WriteLine($"[非流式] 等待 {sw.ElapsedMilliseconds} ms 后，整段一次性出现\n");

        // 实验组：流式（GetStreamingResponseAsync）——每生成一小片就推给你
        Console.WriteLine("=== 流式：边生成边打印 ===");
        sw.Restart();
        long firstTokenMs = -1;
        await foreach (var update in client.GetStreamingResponseAsync(history))
        {
            if (firstTokenMs < 0 && !string.IsNullOrEmpty(update.Text))
            {
                firstTokenMs = sw.ElapsedMilliseconds;
                Console.WriteLine($"[流式] 首字延迟 {firstTokenMs} ms，开始逐字输出：");
            }
            Console.Write(update.Text);   // 用 Write 不用 WriteLine，碎片才能连成一句
        }
        sw.Stop();
        Console.WriteLine($"\n[流式] 全部完成，总耗时 {sw.ElapsedMilliseconds} ms");
    }
}
