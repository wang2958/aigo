using Microsoft.Extensions.AI;

// W4 第一节：多轮对话 & 模型无状态
// 目标：用两次真实调用证明——模型不记得上一次说了什么，"记忆"完全靠我们每次把 history 重新发过去。
public static class W4S1_多轮对话
{
    public static async Task Run(IChatClient client)
    {
        // 我们自己维护的对话历史。这个 List 就是模型唯一的"记忆"载体。
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "你是一个回答极简的助手，一句话以内。")
        };

        // 第 1 轮：把一个事实告诉模型
        const string fact = "记住：我叫小明，我最喜欢的编程语言是 C#。";
        history.Add(new ChatMessage(ChatRole.User, fact));
        var r1 = await client.GetResponseAsync(history);
        history.Add(new ChatMessage(ChatRole.Assistant, r1.Text)); // 关键：把 AI 的回复也塞回 history
        Console.WriteLine($"[我] {fact}");
        Console.WriteLine($"[AI] {r1.Text}\n");

        const string question = "我最喜欢的编程语言是什么？";

        // 实验 A：全新调用，只发问题、不带 history —— 模型"失忆"
        Console.WriteLine("--- 实验A：不带历史，直接问 ---");
        var amnesia = new List<ChatMessage> { new(ChatRole.User, question) };
        var ra = await client.GetResponseAsync(amnesia);
        Console.WriteLine($"[AI] {ra.Text}\n");

        // 实验 B：带上完整 history 再问同一个问题 —— 模型"记得"
        Console.WriteLine("--- 实验B：带上 history 再问 ---");
        history.Add(new ChatMessage(ChatRole.User, question));
        var rb = await client.GetResponseAsync(history);
        history.Add(new ChatMessage(ChatRole.Assistant, rb.Text));
        Console.WriteLine($"[AI] {rb.Text}\n");

        // 把实验 B 实际发出去的 history 打出来：这就是模型看到的全部，一个字都不多
        Console.WriteLine("--- 实验B 实际发送的 history（模型看到的全部）---");
        foreach (var m in history)
            Console.WriteLine($"  [{m.Role}] {m.Text}");
    }
}
