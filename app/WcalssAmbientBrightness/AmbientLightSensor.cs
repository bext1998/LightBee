using System.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 週期性 Open → Sample → Release 取樣環境亮度。取樣節奏、格式挑選與釋放路徑的
/// 設計理由與實測數據見 docs/spike-report.md。
/// </summary>
internal sealed class AmbientLightSensor : IDisposable
{
    // 略過每次取樣視窗開頭的曝光收斂期（實測約 0.44–0.48 秒，見 spike-report §4）。
    private const int ConvergenceSkipMs = 550;
    private const int SampleWindowMs = 1200;

    // 取樣窗提前結束：窗內每 100ms 檢查一次收斂期後的讀值，最近 6 個 frame（30 FPS 下約 200ms）
    // 的 mean luminance max−min ≤ 0.01 就提前結束，不必等滿 SampleWindowMs。
    private const int EarlyCheckIntervalMs = 100;
    private const int MinConvergedFramesForEarlyExit = 6;
    private const double StabilityTolerance = 0.01;

    private readonly string deviceName;
    private readonly MediaCaptureSharingMode sharingMode;
    private DeviceInformation? device;
    private SourceSelection? sourceSelection;

    public AmbientLightSensor(string deviceName, MediaCaptureSharingMode sharingMode)
    {
        this.deviceName = deviceName;
        this.sharingMode = sharingMode;
    }

    /// <summary>裝置與格式只在啟動／設定變更時重新協商一次，對應原本 coldstart 「選一次、重複用」的模式。</summary>
    public async Task PrepareAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var enumerated = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        var enumerationMs = stopwatch.ElapsedMilliseconds;
        var names = enumerated.Select(d => d.Name).ToList();
        // §17：不再只認寫死的裝置名稱——完全同名找不到時，退而用唯一部分符合或「只有一台就採用」，
        // 讓換相機不必先改設定就能跑起來。
        var match = CameraCompatibility.ResolveDevice(names, deviceName);

