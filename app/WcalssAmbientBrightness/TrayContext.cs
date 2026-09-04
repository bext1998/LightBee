using System.Drawing;
using System.Windows.Forms;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 背景常駐主體：System Tray 圖示 + 週期性取樣迴圈。
/// 對應 docs/spike-report.md 的定位：Gate A（能穩定開關裝置）、Gate B（三段亮度分級）、
/// Gate C（SharedReadOnly + 週期性 Lazy 取樣，不長時間佔用相機）在這裡合成一個實際會跑的行為。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // EMA 平滑改成自適應版（SampleSmoother）：原本固定 α=0.5（13.2 節校準過，兩三次取樣追上持續性變化），
    // 但關燈這種大幅轉換仍要靠多次取樣的指數逼近收尾，在 5 秒取樣間隔下拖慢判定。
    // 現在小幅擾動仍用 0.5 防抖，大幅變化（|raw−ema| ≥ 0.1）改用 0.9 在 1-2 次取樣內追上；
    // 往上的分級切換仍受「連續兩次確認」與遲滯把關，防單次突波與螢幕回饋的語意不變。
    private readonly SampleSmoother smoother = new();
    private const int RampTickIntervalMs = 200;

    // 實測發現的新問題：SharedReadOnly 長時間運作（本機約 7-8 分鐘、70 輪取樣後）偶爾會安靜卡住，
    // 拿不到任何 frame，但相機本身、驅動都沒壞——用獨立探測工具馬上重開一個新的 MediaCapture 就正常。
    // 這代表卡住的是這個 App 自己那個 MediaFrameReader 工作階段，不是裝置或系統層級的問題，
    // 所以自動恢復的做法是重新建立 AmbientLightSensor（等於重開一個乾淨的相機工作階段），不用重啟整個 App。
    private const int ReconnectAfterConsecutiveFailures = 3;

    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem autoAdjustMenuItem;
    private readonly ToolStripMenuItem statusMenuItem;
    private readonly System.Windows.Forms.Timer sampleTimer;
    private readonly System.Windows.Forms.Timer rampTimer;
    private readonly AppConfig config;
    private readonly DisplayBrightnessController brightnessController;
    private BrightnessMapper mapper;  // 由 CreateMapper() 建立，設定變更時重建
    private SamplePacing pacing = null!;
    private readonly ValidationLog validationLog;
    private readonly CameraDiagnosticsLog cameraDiagnostics = new();
    private readonly BrightnessRamp ramp = new();
    private AmbientLightSensor? sensor;
    private SettingsForm? settingsForm;
    private bool sensorReady;
    private bool sampling;
    private double? emaLuminance;
    private bool rampInProgress;
    private bool rampFailureNotified;
    private int consecutiveSampleFailures;

    public TrayContext()
    {
        config = AppConfig.Load();
        brightnessController = new DisplayBrightnessController();
        brightnessController.Probe();
        validationLog = new ValidationLog();
        mapper = CreateMapper();
        pacing = CreatePacing();

        statusMenuItem = new ToolStripMenuItem("狀態：初始化中…") { Enabled = false };
        autoAdjustMenuItem = new ToolStripMenuItem("啟用自動調整", null, OnToggleAutoAdjust) { Checked = config.AutoAdjustEnabled, CheckOnClick = true };

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autoAdjustMenuItem);
        menu.Items.Add("立即取樣", null, async (_, _) => await SampleOnceAsync(manualTrigger: true));
        menu.Items.Add("開啟設定…", null, OnOpenSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, OnExit);

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WCALSS 環境光自動亮度",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += OnOpenSettings;

        sampleTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1000, config.SampleIntervalMs) };
        sampleTimer.Tick += async (_, _) => await SampleOnceAsync(manualTrigger: false);

        // rampTimer 跟 sampleTimer 分開跑：sampleTimer 負責「多久重新判定一次分級」（沿用 Test 08 驗證過的
        // Lazy 取樣頻率），rampTimer 負責「往目標亮度前進一小步」，用高得多的頻率（200ms）才能做出漸進感，
        // 這是使用者跟 codex 討論後採用的短期方案第二層：目標值跟實際套用值分開、用速率限制器慢慢逼近。
        rampTimer = new System.Windows.Forms.Timer { Interval = RampTickIntervalMs };
        rampTimer.Tick += OnRampTick;
        rampTimer.Start();

        _ = InitializeAsync();
    }

    /// <summary>建構 mapper 與 SamplePacing；兩者都依賴分級設定，設定變更時一起重建。</summary>
    private BrightnessMapper CreateMapper() => new(config.ToBands(), config.HysteresisMargin);

    private SamplePacing CreatePacing() => new(
        config.ToBands().Where(b => b.UpperBound != double.MaxValue).Select(b => b.UpperBound),
        slowIntervalMs: Math.Max(1000, config.SampleIntervalMs),
        fastIntervalMs: Math.Max(200, config.AdaptiveFastIntervalMs),
        deltaThreshold: config.AdaptiveDeltaThreshold,
        boundaryMargin: config.AdaptiveBoundaryMargin,
        maxFastCycles: Math.Max(1, config.AdaptiveMaxFastCycles));

    private async Task InitializeAsync()
    {
        try
        {
            sensor = new AmbientLightSensor(config.DeviceName, config.ResolvedSharingMode);
            await sensor.PrepareAsync();
            sensorReady = true;
            statusMenuItem.Text = $"狀態：就緒（{sensor.ResolvedFormatDescription}）";
            cameraDiagnostics.Append("initialize", true, sensor.ResolvedFormatDescription, prepare: sensor.LastPrepareDiagnostics);

            // 用螢幕實際回報的目前亮度當漸進控制器的起點，避免第一次判定分級時從一個猜測值開始漸進，
            // 導致跟螢幕實際狀態對不上。讀不到目前亮度（例如兩種控制方式都不可用）就不校正，讓 ramp 保持未知狀態，
            // 之後第一次 SetTarget 會直接採用目標值，不做漸進（沒有基準可以漸進）。
            var currentBrightness = brightnessController.CurrentBrightnessPercent;
            if (currentBrightness is not null)
            {
                ramp.SyncCurrent(currentBrightness.Value);
            }

            sampleTimer.Start();
            await SampleOnceAsync(manualTrigger: false);
        }
        catch (Exception ex)
        {
            sensorReady = false;
            statusMenuItem.Text = "狀態：相機初始化失敗";
            cameraDiagnostics.Append("initialize", false, $"{ex.GetType().Name} (0x{ex.HResult:X8})", prepare: sensor?.LastPrepareDiagnostics);
            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = false,
                ValidatedBy = "Gate A / Test 01-03",
                Note = $"初始化失敗（對應 Test 10：Camera Sharing 系統設定關閉時可能安靜失敗，或裝置被其他 App 以 ExclusiveControl 佔用）：{ex.GetType().Name} (0x{ex.HResult:X8}): {(string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message)}"
            });
            trayIcon.ShowBalloonTip(5000, "WCALSS 環境光自動亮度", $"相機初始化失敗：{ex.Message}", ToolTipIcon.Error);
        }
    }

    public async Task SampleOnceAsync(bool manualTrigger)
    {
        if (sampling || sensor is null || !sensorReady)
        {
            return;
        }

        sampling = true;
        try
        {
            // §16.6：已在連續失敗狀態時，先確認裝置還在列舉中；不在就別再對 Media Foundation 硬送
            // InitializeAsync（便宜相機會整個從 USB bus 掉，§16.5 實測；§13.4 警告過高頻 MF thrash 的風險）。
            // 健康路徑（首次失敗前）不跑這道檢查，零額外開銷。
            if (consecutiveSampleFailures > 0)
            {
                var presence = await sensor.CheckTargetDevicePresenceAsync();
                if (!presence.TargetDeviceFound)
                {
                    consecutiveSampleFailures++;
                    cameraDiagnostics.Append("device-check", false, "目標裝置不在列舉清單中，略過本輪取樣、未觸碰相機 API", prepare: presence);
                    statusMenuItem.Text = $"狀態：相機已離線，等待重新連接（連續 {consecutiveSampleFailures} 輪）";
                    validationLog.Append(new ValidationLogEntry
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        SampleSucceeded = false,
                        ValidatedBy = "Gate A / Test 01-03",
                        Note = $"相機不在裝置列舉中（連續 {consecutiveSampleFailures} 輪），略過本輪取樣、未觸碰相機 API。目前列舉到：{string.Join(", ", presence.EnumeratedDevices)}"
                    });
                    settingsForm?.OnLogUpdated();

                    if (consecutiveSampleFailures >= ReconnectAfterConsecutiveFailures)
                    {
                        consecutiveSampleFailures = 0;
                        await TryReconnectSensorAsync();
                    }

                    pacing.OnSample(success: false, raw: 0, smoothed: 0);
                    sampleTimer.Interval = pacing.NextIntervalMs();
                    return;
                }
            }

            var result = await sensor.SampleOnceAsync();
            cameraDiagnostics.Append("sample", result.Success, result.Success ? null : result.Error, sample: result.Diagnostics);
            if (!result.Success)
            {
                consecutiveSampleFailures++;
                statusMenuItem.Text = $"狀態：本次取樣失敗（連續 {consecutiveSampleFailures} 輪）";
                validationLog.Append(new ValidationLogEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    SampleSucceeded = false,
                    ValidatedBy = "Gate C / Test 10",
                    Note = result.Error
                });
                settingsForm?.OnLogUpdated();

                if (consecutiveSampleFailures >= ReconnectAfterConsecutiveFailures)
                {
                    consecutiveSampleFailures = 0;
                    await TryReconnectSensorAsync();
                }

                // 取樣失敗一律退回慢間隔：不對疑似故障的相機連續高頻開關（13.4 節教訓）。
                pacing.OnSample(success: false, raw: 0, smoothed: 0);
                sampleTimer.Interval = pacing.NextIntervalMs();
                return;
            }

            consecutiveSampleFailures = 0;

            // EMA 平滑：小幅擾動用 α=0.5，大幅變化用 α=0.9 快速追上（見 SampleSmoother）；
            // CSV 的 mean_luminance 欄位仍記錄原始讀值，跟 Test 06 的量測方式保持一致，方便回溯比對。
            var smoothed = smoother.Smooth(emaLuminance, result.MeanLuminance, out var alphaUsed);
            emaLuminance = smoothed;
            var effectiveLuminance = smoothed!.Value;

            var rawBand = mapper.GetBand(effectiveLuminance);

            var switchedBand = config.AutoAdjustEnabled ? mapper.Evaluate(effectiveLuminance) : null;

            if (switchedBand is not null)
            {
                // 不在這裡直接套用亮度，改成設定 ramp 的目標值，實際寫入由 rampTimer 每 200ms 逐步逼近，
                // 才會有「慢慢變暗」的漸進感，而不是每次判定就直接跳到目標百分比。
                ramp.SetTarget(switchedBand.TargetBrightnessPercent);
            }

            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = true,
                MeanLuminance = result.MeanLuminance,
                BandLabel = rawBand.Label,
                AppliedBrightnessPercent = switchedBand?.TargetBrightnessPercent,
                BrightnessApplySucceeded = false,
                ValidatedBy = rawBand.ValidatedBy,
                Note = switchedBand is null
                    ? (manualTrigger ? "手動觸發；維持原分級（遲滯區間內）" : "維持原分級（遲滯區間內）")
                    : $"{(manualTrigger ? "手動觸發；" : string.Empty)}分級切換為「{switchedBand.Label}」，開始漸進調整至 {switchedBand.TargetBrightnessPercent}%（平滑值 {effectiveLuminance:F4}；變亮 15%/秒、變暗 8%/秒，非立即套用，完成時會另有一筆紀錄）"
            });

            settingsForm?.OnLogUpdated();

            // 自適應取樣節奏：每次取樣結果處理完後重新決定下一輪的間隔。
            // 讀值正在變化或逼近分級邊界時用快間隔（預設 500ms），穩定時退回慢間隔（預設 5 秒），
            // 讓「以取樣次數計」的防抖機制在時間軸上自動縮短，而長期平均的相機佔用不變。
            pacing.OnSample(result.Success, result.MeanLuminance, effectiveLuminance);
            sampleTimer.Interval = pacing.NextIntervalMs();
            statusMenuItem.Text = $"狀態：{rawBand.Label}（讀值 {result.MeanLuminance:F4}，平滑 {effectiveLuminance:F4}，α{alphaUsed:F2}，{pacing.NextIntervalMs()}ms）";
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message;
            statusMenuItem.Text = "狀態：取樣發生未預期錯誤";
            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = false,
                ValidatedBy = "未預期例外",
                Note = $"{ex.GetType().Name} (0x{ex.HResult:X8}): {detail}"
            });
            settingsForm?.OnLogUpdated();
        }
        finally
        {
            sampling = false;
        }
    }

    /// <summary>
    /// 連續 <see cref="ReconnectAfterConsecutiveFailures"/> 輪取樣都拿不到 frame 時呼叫：
    /// 重新建立一個乾淨的 AmbientLightSensor（等於重新協商裝置與格式、開一個新的相機工作階段），
    /// 而不是整個 App 重啟。先 Dispose 舊 sensor 再換上新的；閒置的 AmbientLightSensor 不持有跨次
    /// 取樣的原生資源（每次 SampleOnceAsync 自己開關 MediaCapture），而取樣進行中的 MediaCapture 由
    /// §16 加固過的 finally 保證會釋放到，process 不會卡著相機控制代碼。
    /// 重連失敗也不會讓 App 卡死：只是記一筆失敗紀錄，等下一次再連續失敗 3 輪就會再試一次。
    /// </summary>
    private async Task TryReconnectSensorAsync()
    {
        statusMenuItem.Text = "狀態：連續取樣失敗，正在自動重建相機工作階段…";
        AmbientLightSensor? newSensor = null;
        try
        {
            newSensor = new AmbientLightSensor(config.DeviceName, config.ResolvedSharingMode);
            await newSensor.PrepareAsync();
            sensor?.Dispose();
            sensor = newSensor;
            statusMenuItem.Text = $"狀態：已自動重連相機（{newSensor.ResolvedFormatDescription}）";
            trayIcon.ShowBalloonTip(4000, "WCALSS 環境光自動亮度", "偵測到相機連續取樣失敗，已自動重建工作階段。", ToolTipIcon.Info);
            cameraDiagnostics.Append("reconnect", true, newSensor.ResolvedFormatDescription, prepare: newSensor.LastPrepareDiagnostics);
            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = true,
                ValidatedBy = "自動重連機制（回應實測發現：SharedReadOnly 長時間運作偶發卡住）",
                Note = $"連續 {ReconnectAfterConsecutiveFailures} 輪取樣失敗，已自動重建相機工作階段並恢復：{newSensor.ResolvedFormatDescription}"
            });
        }
        catch (Exception ex)
        {
            statusMenuItem.Text = "狀態：自動重連失敗，將於下次連續取樣失敗後再試";
            cameraDiagnostics.Append("reconnect", false, $"{ex.GetType().Name} (0x{ex.HResult:X8})", prepare: newSensor?.LastPrepareDiagnostics);
            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = false,
                ValidatedBy = "自動重連機制（回應實測發現：SharedReadOnly 長時間運作偶發卡住）",
                Note = $"連續 {ReconnectAfterConsecutiveFailures} 輪取樣失敗後嘗試自動重連，但重連本身也失敗，將於下次連續失敗後再試：{ex.GetType().Name} (0x{ex.HResult:X8}): {(string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message)}"
            });
        }
        finally
        {
            settingsForm?.OnLogUpdated();
        }
    }

    /// <summary>
    /// 每 200ms 觸發一次：往目前的漸進目標前進一小步。只在真的有寫入動作或一段漸進調整剛完成時
    /// 才寫驗證紀錄，不會每 200ms 就洗一筆——驗證紀錄要保留「一次有意義的判斷／結果」，不是動畫影格記錄。
    /// </summary>
    private void OnRampTick(object? sender, EventArgs e)
    {
        if (!config.AutoAdjustEnabled || !sensorReady)
        {
            return;
        }

        var next = ramp.Step(RampTickIntervalMs);
        if (next is null)
        {
            if (rampInProgress)
            {
                rampInProgress = false;
                rampFailureNotified = false;
                validationLog.Append(new ValidationLogEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    SampleSucceeded = true,
                    AppliedBrightnessPercent = (int)Math.Round(ramp.TargetPercent),
                    BrightnessApplySucceeded = true,
                    ValidatedBy = "漸進亮度控制（EMA 平滑 + 速率限制，短期方案）",
                    Note = $"漸進調整完成，已透過 {brightnessController.ControlMethodDescription} 達成 {ramp.TargetPercent:F0}%。"
                });
                settingsForm?.OnLogUpdated();
            }

            return;
        }

        rampInProgress = true;
        var applied = brightnessController.TrySetBrightness((int)Math.Round(next.Value), out var error);
        if (!applied && !rampFailureNotified)
        {
            rampFailureNotified = true;
            trayIcon.ShowBalloonTip(4000, "WCALSS 環境光自動亮度", $"漸進調整寫入失敗：{error}", ToolTipIcon.Warning);
            validationLog.Append(new ValidationLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                SampleSucceeded = true,
                AppliedBrightnessPercent = (int)Math.Round(next.Value),
                BrightnessApplySucceeded = false,
                ValidatedBy = "漸進亮度控制（EMA 平滑 + 速率限制，短期方案）",
                Note = $"漸進調整寫入失敗：{error}"
            });
            settingsForm?.OnLogUpdated();
        }
    }

    private void OnToggleAutoAdjust(object? sender, EventArgs e)
    {
        config.AutoAdjustEnabled = autoAdjustMenuItem.Checked;
        config.Save();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (settingsForm is null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(config, brightnessController, validationLog, mapper, ApplyRuntimeConfigChanges);
        }

        settingsForm.Show();
        settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    /// <summary>設定視窗按下「儲存」後呼叫：重建 mapper（分級/遲滯可能變了），並視需要重啟取樣計時器與感測器。</summary>
    public void ApplyRuntimeConfigChanges(bool sensorSettingsChanged)
    {
        mapper = CreateMapper();
        pacing = CreatePacing();
        autoAdjustMenuItem.Checked = config.AutoAdjustEnabled;
        sampleTimer.Interval = Math.Max(1000, config.SampleIntervalMs);

        if (sensorSettingsChanged)
        {
            sampleTimer.Stop();
            sensorReady = false;
            sensor?.Dispose();
            statusMenuItem.Text = "狀態：重新初始化中…";
            _ = InitializeAsync();
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        sampleTimer.Stop();
        rampTimer.Stop();
        trayIcon.Visible = false;
        sensor?.Dispose();
        brightnessController.Dispose();
        Application.Exit();
    }
}
