using System.Diagnostics;

// W4 第六节：异常与重试 —— 超时、429 限流、指数退避。
// 用"本地模拟的 HTTP 异常"验证重试机制，不拿真 API 当靶子（省钱、避免真被限流）。
public static class W4S6_异常与重试
{
    // 模拟带 HTTP 状态码的异常
    class FakeHttpException : Exception
    {
        public int Status { get; }
        public FakeHttpException(int status, string msg) : base(msg) => Status = status;
    }

    // 重试分类（W3 学的）：429/408/5xx 是瞬态→可重试；401/404/400 是永久→别重试
    static bool IsTransient(int status) => status == 408 || status == 429 || status >= 500;

    static readonly Random Rnd = new();

    public static async Task Run()
    {
        const int maxAttempts = 4;
        const int baseDelay = 200;   // ms，小一点让演示跑得快

        // ===== 演示1：429 限流，失败2次后成功 → 指数退避 + 抖动 =====
        Console.WriteLine("=== 演示1：429 瞬态，第3次成功（指数退避+抖动）===");
        int n1 = 0;
        await WithRetry(async () =>
        {
            await Task.Delay(30);
            if (++n1 < 3) throw new FakeHttpException(429, "rate limited");
        }, maxAttempts, baseDelay, "429");

        // ===== 演示2：401 永久错误 → 不重试，快速失败 =====
        Console.WriteLine("\n=== 演示2：401 永久错误 → 不重试 ===");
        try
        {
            await WithRetry(() => throw new FakeHttpException(401, "invalid api key"),
                maxAttempts, baseDelay, "401");
        }
        catch (FakeHttpException) { /* 预期：永久错误直接抛出 */ }

        // ===== 演示3：503 一直失败 → 重试到上限后放弃 =====
        Console.WriteLine("\n=== 演示3：503 持续失败 → 触顶放弃 ===");
        try
        {
            await WithRetry(() => throw new FakeHttpException(503, "service unavailable"),
                maxAttempts, baseDelay, "503");
        }
        catch (FakeHttpException ex)
        {
            Console.WriteLine($"  [503] ❌ 重试 {maxAttempts} 次仍失败，放弃：{ex.Message}");
        }

        // ===== 演示4：超时（用 CTS.CancelAfter 实现真超时）=====
        Console.WriteLine("\n=== 演示4：操作超时 → 当作瞬态重试 ===");
        int n4 = 0;
        await WithRetry(async () =>
        {
            using var timeoutCts = new CancellationTokenSource();
            timeoutCts.CancelAfter(300);            // 300ms 超时
            try
            {
                // 前两次"卡住"超过超时；第三次秒回
                await Task.Delay(++n4 < 3 ? 2000 : 10, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // 超时（不是用户取消）→ 翻译成瞬态错误交给重试器
                throw new FakeHttpException(408, "request timeout");
            }
        }, maxAttempts, baseDelay, "超时");
    }

    // 指数退避重试器：delay = base * 2^(attempt-1) + 随机抖动
    static async Task WithRetry(Func<Task> op, int maxAttempts, int baseDelayMs, string label)
    {
        int attempt = 0;
        var sw = Stopwatch.StartNew();
        while (true)
        {
            attempt++;
            try
            {
                Console.WriteLine($"  [{label}] 第 {attempt} 次尝试 @ {sw.ElapsedMilliseconds}ms");
                await op();
                Console.WriteLine($"  [{label}] ✅ 成功（共 {attempt} 次）");
                return;
            }
            // 瞬态 + 还有次数 → 退避重试
            catch (FakeHttpException ex) when (IsTransient(ex.Status) && attempt < maxAttempts)
            {
                int backoff = (int)(baseDelayMs * Math.Pow(2, attempt - 1));  // 200,400,800...
                int jitter = Rnd.Next(0, baseDelayMs);                         // 0~200 随机
                Console.WriteLine($"  [{label}] ⚠ 瞬态 HTTP {ex.Status}，{backoff + jitter}ms 后重试（退避{backoff} + 抖动{jitter}）");
                await Task.Delay(backoff + jitter);
            }
            // 永久错误 → 不重试，直接抛（fail fast）
            catch (FakeHttpException ex) when (!IsTransient(ex.Status))
            {
                Console.WriteLine($"  [{label}] ❌ 永久 HTTP {ex.Status}，不重试：{ex.Message}");
                throw;
            }
            // 瞬态但已到上限 → 抛出（触顶放弃）
        }
    }
}
