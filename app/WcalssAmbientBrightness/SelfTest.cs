namespace Wcalss.AmbientBrightness;

/// <summary>
/// 自檢模式（--selftest）：單元測試內建在主專案裡，不另開測試專案。涵蓋純邏輯元件——
/// SampleSmoother、SamplePacing、LuminanceStability、BrightnessMapper（雙重確認／遲滯／突波過濾）、
/// AsyncGuard（釋放路徑不被卡住的 StopAsync 阻擋）、CameraCompatibility（裝置比對／格式挑選）。
/// 全數通過回傳 0，任一失敗回傳 1 並列出，可用於 CI 或接手驗證。
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

        Check("SamplePacing: 靠近分級邊界但持平用慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.012, smoothed: 0.012); // 距「暗」邊界 0.01 僅 0.002
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 穩定關燈用慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.0005, smoothed: 0.0005);
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 穩定微光用慢間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.022, smoothed: 0.022);
            Assert(pacing.NextIntervalMs() == 5000, $"interval={pacing.NextIntervalMs()}");
        });

        Check("SamplePacing: 從關燈往微光移動觸發快間隔", () =>
        {
            var pacing = CreatePacing();
            pacing.OnSample(success: true, raw: 0.022, smoothed: 0.005);
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

        // ── CameraCompatibility：換相機時的裝置比對與原生格式挑選（§17）──

        Check("ResolveDevice: 完全同名（大小寫不敏感）優先", () =>
        {
            var match = CameraCompatibility.ResolveDevice(["Integrated Camera", "USB Camera"], "usb camera");
            Assert(match.Found && match.Index == 1 && match.Kind == CameraCompatibility.DeviceMatchKind.ExactName, match.Note);
        });

        Check("ResolveDevice: 同名找不到但唯一部分符合就採用", () =>
        {
            var match = CameraCompatibility.ResolveDevice(["Integrated Camera", "HD Pro Webcam C920"], "C920");
            Assert(match.Found && match.Index == 1 && match.Kind == CameraCompatibility.DeviceMatchKind.UniquePartialName, match.Note);
        });

        Check("ResolveDevice: 部分符合有多筆時不猜（落到下一層）", () =>
        {
            // 兩台都含 "Camera"，設定 "Camera" → 部分符合不唯一；又有多台 → NotFound。
            var match = CameraCompatibility.ResolveDevice(["USB Camera", "Integrated Camera"], "Camera");
            Assert(!match.Found && match.Kind == CameraCompatibility.DeviceMatchKind.None, match.Note);
        });

        Check("ResolveDevice: 設定對不上但只列舉到一台就改用那台", () =>
        {
            var match = CameraCompatibility.ResolveDevice(["Some New Webcam 4K"], "USB Camera");
            Assert(match.Found && match.Index == 0 && match.Kind == CameraCompatibility.DeviceMatchKind.SoleDevice, match.Note);
        });

        Check("ResolveDevice: 未設定名稱 + 只有一台 → 採用該台", () =>
        {
            var match = CameraCompatibility.ResolveDevice(["Some New Webcam 4K"], "");
            Assert(match.Found && match.Index == 0 && match.Kind == CameraCompatibility.DeviceMatchKind.SoleDevice, match.Note);
        });

        Check("ResolveDevice: 完全沒有裝置 → NotFound", () =>
        {
            var match = CameraCompatibility.ResolveDevice([], "USB Camera");
            Assert(!match.Found && match.Kind == CameraCompatibility.DeviceMatchKind.None, match.Note);
        });

        Check("ResolveDevice: 多台且都對不上 → NotFound（不亂猜）", () =>
        {
            var match = CameraCompatibility.ResolveDevice(["Cam A", "Cam B"], "USB Camera");
            Assert(!match.Found && match.Kind == CameraCompatibility.DeviceMatchKind.None, match.Note);
        });

        Check("SelectFormatIndex: 有 NV12 640x480/30 就選它", () =>
        {
            var formats = new List<CameraCompatibility.FormatCandidate>
            {
                new(1920, 1080, "NV12", 30),
                new(640, 480, "MJPG", 30),
                new(640, 480, "NV12", 30),
                new(640, 480, "NV12", 15),
            };
            Assert(CameraCompatibility.SelectFormatIndex(formats) == 2, "應選 index 2");
        });

        Check("SelectFormatIndex: 沒有 NV12 時選 YUY2，不選 MJPG", () =>
        {
            var formats = new List<CameraCompatibility.FormatCandidate>
            {
                new(640, 480, "MJPG", 30),
                new(640, 480, "YUY2", 30),
            };
            Assert(CameraCompatibility.SelectFormatIndex(formats) == 1, "應選 YUY2");
        });

        Check("SelectFormatIndex: 只有 MJPG 也要選出來（不再 throw）", () =>
        {
            var formats = new List<CameraCompatibility.FormatCandidate>
            {
                new(1280, 720, "MJPG", 30),
                new(640, 480, "MJPG", 30),
            };
            Assert(CameraCompatibility.SelectFormatIndex(formats) == 1, "應選接近 VGA 的 MJPG");
        });

        Check("SelectFormatIndex: 同格式時挑最接近 640x480 的解析度", () =>
        {
            var formats = new List<CameraCompatibility.FormatCandidate>
            {
                new(160, 120, "NV12", 30),
                new(800, 600, "NV12", 30),
                new(1920, 1080, "NV12", 30),
            };
            Assert(CameraCompatibility.SelectFormatIndex(formats) == 1, "800x600 面積差最小");
        });

        Check("SelectFormatIndex: 略過寬或高為 0 的格式；全無效回傳 -1", () =>
        {
            var formats = new List<CameraCompatibility.FormatCandidate>
            {
                new(0, 0, "NV12", 30),
                new(640, 0, "NV12", 30),
            };
            Assert(CameraCompatibility.SelectFormatIndex(formats) == -1, "沒有有效格式");
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
