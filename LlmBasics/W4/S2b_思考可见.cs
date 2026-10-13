using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// W4 第二节·补充实验：让"思考"可见。
// 第一步是探针——裸调流式接口，打印原始 SSE，亲眼确认 reasoning_content 的真实字段路径，
// 而不是猜（W3 扩展7：抽象会吃掉供应商特有字段，需要时回退裸调）。
public static class W4S2b_思考可见
{
    public static async Task Probe(string baseUrl, string apiKey, string model)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            // 用纯 ASCII prompt，避开 Git Bash/控制台的中文编码干扰
            messages = new[] { new { role = "user", content = "Explain recursion in one short sentence." } }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        // ResponseHeadersRead：拿到响应头就返回，body 边到边读（流式的关键）
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        Console.WriteLine($"HTTP {(int)resp.StatusCode}  Content-Type={resp.Content.Headers.ContentType}");

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        int n = 0;
        while (n < 25)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;                 // 流结束
            if (string.IsNullOrWhiteSpace(line)) continue;  // SSE 事件之间的空行，跳过
            Console.WriteLine(line);
            n++;
        }
    }

    // 干净版：解析 SSE，把"思考(reasoning_content)"和"正文(content)"分区显示，末尾打印 usage
    public static async Task Run(string baseUrl, string apiKey, string model)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            messages = new[] { new { role = "user", content = "用三句话解释什么是递归。请多思考再回答. " } }
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        bool inThinking = false, inAnswer = false;
        string? usageJson = null;

        Console.WriteLine("=== 思考可见：[思考]=reasoning_content  [回答]=content ===\n");
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;                       // 流结束
            if (!line.StartsWith("data:")) continue;       // 跳过空行
            var payload = line.Substring("data:".Length).Trim();
            if (payload == "[DONE]") break;                // SSE 终止哨兵

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // usage 只在最后一个 chunk 出现（实测：本中转默认返回，无需 include_usage）
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                usageJson = u.GetRawText();

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            var delta = choices[0].GetProperty("delta");

            // 思考片段：先流
            if (delta.TryGetProperty("reasoning_content", out var rc)
                && rc.ValueKind == JsonValueKind.String
                && rc.GetString() is { Length: > 0 } think)
            {
                if (!inThinking) { Console.Write("[思考] "); inThinking = true; }
                Console.Write(think);
            }
            // 正文片段：思考结束后才流
            if (delta.TryGetProperty("content", out var ct)
                && ct.ValueKind == JsonValueKind.String
                && ct.GetString() is { Length: > 0 } answer)
            {
                if (inThinking && !inAnswer) Console.Write("\n\n");   // 思考→正文，换行分隔
                if (!inAnswer) { Console.Write("[回答] "); inAnswer = true; }
                Console.Write(answer);
            }
        }

        Console.WriteLine("\n\n=== 完成 ===");
        if (usageJson is not null) Console.WriteLine($"usage: {usageJson}");
    }
}
