namespace Wcalss.AmbientBrightness;

/// <summary>
/// 自檢模式（--selftest）：把單元測試直接內建在主專案裡，維持「只有一個 WcalssAmbientBrightness 資料夾」的專案結構，
/// 不需要獨立的測試專案與測試框架。涵蓋三部分：
/// 1. 新邏輯：SampleSmoother（自適應 EMA）、SamplePacing（自適應取樣間隔）、LuminanceStability（取樣窗提前結束判定）
/// 2. BrightnessMapper 既有行為的回歸測試：雙重確認、遲滯（含 12.4 節修過的負邊界 bug）、單次突波過濾
/// 3. AsyncGuard：卡住的非同步工作逾時後不阻擋後續資源釋放（§13.3／§16 的釋放路徑加固）
/// 全部通過回傳 0（exit code），任一失敗回傳 1 並列出失敗項目，可用於 CI 或接手 agent 的快速驗證。
/// </summary>
internal static class SelfTest
{
    private sealed class CheckFailedException(string message) : Exception(message);

    public static int RunAll(TextWriter output)
    {
        var failures = new List<string>();
        var total = 0;

        void Check(string name, Action action)
        {
            total++;
            try
            {
                action();
                output.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                output.WriteLine($"FAIL  {name} — {ex.Message}");
            }
        }

        // ── BrightnessMapper 既有行為回歸 ──

        Check("BrightnessMapper: 第一次評估立即回傳目前分級", () =>
        {
            var mapper = CreateDefaultMapper();
            var band = mapper.Evaluate(0.4597);
            Assert(band is not null && band.Label == "有開燈", $"label={band?.Label}");
        });

        Check("BrightnessMapper: 往上切換需要連續兩次確認", () =>
        {
            var mapper = CreateDefaultMapper();
            mapper.Evaluate(0.0005); // 建立「暗」為目前分級
            Assert(mapper.Evaluate(0.4597) is null, "第一次跳亮不應切換");
            var second = mapper.Evaluate(0.4597);
            Assert(second is not null && second.Label == "有開燈", $"第二次應切換，實際 {second?.Label ?? "null"}");
        });

        Check("BrightnessMapper: 往下切換在門檻小於遲滯時仍可行（12.4 節 fix 回歸）", () =>
        {
            var mapper = CreateDefaultMapper();
            mapper.Evaluate(0.4597); // 有開燈
            Assert(mapper.Evaluate(0.003) is null, "第一次確認不應切換");
            var third = mapper.Evaluate(0.003);
            Assert(third is not null && third.Label == "暗（無光）", $"第三次應切到暗，實際 {third?.Label ?? "null"}");
        });

        Check("BrightnessMapper: 單次突波不會切換分級", () =>
        {
            var mapper = CreateDefaultMapper();
            mapper.Evaluate(0.0005); // 暗
            Assert(mapper.Evaluate(0.076) is null, "突波第一筆不應切換");
            Assert(mapper.Evaluate(0.0005) is null, "回到暗範圍不應有任何切換");
        });

        // ── SampleSmoother（自適應 EMA）──

        Check("SampleSmoother: 大落差用強 α=0.9", () =>
        {
            var smoother = new SampleSmoother();
            var smoothed = smoother.Smooth(0.46, 0.0002, out var alphaUsed);
            Assert(alphaUsed == 0.9, $"alpha={alphaUsed}");
            AreEqual(0.46 + 0.9 * (0.0002 - 0.46), smoothed!.Value, 1e-9);
        });

        Check("SampleSmoother: 小擾動維持 α=0.5", () =>
        {
            var smoother = new SampleSmoother();
            var smoothed = smoother.Smooth(0.46, 0.41, out var alphaUsed);
            Assert(alphaUsed == 0.5, $"alpha={alphaUsed}");
            AreEqual(0.435, smoothed!.Value, 1e-9);
        });

        Check("SampleSmoother: 落差恰為門檻 0.1 也用強 α", () =>
        {
            var smoother = new SampleSmoother();
            smoother.Smooth(0.2, 0.1, out var alphaUsed);
            Assert(alphaUsed == 0.9, $"alpha={alphaUsed}");
        });

        Check("SampleSmoother: 關燈轉換三次取樣內跨過暗的門檻", () =>
        {
            // 對照原本固定 α=0.5：0.46 → 0.23 → 0.115 → 0.0576 → … 要 7 次以上
            var smoother = new SampleSmoother();
            var ema = smoother.Smooth(null, 0.46, out _);   // 初始化：ema 直接採用第一筆讀值
            ema = smoother.Smooth(ema, 0.0002, out _);      // α=0.9 → 0.0462
            ema = smoother.Smooth(ema, 0.0002, out _);      // 尾窗強 α → 0.0048
            Assert(ema!.Value < 0.01, $"ema={ema}");
        });

        Check("SampleSmoother: 強 α 持續一次尾窗，之後退回 α=0.5", () =>
        {
            // 觸發那次（大落差）+ 尾窗額外一次；第 2 次取樣已把 ema 壓到 0.0048、跨過暗的門檻。
            var smoother = new SampleSmoother();
            smoother.Smooth(null, 0.46, out _);
            smoother.Smooth(0.46, 0.0002, out var first);
            Assert(first == 0.9, $"first={first}");
            smoother.Smooth(0.04618, 0.0002, out var second);
            Assert(second == 0.9, $"second alpha={second}");
            smoother.Smooth(0.004798, 0.0002, out var third);
            Assert(third == 0.5, $"third alpha={third}");
        });

        Check("SampleSmoother: 暗態單次高突波快速拉回、不殘留", () =>
        {
            var smoother = new SampleSmoother();
            var ema = smoother.Smooth(null, 0.0005, out _);
            ema = smoother.Smooth(ema, 0.31, out _);        // 單次突波 → ema 被拉到 0.279
            ema = smoother.Smooth(ema, 0.0005, out var back);
            Assert(back == 0.9, $"拉回也是大落差，alpha={back}");
            ema = smoother.Smooth(ema, 0.0005, out _);      // 尾窗強 α
            Assert(ema!.Value < 0.01, $"ema={ema}");
        });

        // ── SamplePacing（自適應取樣間隔）──

        Check("SamplePacing: 穩定且遠離邊界用慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.4597);
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 大落差觸發快間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.23);
            Assert(pacing.NextIntervalMs() == 500, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 逼近分級邊界觸發快間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.012, smoothed: 0.012); // 距「暗」邊界 0.01 僅 0.002
            Assert(pacing.NextIntervalMs() == 500, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 取樣失敗退回慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.01); // 進入快模式
            pacing.OnSample(success: false, raw: 0, smoothed: 0);
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 快模式達上限強制一輪慢取樣，之後可重新進入", () =>
        {
            var pacing = CreatePacing(fastCyclesCapacity: 3);
            for (var i = 0; i < 3; i++)
            {
                pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.20);
                Assert(pacing.NextIntervalMs() == 500, $"第 {i + 1} 次應為快間隔");
            }

            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.20);
            Assert(pacing.NextIntervalMs() == 5000, "超過上限應強制慢取樣");

            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.20);
            Assert(pacing.NextIntervalMs() == 500, "之後仍符合條件應再次進入快模式");
        });

        Check("SamplePacing: 讀值回穩後退回慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.01);   // 快模式
            pacing.OnSample(success: true, raw: 0.4597, smoothed: 0.4597); // 已穩定
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        // ── LuminanceStability（取樣窗提前結束判定）──

        Check("LuminanceStability: 穩定窗回傳 true", () =>
        {
            double[] values = [0.4597, 0.4598, 0.4597, 0.4596, 0.4597, 0.4598];
            Assert(LuminanceStability.IsStable(values, minimumCount: 6, tolerance: 0.01));
        });

        Check("LuminanceStability: 幀數不足回傳 false", () =>
        {
            double[] values = [0.4597, 0.4598];
            Assert(!LuminanceStability.IsStable(values, minimumCount: 6, tolerance: 0.01));
        });

        Check("LuminanceStability: 讀值範圍過寬（收斂中）回傳 false", () =>
        {
            double[] values = [0.40, 0.44, 0.45, 0.46, 0.46, 0.46];
            Assert(!LuminanceStability.IsStable(values, minimumCount: 6, tolerance: 0.01));
        });

        // ── AsyncGuard（釋放路徑不被卡住的 StopAsync 阻擋，對應 §13.3／§16 修復）──

        Check("AsyncGuard: 工作即時完成回報 Completed、無例外", () =>
        {
            var result = AsyncGuard.RunAsync(() => Task.CompletedTask, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            Assert(result.Completed && result.Error is null, $"completed={result.Completed}, error={result.Error?.GetType().Name ?? "null"}");
        });

        Check("AsyncGuard: 工作卡住時逾時返回、不拋例外、不等到工作結束", () =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = AsyncGuard.RunAsync(() => Task.Delay(TimeSpan.FromSeconds(10)), TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();
            stopwatch.Stop();
            Assert(!result.Completed, "逾時應回報未完成");
            Assert(stopwatch.ElapsedMilliseconds < 3000, $"不應阻塞到工作自己結束，實際 {stopwatch.ElapsedMilliseconds}ms");
        });

        Check("AsyncGuard: 底層工作拋例外仍算已完成（不是卡住），並帶回例外", () =>
        {
            var result = AsyncGuard.RunAsync(() => Task.FromException(new InvalidOperationException("boom")), TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            Assert(result.Completed, "拋例外也算結束");
            Assert(result.Error is InvalidOperationException, $"error={result.Error?.GetType().Name ?? "null"}");
        });

        output.WriteLine($"\n共 {total} 項：通過 {total - failures.Count}，失敗 {failures.Count}");
        foreach (var failure in failures)
        {
            output.WriteLine($"  失敗：{failure}");
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static BrightnessMapper CreateDefaultMapper() =>
        new(BrightnessMapper.DefaultBands, hysteresis: 0.02);

    private static SamplePacing CreatePacing(int fastCyclesCapacity = 30) =>
        new(
            boundaries: [0.01, 0.20],
            slowIntervalMs: 5000,
            fastIntervalMs: 500,
            deltaThreshold: 0.03,
            boundaryMargin: 0.05,
            maxFastCycles: fastCyclesCapacity);

    private static void Assert(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new CheckFailedException(message ?? "斷言失敗");
        }
    }

    private static void AreEqual(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new CheckFailedException($"expected={expected}, actual={actual}");
        }
    }
}