        stopwatch.Restart();
        try
        {
            if (!match.Found)
            {
                throw new InvalidOperationException(
                    $"找不到視訊裝置：{deviceName}。{match.Note}");
            }

            device = enumerated[match.Index];
            sourceSelection = await FindColorSourceAsync(device.Id, sharingMode);
        }
        finally
        {
            // 即使找不到裝置（開機後相機還沒被列舉）也要留下這筆：EnumeratedDevices 就是
            // 「相機到底有沒有出現」的直接證據，對應待正式化的 Test 13。
            LastPrepareDiagnostics = new PrepareDiagnostics
            {
                DeviceEnumerationMs = enumerationMs,
                EnumeratedDevices = names,
                TargetDeviceFound = match.Found,
                DeviceMatchNote = match.Note,
                SourceNegotiationMs = stopwatch.ElapsedMilliseconds,
                ResolvedFormat = sourceSelection is null ? "" : ResolvedFormatDescription,
            };
        }
    }

    /// <summary>最近一次 <see cref="PrepareAsync"/> 的裝置列舉／格式協商診斷（成功或失敗都會留下）。</summary>
    public PrepareDiagnostics? LastPrepareDiagnostics { get; private set; }

    /// <summary>
    /// 目標裝置的 <c>VID_xxxx&amp;PID_xxxx</c>（從 WinRT 裝置 Id 抽出；抽不到回傳完整 Id，都沒有回空字串）。
    /// 用於硬體相容性排查——把「哪顆晶片」記進 log，別只有「雜牌相機」。PrepareAsync 成功後才有值。
    /// </summary>
    public string ResolvedDeviceHardwareId
    {
        get
        {
            var id = device?.Id;
            if (string.IsNullOrEmpty(id))
            {
                return "";
            }

            var match = System.Text.RegularExpressions.Regex.Match(id, "VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}");
            return match.Success ? match.Value.ToUpperInvariant() : id;
        }
    }

    /// <summary>
    /// 只做裝置列舉、不碰 <see cref="MediaCapture"/>，回報目標裝置是否還在（<c>TargetDeviceFound</c>）。
    /// 用於 §16.6：便宜相機可能整個從 USB bus 掉，此時不該再對 Media Foundation 硬送
    /// <c>InitializeAsync</c>（每 5 秒秒炸 + 對 MF 高頻 thrash，§13.4 警告過的風險）。
    /// </summary>
    public async Task<PrepareDiagnostics> CheckTargetDevicePresenceAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var enumerated = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        var names = enumerated.Select(d => d.Name).ToList();
        // 與 PrepareAsync 用同一套比對規則，兩邊對「裝置還在不在」的判斷才會一致。
        var match = CameraCompatibility.ResolveDevice(names, deviceName);
        return new PrepareDiagnostics
        {
            DeviceEnumerationMs = stopwatch.ElapsedMilliseconds,
            EnumeratedDevices = names,
            TargetDeviceFound = match.Found,
            DeviceMatchNote = match.Note,
            SourceNegotiationMs = 0,
            ResolvedFormat = "",
        };
    }

    public string ResolvedFormatDescription =>
        sourceSelection is null
            ? "尚未初始化"
            : $"{sourceSelection.Format.VideoFormat.Width}x{sourceSelection.Format.VideoFormat.Height} @ {sourceSelection.Format.FrameRate.Numerator}/{sourceSelection.Format.FrameRate.Denominator} FPS / {sourceSelection.Format.Subtype}";

    /// <summary>執行一次 Open → Sample(略過收斂期) → Release，回傳這次取樣的平均亮度。失敗回傳 null 並附上原因。</summary>
    public async Task<SampleResult> SampleOnceAsync()
    {
        if (device is null || sourceSelection is null)
        {
            return SampleResult.Failed("尚未呼叫 PrepareAsync()。");
        }

        MediaCapture? mediaCapture = null;
        MediaFrameReader? reader = null;
        var readerStarted = false;
        var openedAtUtc = DateTimeOffset.UtcNow;
        var samples = new List<(DateTimeOffset At, double Mean)>();
        var gate = new object();

        // 診斷欄位：每一步的耗時與結果都記下來，寫進 camera-diagnostics.csv，
        // 用來佐證 spike-report §13.3／§13.4 的相機工作階段卡死（症狀：一直取樣失敗、關掉 App 才恢復）。
        long initializeMs = 0, stopAsyncMs = 0, readerDisposeMs = 0, mediaCaptureDisposeMs = 0, sampleWindowMs = 0;
        var initialized = false;
        var startStatus = "not-reached";
        var stopAsyncTimedOut = false;
        var failedStep = "none";
        string? failureDetail = null;
        string? frameError = null;

        void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap is null)
                {
                    return;
                }

                var mean = ReadNv12MeanLuminance(bitmap);
                lock (gate)
                {
                    samples.Add((DateTimeOffset.UtcNow, mean));
                }
            }
            catch (Exception ex)
            {
                // 單一 frame 讀取失敗不影響整體取樣（與原 FrameCollector 行為一致），
                // 但要留下第一筆錯誤：§17 之後若某相機的原生格式無法轉成 NV12，
                // 症狀會是「一個 frame 都用不了」，沒有這個就只會看到誤導性的「沒有 frame 抵達」。
                lock (gate)
                {
                    frameError ??= $"frame 解析失敗：{ex.GetType().Name} (0x{ex.HResult:X8}): {(string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message)}";
                }
            }
        }

        var stepWatch = Stopwatch.StartNew();
        try
        {
            mediaCapture = new MediaCapture();
            stepWatch.Restart();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = sourceSelection.Group,
                SharingMode = sharingMode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });
            initializeMs = stepWatch.ElapsedMilliseconds;
            initialized = true;

            var source = mediaCapture.FrameSources[sourceSelection.Info.Id];
            await source.SetFormatAsync(sourceSelection.Format);
            // §17：原生格式照 sourceSelection.Format 協商，但一律要求 MediaFrameReader 以 NV12 輸出，
            // 下游 ReadNv12MeanLuminance 的 Y 平面解析維持不變（NV12 是最普遍支援的轉換目標）。
            reader = await mediaCapture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12);
            reader.FrameArrived += OnFrameArrived;

            var status = await reader.StartAsync();
            startStatus = status.ToString();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                failedStep = "start";
                failureDetail = $"MediaFrameReader.StartAsync 狀態：{status}";
            }
            else
            {
                readerStarted = true;
                while (true)
                {
                    var elapsedMs = (int)(DateTimeOffset.UtcNow - openedAtUtc).TotalMilliseconds;
                    var waitMs = Math.Min(EarlyCheckIntervalMs, Math.Max(0, SampleWindowMs - elapsedMs));
                    if (waitMs <= 0)
                    {
                        break;
                    }

                    await Task.Delay(waitMs);
                    List<(DateTimeOffset At, double Mean)> snapshot;
                    lock (gate)
                    {
                        snapshot = samples.ToList();
                    }

                    var window = snapshot
                        .Where(s => (s.At - openedAtUtc).TotalMilliseconds >= ConvergenceSkipMs)
                        .Select(s => s.Mean)
                        .ToArray();
                    if (LuminanceStability.IsStable(window, MinConvergedFramesForEarlyExit, StabilityTolerance))
                    {
                        break;
                    }
                }

                sampleWindowMs = (long)(DateTimeOffset.UtcNow - openedAtUtc).TotalMilliseconds;
            }
        }
        catch (Exception ex)
        {
            if (!initialized)
            {
                initializeMs = stepWatch.ElapsedMilliseconds;
                failedStep = "initialize";
            }
            else if (failedStep == "none")
            {
                failedStep = "post-init";
            }

            // ex.Message 在部分 WinRT HRESULT（例如 0x80070020 sharing violation）projection 成
            // managed 例外時可能是空字串，所以一定要帶上 HResult，不然診斷紀錄裡的錯誤訊息會是空的、沒有診斷價值。
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message;
            failureDetail = $"{ex.GetType().Name} (0x{ex.HResult:X8}): {detail}";
        }
        finally
        {
            // 釋放路徑加固（回應 §13.3／§16 讀碼發現）：原本 finally 直接 await reader.StopAsync()，
            // 一旦它卡住不返回，後面的 mediaCapture.Dispose() 就永遠跑不到，相機控制代碼會被 App
            // 持有到 process 結束。現在：事件一定先解除；StopAsync 包 2 秒 timeout，卡住就不再等它；
            // reader 與 mediaCapture 各自獨立 Dispose，彼此不受影響、也不受 StopAsync 影響。
            if (reader is { } activeReader)
            {
                activeReader.FrameArrived -= OnFrameArrived;

                if (readerStarted)
                {
                    var stop = await AsyncGuard.RunAsync(
                        async () => await activeReader.StopAsync(),
                        TimeSpan.FromSeconds(2));
                    stopAsyncMs = stop.ElapsedMs;
                    stopAsyncTimedOut = !stop.Completed;
                }

                var readerDisposeWatch = Stopwatch.StartNew();
                try { activeReader.Dispose(); } catch { /* Dispose 失敗不影響後續釋放 */ }
                readerDisposeMs = readerDisposeWatch.ElapsedMilliseconds;
            }

            var mediaCaptureDisposeWatch = Stopwatch.StartNew();
            try { mediaCapture?.Dispose(); } catch { /* 同上，確保一定被呼叫到 */ }
            mediaCaptureDisposeMs = mediaCaptureDisposeWatch.ElapsedMilliseconds;
        }

        List<(DateTimeOffset At, double Mean)> finalSnapshot;
        lock (gate)
        {
            finalSnapshot = samples.ToList();
        }

        double meanLuminance = 0;
        var usableFrames = 0;
        if (failureDetail is null)
        {
            // 只取收斂期之後的 frame 計算平均值，對應 Test 04/05 的實測收斂時間。
            var converged = finalSnapshot.Where(s => (s.At - openedAtUtc).TotalMilliseconds >= ConvergenceSkipMs).ToList();
            var usable = converged.Count > 0 ? converged : finalSnapshot;
            if (usable.Count == 0)
            {
                failedStep = "no-frames";
                failureDetail = frameError
                    ?? "取樣期間沒有任何 frame 抵達（安靜失敗，對應 Test 10 觀察到的情況：Camera Sharing 關閉時 FrameArrived 不會觸發）。";
            }
            else
            {
                meanLuminance = usable.Average(s => s.Mean);
                usableFrames = usable.Count;
            }
        }

        var diagnostics = new SampleDiagnostics
        {
            InitializeMs = initializeMs,
            StartStatus = startStatus,
            FramesArrived = finalSnapshot.Count,
            SampleWindowMs = sampleWindowMs,
            StopAsyncMs = stopAsyncMs,
            StopAsyncTimedOut = stopAsyncTimedOut,
            ReaderDisposeMs = readerDisposeMs,
            MediaCaptureDisposeMs = mediaCaptureDisposeMs,
            FailedStep = failedStep,
        };

        return failureDetail is null
            ? SampleResult.Ok(meanLuminance, finalSnapshot.Count, usableFrames) with { Diagnostics = diagnostics }
            : SampleResult.Failed(failureDetail) with { Diagnostics = diagnostics };
    }

    private static async Task<SourceSelection> FindColorSourceAsync(string deviceId, MediaCaptureSharingMode sharingMode)
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var candidates = groups
            .SelectMany(group => group.SourceInfos.Select(info => new { Group = group, Info = info }))
            .Where(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color)
            .Where(candidate => string.Equals(candidate.Info.DeviceInformation?.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Info.MediaStreamType == MediaStreamType.VideoPreview)
            .ToList();

        var selected = candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("MediaFrameSourceGroup 找不到目標裝置的 color MediaFrameSource。");

        var mediaCapture = new MediaCapture();
        try
        {
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = selected.Group,
                SharingMode = sharingMode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            var source = mediaCapture.FrameSources[selected.Info.Id];
            var format = SelectFormat(source);
            return new SourceSelection(selected.Group, selected.Info, format);
        }
        finally
        {
            mediaCapture.Dispose();
        }
    }

    /// <summary>
    /// §17：不再限定 NV12。挑「原生擷取格式」交給 <see cref="CameraCompatibility.SelectFormatIndex"/>
    /// （NV12 &gt; YUY2 &gt; 其他未壓縮 &gt; MJPG，再優先接近 640x480/30fps），
    /// 影像在 <see cref="MediaFrameReader"/> 輸出端統一轉成 NV12。
    /// </summary>
    internal static MediaFrameFormat SelectFormat(MediaFrameSource source)
    {
        var formats = source.SupportedFormats
            .Where(candidate => candidate.VideoFormat is not null)
            .ToList();

        var candidates = formats
            .Select(candidate => new CameraCompatibility.FormatCandidate(
                (int)candidate.VideoFormat.Width,
                (int)candidate.VideoFormat.Height,
                candidate.Subtype,
                candidate.FrameRate.Denominator != 0
                    ? (double)candidate.FrameRate.Numerator / candidate.FrameRate.Denominator
                    : 0))
            .ToList();

        var index = CameraCompatibility.SelectFormatIndex(candidates);
        return index >= 0
            ? formats[index]
            : throw new InvalidOperationException("目標 color MediaFrameSource 沒有任何可用的視訊格式。");
    }

    private static double ReadNv12MeanLuminance(SoftwareBitmap bitmap)
    {
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Nv12)
        {
            throw new InvalidOperationException($"Frame SoftwareBitmap 格式不是 NV12，而是 {bitmap.BitmapPixelFormat}。");
        }

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixelCount = checked(width * height);
        var nv12BufferSize = checked(pixelCount + pixelCount / 2);
        var buffer = new Windows.Storage.Streams.Buffer((uint)nv12BufferSize);
        bitmap.CopyToBuffer(buffer);
        if (buffer.Length < pixelCount)
        {
            throw new InvalidOperationException($"NV12 CopyToBuffer 資料不足：需要至少 {pixelCount} bytes，實際 {buffer.Length} bytes。");
        }

        var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(bytes);
        }

        long sum = 0;
        for (var index = 0; index < pixelCount; index++)
        {
            sum += bytes[index];
        }

        return sum / (double)pixelCount / 255.0;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

internal sealed record SourceSelection(MediaFrameSourceGroup Group, MediaFrameSourceInfo Info, MediaFrameFormat Format);

internal sealed record SampleResult(bool Success, double MeanLuminance, int TotalFrames, int UsableFrames, string? Error)
{
    /// <summary>相機工作階段的低階時序診斷（每次取樣都會附上，供 camera-diagnostics.csv 記錄）。</summary>
    public SampleDiagnostics? Diagnostics { get; init; }

    public static SampleResult Ok(double meanLuminance, int totalFrames, int usableFrames) =>
        new(true, meanLuminance, totalFrames, usableFrames, null);

    public static SampleResult Failed(string error) =>
        new(false, 0, 0, 0, error);
}
