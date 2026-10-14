using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

// W5 第一节：严格 JSON 输出（response_format / json_schema）+ 反序列化到 record
// 对比 W2：那时在 prompt 里"求"模型返回 JSON（会翻车）；这里用 API 层 response_format 从采样阶段强制。
public static class W5S1_严格JSON
{
    public enum Priority { Low, Medium, High, Urgent }

    // 目标结构：record（不可变、值语义，天生适合当 DTO）
    public record TicketInfo(
        string Title,
        Priority Priority,
        string[] Tags,
        string Summary);

    // schema 生成 与 反序列化 用同一套选项，保证 enum 都按"字符串"处理
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task Run(IChatClient client)
    {
        var messy = "帮个忙，生产环境登录接口从今天凌晨一直报 500，用户大面积登录不了，" +
                    "日志全是 database timeout，已经影响下单了，赶紧的！！涉及 auth 和 db 两块。";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是工单抽取器，从用户文本中抽取结构化工单信息。"),
            new(ChatRole.User, messy)
        };

        // json_schema 模式：从 record 类型自动生成 schema，约束模型必须按此结构输出
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(typeof(TicketInfo), JsonOpts, schemaName: "ticket")
        };

        var resp = await client.GetResponseAsync(messages, options);

        Console.WriteLine("=== 模型返回的原始文本 ===");
        Console.WriteLine(resp.Text);

        Console.WriteLine("\n=== 反序列化到强类型 record（仍要 try-catch 兜底）===");
        try
        {
            var ticket = JsonSerializer.Deserialize<TicketInfo>(resp.Text ?? "", JsonOpts);
            if (ticket is null) { Console.WriteLine("❌ 解析得到 null"); return; }
            Console.WriteLine($"Title    : {ticket.Title}");
            Console.WriteLine($"Priority : {ticket.Priority}   (枚举，越界值会被反序列化拦下)");
            Console.WriteLine($"Tags     : {string.Join(", ", ticket.Tags ?? [])}");
            Console.WriteLine($"Summary  : {ticket.Summary}");
        }
        catch (JsonException ex)
        {
            // 即便有 schema，也不能假设一定成功：中转可能不支持、模型可能不守约
            Console.WriteLine($"❌ 反序列化失败：{ex.Message}");
        }
    }
}
