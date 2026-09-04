using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Wcalss.CameraProbe;

internal static class ColdStartCommand
{
    private const int DefaultDurationMs = 3000;
    private const int DefaultRepeats = 5;
    private const int DefaultPauseMs = 1000;
    private const int DefaultStabilityWindow = 15;
    private const double DefaultStabilityThreshold = 0.01;
    private const double BlackFrameThreshold = 0.01;
    private const string DefaultDeviceName = "USB Camera";
    private const string CsvHeader = "timestamp,session_id,environment,roi,mean_luminance,median_luminance,frame_index";
    private const string LazyCycleCsvHeader = "timestamp,session_id,repeat,open_latency_ms,frame_count,capture_success,release_success,error";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ColdStartOptions.Parse(args);
            var projectDirectory = FindProjectDirectory();
            var csvPath = Path.Combine(projectDirectory, "raw-data", "luminance-samples.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

            var device = await FindDeviceAsync(options.DeviceName);
            var sourceSelection = await FindColorSourceAsync(device.Id, options.SharingMode);
            Console.WriteLine("WCALSS Cold Start / Exposure Convergence / Environment Separation");
            Console.WriteLine($"Camera: {device.Name}");
            Console.WriteLine($"Capture source group: {sourceSelection.Group.DisplayName}");
            Console.WriteLine($"Capture mode: {sourceSelection.Format.VideoFormat.Width}x{sourceSelection.Format.VideoFormat.Height} @ {sourceSelection.Format.FrameRate.Numerator}/{sourceSelection.Format.FrameRate.Denominator} FPS / {sourceSelection.Format.Subtype}");
            Console.WriteLine($"Sharing mode: {options.SharingMode}");
            Console.WriteLine($"Environment: {options.Environment}");
            Console.WriteLine($"Repeats: {options.Repeats}, duration: {options.DurationMs} ms, pause: {options.PauseMs} ms");
            Console.WriteLine($"CSV: {csvPath}");

            var sessions = new List<ColdStartSession>();
            var lazyCyclesPath = Path.Combine(projectDirectory, "raw-data", "lazy-cycles.csv");
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var wallClock = Stopwatch.StartNew();
            for (var repeat = 1; repeat <= options.Repeats; repeat++)
            {
                var session = await CaptureSessionAsync(device, sourceSelection, options, repeat);
                AppendCsv(csvPath, session);
                AppendLazyCycleCsv(lazyCyclesPath, session);
                sessions.Add(session);
                PrintSessionResult(session, DefaultStabilityWindow, DefaultStabilityThreshold);

                if (repeat < options.Repeats && options.PauseMs > 0)
                {
                    await Task.Delay(options.PauseMs);
                }
            }

            wallClock.Stop();
            var execution = new ExecutionMetrics
            {
                Mode = "lazy",
                TotalCpuTimeMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds,
                TotalWallTimeMs = wallClock.Elapsed.TotalMilliseconds
            };
            var jsonPath = WriteMetadata(projectDirectory, device, sourceSelection.Format, options, sessions, csvPath, execution);
            Console.WriteLine();
            Console.WriteLine($"Metadata JSON: {jsonPath}");
            Console.WriteLine($"已追加 {sessions.Sum(s => s.Samples.Count)} 筆 frame samples 到 CSV。");
            PrintLazySummary(sessions, execution);

            return sessions.Any(s => s.Samples.Count > 0) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"coldstart 失敗：{FormatException(ex)}");
            return 1;
        }
    }

    public static async Task<int> PersistentAsync(string[] args)
    {
        try
        {
            var options = PersistentOptions.Parse(args);
            var projectDirectory = FindProjectDirectory();
            var csvPath = Path.Combine(projectDirectory, "raw-data", "luminance-samples.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

            var device = await FindDeviceAsync(options.DeviceName);
            var sourceSelection = await FindColorSourceAsync(device.Id, options.SharingMode);
            Console.WriteLine("WCALSS Persistent Acquisition / Test 09");
            Console.WriteLine($"Camera: {device.Name}");
            Console.WriteLine($"Capture source group: {sourceSelection.Group.DisplayName}");
            Console.WriteLine($"Capture mode: {sourceSelection.Format.VideoFormat.Width}x{sourceSelection.Format.VideoFormat.Height} @ {sourceSelection.Format.FrameRate.Numerator}/{sourceSelection.Format.FrameRate.Denominator} FPS / {sourceSelection.Format.Subtype}");
            Console.WriteLine($"Sharing mode: {options.SharingMode}");
            Console.WriteLine($"Environment: {options.Environment}");
            Console.WriteLine($"Total duration: {options.TotalDurationMs} ms");
            Console.WriteLine($"CSV: {csvPath}");

            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var wallClock = Stopwatch.StartNew();
            var session = await CapturePersistentSessionAsync(device, sourceSelection, options);
            AppendCsv(csvPath, session);
            wallClock.Stop();

            var execution = new ExecutionMetrics
            {
                Mode = "persistent",
                TotalCpuTimeMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds,
                TotalWallTimeMs = wallClock.Elapsed.TotalMilliseconds,
                OpenLatencyMs = session.OpenLatencyMs
            };
            var jsonPath = WritePersistentMetadata(projectDirectory, device, sourceSelection.Format, options, session, csvPath, execution);
            PrintPersistentSummary(session, execution);
            Console.WriteLine();
            Console.WriteLine($"Metadata JSON: {jsonPath}");
            Console.WriteLine($"已追加 {session.Samples.Count} 筆 frame samples 到 CSV。");

            return session.Samples.Count > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"persistent 失敗：{FormatException(ex)}");
            return 1;
        }
    }

    public static Task<int> AnalyzeAsync(string[] args)
    {
        try
        {
            var options = AnalyzeOptions.Parse(args);
            var projectDirectory = FindProjectDirectory();
            var csvPath = options.CsvPath is null
                ? Path.Combine(projectDirectory, "raw-data", "luminance-samples.csv")
                : Path.GetFullPath(options.CsvPath, Directory.GetCurrentDirectory());

            var samples = ReadCsv(csvPath);
            if (samples.Count == 0)
            {
                Console.WriteLine($"CSV 沒有 samples：{csvPath}");
                return Task.FromResult(1);
            }

            Console.WriteLine("WCALSS Exposure Convergence / Environment Separation Analysis");
            Console.WriteLine($"CSV: {csvPath}");
            Console.WriteLine($"Stability rule: recent {options.StabilityWindow} frames 的 Mean Luminance max-min <= {options.StabilityThreshold.ToString("0.######", CultureInfo.InvariantCulture)}");
            Console.WriteLine();
            Console.WriteLine("=== Per-session Time-to-Stable-Luminance ===");

            var grouped = samples
                .GroupBy(s => s.SessionId)
                .Select(group => AnalyzeSession(group.Key, group.OrderBy(s => s.Timestamp).ToList(), options.StabilityWindow, options.StabilityThreshold))
                .ToList();

            foreach (var result in grouped)
            {
                var stable = result.TimeToStableMs is null
                    ? "未收斂"
                    : $"{result.TimeToStableMs.Value.ToString("0.###", CultureInfo.InvariantCulture)} ms";
                Console.WriteLine($"- environment={result.Environment}, session={result.SessionId}, frames={result.FrameCount}, Time-to-Stable-Luminance={stable}");
            }

            Console.WriteLine();
            Console.WriteLine("=== Environment Mean Luminance Range ===");
            foreach (var group in samples.GroupBy(s => s.Environment).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var values = group.Select(s => s.MeanLuminance).ToList();
                Console.WriteLine($"- environment={group.Key}, samples={values.Count}, min={values.Min().ToString("0.######", CultureInfo.InvariantCulture)}, max={values.Max().ToString("0.######", CultureInfo.InvariantCulture)}, average={values.Average().ToString("0.######", CultureInfo.InvariantCulture)}");
            }

            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"analyze 失敗：{FormatException(ex)}");
            return Task.FromResult(1);
        }
    }

    private static async Task<DeviceInformation> FindDeviceAsync(string deviceName)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        var device = devices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        return device ?? throw new InvalidOperationException($"找不到視訊裝置：{deviceName}。目前列舉到：{string.Join(", ", devices.Select(d => d.Name))}");
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

    private static MediaFrameFormat SelectFormat(MediaFrameSource source)
    {
        var preferred = source.SupportedFormats
            .Where(candidate => candidate.VideoFormat is not null)
            .Where(candidate => candidate.VideoFormat.Width == 640 && candidate.VideoFormat.Height == 480)
            .Where(candidate => string.Equals(candidate.Subtype, "NV12", StringComparison.OrdinalIgnoreCase))
            .Where(candidate => IsThirtyFps(candidate.FrameRate))
            .FirstOrDefault();

        if (preferred is not null)
        {
            return preferred;
        }

        // 640x480/NV12/30fps 不一定總是可取得——例如 Camera Sharing 開啟、已有其他 App 先協商走某個格式時，
        // 目前這個 MediaFrameSource 可能只公布對方已經在用的單一格式（Test 10 Camera Coexistence 觀察到的真實情況）。
        // 這種情況下退而求其次，選可用清單中解析度最高的 NV12 格式，而不是直接失敗。
        var fallback = source.SupportedFormats
            .Where(candidate => candidate.VideoFormat is not null)
            .Where(candidate => string.Equals(candidate.Subtype, "NV12", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.VideoFormat.Width * candidate.VideoFormat.Height)
            .FirstOrDefault();

        if (fallback is not null)
        {
            Console.WriteLine($"[警告] 找不到 640x480/NV12/30fps，改用目前可用格式：{fallback.VideoFormat.Width}x{fallback.VideoFormat.Height} @ {fallback.FrameRate.Numerator}/{fallback.FrameRate.Denominator} FPS / {fallback.Subtype}");
            return fallback;
        }

        throw new InvalidOperationException("目標 color MediaFrameSource 沒有任何 NV12 格式可用。");
    }

    private static async Task<ColdStartSession> CaptureSessionAsync(
        DeviceInformation device,
        SourceSelection selection,
        ColdStartOptions options,
        int repeat)
    {
        var session = new ColdStartSession
        {
            SessionId = Guid.NewGuid().ToString("D"),
            Repeat = repeat,
            Environment = options.Environment,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        MediaCapture? mediaCapture = null;
        MediaFrameReader? reader = null;
        FrameCollector? collector = null;
        var readerStarted = false;
        var openClock = new Stopwatch();
        try
        {
            mediaCapture = new MediaCapture();
            openClock.Start();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = selection.Group,
                SharingMode = options.SharingMode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            var source = mediaCapture.FrameSources[selection.Info.Id];
            await source.SetFormatAsync(selection.Format);
            reader = await mediaCapture.CreateFrameReaderAsync(source, selection.Format.Subtype);
            collector = new FrameCollector(session);
            reader.FrameArrived += collector.OnFrameArrived;

            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new InvalidOperationException($"MediaFrameReader.StartAsync 狀態：{status}");
            }

            session.OpenLatencyMs = openClock.Elapsed.TotalMilliseconds;
            readerStarted = true;
            await Task.Delay(options.DurationMs);
        }
        catch (Exception ex)
        {
            session.Error = FormatException(ex);
        }
        finally
        {
            session.ReleaseSuccess = true;
            if (reader is not null)
            {
                if (readerStarted)
                {
                    try
                    {
                        await reader.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        session.ReleaseSuccess = false;
                        session.Error ??= $"StopAsync: {FormatException(ex)}";
                    }
                }

                try
                {
                    if (collector is not null)
                    {
                        reader.FrameArrived -= collector.OnFrameArrived;
                    }
                }
                catch (Exception ex)
                {
                    session.ReleaseSuccess = false;
                    session.Error ??= $"FrameArrived unsubscribe: {FormatException(ex)}";
                }

                try
                {
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    session.ReleaseSuccess = false;
                    session.Error ??= $"Reader Dispose: {FormatException(ex)}";
                }
            }

            try
            {
                mediaCapture?.Dispose();
            }
            catch (Exception ex)
            {
                session.ReleaseSuccess = false;
                session.Error ??= $"MediaCapture Dispose: {FormatException(ex)}";
            }
        }

        session.EndedAtUtc = DateTimeOffset.UtcNow;
        if (collector?.Error is not null)
        {
            session.Error ??= $"Frame processing: {collector.Error}";
        }

        return session;
    }

    private static async Task<ColdStartSession> CapturePersistentSessionAsync(
        DeviceInformation device,
        SourceSelection selection,
        PersistentOptions options)
    {
        var session = new ColdStartSession
        {
            SessionId = Guid.NewGuid().ToString("D"),
            Repeat = 1,
            Environment = options.Environment,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        MediaCapture? mediaCapture = null;
        MediaFrameReader? reader = null;
        FrameCollector? collector = null;
        var readerStarted = false;
        var openClock = new Stopwatch();
        try
        {
            mediaCapture = new MediaCapture();
            openClock.Start();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = selection.Group,
                SharingMode = options.SharingMode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            var source = mediaCapture.FrameSources[selection.Info.Id];
            await source.SetFormatAsync(selection.Format);
            reader = await mediaCapture.CreateFrameReaderAsync(source, selection.Format.Subtype);
            collector = new FrameCollector(session);
            reader.FrameArrived += collector.OnFrameArrived;

            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new InvalidOperationException($"MediaFrameReader.StartAsync 狀態：{status}");
            }

            session.OpenLatencyMs = openClock.Elapsed.TotalMilliseconds;
            readerStarted = true;
            await Task.Delay(options.TotalDurationMs);
        }
        catch (Exception ex)
        {
            session.Error = FormatException(ex);
        }
        finally
        {
            session.ReleaseSuccess = true;
            if (reader is not null)
            {
                if (readerStarted)
                {
                    try
                    {
                        await reader.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        session.ReleaseSuccess = false;
                        session.Error ??= $"StopAsync: {FormatException(ex)}";
                    }
                }

                try
                {
                    if (collector is not null)
                    {
                        reader.FrameArrived -= collector.OnFrameArrived;
                    }
                }
                catch (Exception ex)
                {
                    session.ReleaseSuccess = false;
                    session.Error ??= $"FrameArrived unsubscribe: {FormatException(ex)}";
                }

                try
                {
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    session.ReleaseSuccess = false;
                    session.Error ??= $"Reader Dispose: {FormatException(ex)}";
                }
            }

            try
            {
                mediaCapture?.Dispose();
            }
            catch (Exception ex)
            {
                session.ReleaseSuccess = false;
                session.Error ??= $"MediaCapture Dispose: {FormatException(ex)}";
            }
        }

        session.EndedAtUtc = DateTimeOffset.UtcNow;
        if (collector?.Error is not null)
        {
            session.Error ??= $"Frame processing: {collector.Error}";
        }

        return session;
    }

    private static void AppendCsv(string csvPath, ColdStartSession session)
    {
        var writeHeader = !File.Exists(csvPath) || new FileInfo(csvPath).Length == 0;
        using var writer = new StreamWriter(csvPath, append: true, new UTF8Encoding(false));
        if (writeHeader)
        {
            writer.WriteLine(CsvHeader);
        }

        foreach (var sample in session.Samples)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                EscapeCsv(session.SessionId),
                EscapeCsv(session.Environment),
                "full",
                sample.MeanLuminance.ToString("R", CultureInfo.InvariantCulture),
                sample.MedianLuminance.ToString("R", CultureInfo.InvariantCulture),
                sample.FrameIndex.ToString(CultureInfo.InvariantCulture)
            }));
        }
    }

    private static void AppendLazyCycleCsv(string csvPath, ColdStartSession session)
    {
        var writeHeader = !File.Exists(csvPath) || new FileInfo(csvPath).Length == 0;
        using var writer = new StreamWriter(csvPath, append: true, new UTF8Encoding(false));
        if (writeHeader)
        {
            writer.WriteLine(LazyCycleCsvHeader);
        }

        writer.WriteLine(string.Join(",", new[]
        {
            session.EndedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            EscapeCsv(session.SessionId),
            session.Repeat.ToString(CultureInfo.InvariantCulture),
            session.OpenLatencyMs?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            session.Samples.Count.ToString(CultureInfo.InvariantCulture),
            session.Samples.Count > 0 ? "true" : "false",
            session.ReleaseSuccess ? "true" : "false",
            EscapeCsv(session.Error ?? string.Empty)
        }));
    }

    private static string WriteMetadata(
        string projectDirectory,
        DeviceInformation device,
        MediaFrameFormat format,
        ColdStartOptions options,
        IReadOnlyList<ColdStartSession> sessions,
        string csvPath,
        ExecutionMetrics execution)
    {
        var rawDataDirectory = Path.Combine(projectDirectory, "raw-data");
        var path = Path.Combine(rawDataDirectory, $"coldstart-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss'Z'}.json");
        var videoFormat = format.VideoFormat!;
        var report = new ColdStartReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Test = "Test 04 Cold Start / Test 05 Exposure Convergence / Test 06 Environment Separation",
            ExecutionMode = execution.Mode,
            Camera = new CameraMetadata { Name = device.Name, DeviceId = device.Id },
            CaptureMode = new CaptureModeMetadata
            {
                Width = videoFormat.Width,
                Height = videoFormat.Height,
                Fps = ToFps(format.FrameRate),
                PixelFormat = format.Subtype
            },
            Environment = options.Environment,
            CameraSharing = false,
            DurationMs = options.DurationMs,
            Repeats = options.Repeats,
            PauseMs = options.PauseMs,
            TotalCpuTimeMs = execution.TotalCpuTimeMs,
            TotalWallTimeMs = execution.TotalWallTimeMs,
            CsvPath = Path.GetRelativePath(projectDirectory, csvPath),
            Sessions = sessions.Select(session => new SessionMetadata
            {
                SessionId = session.SessionId,
                Repeat = session.Repeat,
                StartedAtUtc = session.StartedAtUtc,
                EndedAtUtc = session.EndedAtUtc,
                FrameCount = session.Samples.Count,
                OpenLatencyMs = session.OpenLatencyMs,
                CaptureSuccess = session.Samples.Count > 0,
                ReleaseSuccess = session.ReleaseSuccess,
                Error = session.Error
            }).ToList()
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static string WritePersistentMetadata(
        string projectDirectory,
        DeviceInformation device,
        MediaFrameFormat format,
        PersistentOptions options,
        ColdStartSession session,
        string csvPath,
        ExecutionMetrics execution)
    {
        var rawDataDirectory = Path.Combine(projectDirectory, "raw-data");
        var path = Path.Combine(rawDataDirectory, $"persistent-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss'Z'}.json");
        var videoFormat = format.VideoFormat!;
        var report = new PersistentReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Test = "Test 09 Persistent vs Lazy",
            ExecutionMode = execution.Mode,
            Camera = new CameraMetadata { Name = device.Name, DeviceId = device.Id },
            CaptureMode = new CaptureModeMetadata
            {
                Width = videoFormat.Width,
                Height = videoFormat.Height,
                Fps = ToFps(format.FrameRate),
                PixelFormat = format.Subtype
            },
            Environment = options.Environment,
            CameraSharing = false,
            TotalDurationMs = options.TotalDurationMs,
            TotalCpuTimeMs = execution.TotalCpuTimeMs,
            TotalWallTimeMs = execution.TotalWallTimeMs,
            OpenLatencyMs = session.OpenLatencyMs,
            CsvPath = Path.GetRelativePath(projectDirectory, csvPath),
            Session = new SessionMetadata
            {
                SessionId = session.SessionId,
                Repeat = session.Repeat,
                StartedAtUtc = session.StartedAtUtc,
                EndedAtUtc = session.EndedAtUtc,
                FrameCount = session.Samples.Count,
                OpenLatencyMs = session.OpenLatencyMs,
                CaptureSuccess = session.Samples.Count > 0,
                ReleaseSuccess = session.ReleaseSuccess,
                Error = session.Error
            }
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static void PrintLazySummary(IReadOnlyList<ColdStartSession> sessions, ExecutionMetrics execution)
    {
        var latencyValues = sessions
            .Where(session => session.OpenLatencyMs is not null)
            .Select(session => session.OpenLatencyMs!.Value)
            .ToList();
        var latencyText = latencyValues.Count == 0
            ? "無成功 open latency"
            : string.Join(", ", sessions.Select(session => session.OpenLatencyMs is null
                ? $"{session.Repeat}:null"
                : $"{session.Repeat}:{session.OpenLatencyMs.Value.ToString("0.###", CultureInfo.InvariantCulture)} ms"));
        var firstFive = latencyValues.Take(5).ToList();
        var lastFive = latencyValues.TakeLast(5).ToList();
        var failures = sessions.Where(session => session.Samples.Count == 0 || session.Error is not null).ToList();
        var blackFrames = sessions.Where(session => session.Samples.Count > 0 && session.Samples.All(sample => sample.MeanLuminance <= BlackFrameThreshold)).ToList();

        Console.WriteLine();
        Console.WriteLine("=== Test 08 Lazy Acquisition Summary ===");
        Console.WriteLine($"Open latency per cycle (repeat:ms): {latencyText}");
        if (firstFive.Count > 0 && lastFive.Count > 0)
        {
            Console.WriteLine($"First 5 successful open average: {firstFive.Average().ToString("0.###", CultureInfo.InvariantCulture)} ms; last 5 successful open average: {lastFive.Average().ToString("0.###", CultureInfo.InvariantCulture)} ms");
        }

        Console.WriteLine($"Capture success=false cycles: {sessions.Count(session => session.Samples.Count == 0)}");
        Console.WriteLine($"Cycles with error: {sessions.Count(session => session.Error is not null)}");
        Console.WriteLine($"Cycles with all Mean Luminance <= {BlackFrameThreshold.ToString("0.##", CultureInfo.InvariantCulture)}: {blackFrames.Count}");
        Console.WriteLine($"CPU time: {execution.TotalCpuTimeMs.ToString("0.###", CultureInfo.InvariantCulture)} ms; Wall time: {execution.TotalWallTimeMs.ToString("0.###", CultureInfo.InvariantCulture)} ms");
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.WriteLine($"  repeat {failure.Repeat}: error={failure.Error ?? "none"}, frames={failure.Samples.Count}");
            }
        }
    }

    private static void PrintPersistentSummary(ColdStartSession session, ExecutionMetrics execution)
    {
        var analysis = AnalyzeSession(session.SessionId, session.Samples, DefaultStabilityWindow, DefaultStabilityThreshold);
        var stable = analysis.TimeToStableMs is null
            ? "未收斂"
            : $"{analysis.TimeToStableMs.Value.ToString("0.###", CultureInfo.InvariantCulture)} ms";
        var values = session.Samples.Select(sample => sample.MeanLuminance).ToList();
        Console.WriteLine();
        Console.WriteLine("=== Test 09 Persistent Summary ===");
        Console.WriteLine($"Session: {session.SessionId}");
        Console.WriteLine($"Open latency: {(session.OpenLatencyMs?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null")} ms");
        Console.WriteLine($"Frames: {session.Samples.Count}; capture_success={session.Samples.Count > 0}; release_success={session.ReleaseSuccess}; error={session.Error ?? "null"}");
        Console.WriteLine($"Time-to-Stable-Luminance: {stable}");
        if (values.Count > 0)
        {
            Console.WriteLine($"Mean Luminance: min={values.Min().ToString("0.######", CultureInfo.InvariantCulture)}, max={values.Max().ToString("0.######", CultureInfo.InvariantCulture)}, average={values.Average().ToString("0.######", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"Median Luminance: min={session.Samples.Min(sample => sample.MedianLuminance).ToString("0.######", CultureInfo.InvariantCulture)}, max={session.Samples.Max(sample => sample.MedianLuminance).ToString("0.######", CultureInfo.InvariantCulture)}, average={session.Samples.Average(sample => sample.MedianLuminance).ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        Console.WriteLine($"CPU time: {execution.TotalCpuTimeMs.ToString("0.###", CultureInfo.InvariantCulture)} ms; Wall time: {execution.TotalWallTimeMs.ToString("0.###", CultureInfo.InvariantCulture)} ms");
    }

    private static void PrintSessionResult(ColdStartSession session, int stabilityWindow, double stabilityThreshold)
    {
        var analysis = AnalyzeSession(session.SessionId, session.Samples, stabilityWindow, stabilityThreshold);
        var stable = analysis.TimeToStableMs is null
            ? "未收斂"
            : $"{analysis.TimeToStableMs.Value.ToString("0.###", CultureInfo.InvariantCulture)} ms";
        var first = session.Samples.FirstOrDefault();
        var last = session.Samples.LastOrDefault();
        var curve = first is null || last is null
            ? "無 frame"
            : $"Mean {first.MeanLuminance:0.####} → {last.MeanLuminance:0.####}, Median {first.MedianLuminance:0.####} → {last.MedianLuminance:0.####}";
        Console.WriteLine($"Repeat {session.Repeat}: session={session.SessionId}, frames={session.Samples.Count}, Time-to-Stable-Luminance={stable}, {curve}");
        if (session.Error is not null)
        {
            Console.WriteLine($"  error: {session.Error}");
        }
    }

    private static SessionAnalysis AnalyzeSession(string sessionId, IReadOnlyList<LuminanceSample> samples, int stabilityWindow, double stabilityThreshold)
    {
        if (samples.Count == 0)
        {
            return new SessionAnalysis(sessionId, "unknown", 0, null);
        }

        var environment = samples[0].Environment;
        for (var index = stabilityWindow - 1; index < samples.Count; index++)
        {
            var window = samples.Skip(index - stabilityWindow + 1).Take(stabilityWindow).Select(s => s.MeanLuminance).ToList();
            if (window.Max() - window.Min() <= stabilityThreshold)
            {
                var elapsedMs = (samples[index].Timestamp - samples[0].Timestamp).TotalMilliseconds;
                return new SessionAnalysis(sessionId, environment, samples.Count, elapsedMs);
            }
        }

        return new SessionAnalysis(sessionId, environment, samples.Count, null);
    }

    private static List<LuminanceSample> ReadCsv(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到 luminance CSV", path);
        }

        var samples = new List<LuminanceSample>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine();
        if (header != CsvHeader)
        {
            throw new InvalidDataException($"CSV header 不符合預期：{header}");
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count != 7)
            {
                throw new InvalidDataException($"CSV 欄位數錯誤：{line}");
            }

            samples.Add(new LuminanceSample
            {
                Timestamp = DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SessionId = fields[1],
                Environment = fields[2],
                MeanLuminance = double.Parse(fields[4], CultureInfo.InvariantCulture),
                MedianLuminance = double.Parse(fields[5], CultureInfo.InvariantCulture),
                FrameIndex = int.Parse(fields[6], CultureInfo.InvariantCulture)
            });
        }

        return samples;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static bool IsThirtyFps(MediaRatio ratio) =>
        ratio.Denominator != 0 && Math.Abs((double)ratio.Numerator / ratio.Denominator - 30.0) < 0.01;

    private static double ToFps(MediaRatio ratio) =>
        ratio.Denominator == 0 ? 0 : (double)ratio.Numerator / ratio.Denominator;

    private static string FindProjectDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CameraProbe.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string FormatException(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    // Test 10 Camera Coexistence：--sharing-mode exclusive（預設，維持既有行為）或 shared，
    // 對應 MediaCaptureSharingMode.SharedReadOnly，用來測試在其他 App 已佔用相機時，
    // 以「非獨占」方式請求是否能改善共存表現。
    internal static MediaCaptureSharingMode ParseSharingMode(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "exclusive" => MediaCaptureSharingMode.ExclusiveControl,
        "shared" => MediaCaptureSharingMode.SharedReadOnly,
        _ => throw new ArgumentException($"--sharing-mode 必須是 exclusive 或 shared：{raw}")
    };

    private sealed class FrameCollector
    {
        private readonly ColdStartSession session;
        private readonly object gate = new();

        public FrameCollector(ColdStartSession session) => this.session = session;

        public string? Error { get; private set; }

        public void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (bitmap is null)
                {
                    return;
                }

                var luminance = ReadNv12Luminance(bitmap);
                lock (gate)
                {
                    session.Samples.Add(new LuminanceSample
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        SessionId = session.SessionId,
                        Environment = session.Environment,
                        MeanLuminance = luminance.Mean,
                        MedianLuminance = luminance.Median,
                        FrameIndex = session.Samples.Count
                    });
                }
            }
            catch (Exception ex)
            {
                Error ??= FormatException(ex);
            }
        }

        private static LuminancePair ReadNv12Luminance(SoftwareBitmap bitmap)
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
                throw new InvalidOperationException($"NV12 SoftwareBitmap CopyToBuffer 資料不足：需要至少 {pixelCount} bytes，實際 {buffer.Length} bytes。");
            }

            var bytes = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(bytes);
            }

            var histogram = new int[256];
            long sum = 0;

            for (var index = 0; index < pixelCount; index++)
            {
                var value = bytes[index];
                sum += value;
                histogram[value]++;
            }

            var lowRank = (pixelCount - 1) / 2;
            var highRank = pixelCount / 2;
            var lowValue = FindHistogramRank(histogram, lowRank);
            var highValue = FindHistogramRank(histogram, highRank);
            return new LuminancePair(sum / (double)pixelCount / 255.0, (lowValue + highValue) / 2.0 / 255.0);
        }

        private static int FindHistogramRank(int[] histogram, int rank)
        {
            var seen = 0;
            for (var value = 0; value < histogram.Length; value++)
            {
                seen += histogram[value];
                if (seen > rank)
                {
                    return value;
                }
            }

            return 255;
        }
    }
}

internal sealed record SourceSelection(MediaFrameSourceGroup Group, MediaFrameSourceInfo Info, MediaFrameFormat Format);

internal sealed class ColdStartSession
{
    public string SessionId { get; init; } = string.Empty;
    public int Repeat { get; init; }
    public string Environment { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public double? OpenLatencyMs { get; set; }
    public bool ReleaseSuccess { get; set; }
    public List<LuminanceSample> Samples { get; } = [];
    public string? Error { get; set; }
}

internal sealed class LuminanceSample
{
    public DateTimeOffset Timestamp { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public double MeanLuminance { get; init; }
    public double MedianLuminance { get; init; }
    public int FrameIndex { get; init; }
}

internal sealed record LuminancePair(double Mean, double Median);

internal sealed record SessionAnalysis(string SessionId, string Environment, int FrameCount, double? TimeToStableMs);

internal sealed class ExecutionMetrics
{
    public string Mode { get; init; } = string.Empty;
    public double TotalCpuTimeMs { get; init; }
    public double TotalWallTimeMs { get; init; }
    public double? OpenLatencyMs { get; init; }
}

internal sealed class ColdStartReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("test")]
    public string Test { get; init; } = string.Empty;

    [JsonPropertyName("execution_mode")]
    public string ExecutionMode { get; init; } = string.Empty;

    [JsonPropertyName("camera")]
    public CameraMetadata Camera { get; init; } = new();

    [JsonPropertyName("capture_mode")]
    public CaptureModeMetadata CaptureMode { get; init; } = new();

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("camera_sharing")]
    public bool CameraSharing { get; init; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; init; }

    [JsonPropertyName("repeats")]
    public int Repeats { get; init; }

    [JsonPropertyName("pause_ms")]
    public int PauseMs { get; init; }

    [JsonPropertyName("total_cpu_time_ms")]
    public double TotalCpuTimeMs { get; init; }

    [JsonPropertyName("total_wall_time_ms")]
    public double TotalWallTimeMs { get; init; }

    [JsonPropertyName("csv_path")]
    public string CsvPath { get; init; } = string.Empty;

    [JsonPropertyName("sessions")]
    public IReadOnlyList<SessionMetadata> Sessions { get; init; } = [];
}

internal sealed class PersistentReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("test")]
    public string Test { get; init; } = string.Empty;

    [JsonPropertyName("execution_mode")]
    public string ExecutionMode { get; init; } = string.Empty;

    [JsonPropertyName("camera")]
    public CameraMetadata Camera { get; init; } = new();

    [JsonPropertyName("capture_mode")]
    public CaptureModeMetadata CaptureMode { get; init; } = new();

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("camera_sharing")]
    public bool CameraSharing { get; init; }

    [JsonPropertyName("total_duration_ms")]
    public int TotalDurationMs { get; init; }

    [JsonPropertyName("total_cpu_time_ms")]
    public double TotalCpuTimeMs { get; init; }

    [JsonPropertyName("total_wall_time_ms")]
    public double TotalWallTimeMs { get; init; }

    [JsonPropertyName("open_latency_ms")]
    public double? OpenLatencyMs { get; init; }

    [JsonPropertyName("csv_path")]
    public string CsvPath { get; init; } = string.Empty;

    [JsonPropertyName("session")]
    public SessionMetadata Session { get; init; } = new();
}

internal sealed class CameraMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;
}

internal sealed class CaptureModeMetadata
{
    [JsonPropertyName("width")]
    public uint Width { get; init; }

    [JsonPropertyName("height")]
    public uint Height { get; init; }

    [JsonPropertyName("fps")]
    public double Fps { get; init; }

    [JsonPropertyName("pixel_format")]
    public string PixelFormat { get; init; } = string.Empty;
}

internal sealed class SessionMetadata
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("repeat")]
    public int Repeat { get; init; }

    [JsonPropertyName("started_at_utc")]
    public DateTimeOffset StartedAtUtc { get; init; }

    [JsonPropertyName("ended_at_utc")]
    public DateTimeOffset EndedAtUtc { get; init; }

    [JsonPropertyName("frame_count")]
    public int FrameCount { get; init; }

    [JsonPropertyName("open_latency_ms")]
    public double? OpenLatencyMs { get; init; }

    [JsonPropertyName("capture_success")]
    public bool CaptureSuccess { get; init; }

    [JsonPropertyName("release_success")]
    public bool ReleaseSuccess { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class ColdStartOptions
{
    public string Environment { get; private init; } = string.Empty;
    public int DurationMs { get; private init; } = 3000;
    public int Repeats { get; private init; } = 5;
    public int PauseMs { get; private init; } = 1000;
    public string DeviceName { get; private init; } = "USB Camera";
    public MediaCaptureSharingMode SharingMode { get; private init; } = MediaCaptureSharingMode.ExclusiveControl;

    public static ColdStartOptions Parse(string[] args)
    {
        var values = ParseValues(args);
        var environment = Required(values, "environment");
        return new ColdStartOptions
        {
            Environment = environment,
            DurationMs = PositiveInt(values, "duration-ms", 3000),
            Repeats = PositiveInt(values, "repeats", 5),
            PauseMs = NonNegativeInt(values, "pause-ms", 1000),
            DeviceName = values.GetValueOrDefault("device-name", "USB Camera"),
            SharingMode = ColdStartCommand.ParseSharingMode(values.GetValueOrDefault("sharing-mode", "exclusive"))
        };
    }

    private static Dictionary<string, string> ParseValues(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"參數格式錯誤：{args[index]}");
            }

            values[args[index][2..]] = args[++index];
        }

        return values;
    }

    private static string Required(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"缺少 --{name}");

    private static int PositiveInt(Dictionary<string, string> values, string name, int fallback) =>
        ParseInt(values, name, fallback, value => value > 0);

    private static int NonNegativeInt(Dictionary<string, string> values, string name, int fallback) =>
        ParseInt(values, name, fallback, value => value >= 0);

    private static int ParseInt(Dictionary<string, string> values, string name, int fallback, Func<int, bool> valid)
    {
        if (!values.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || !valid(value))
        {
            throw new ArgumentException($"--{name} 必須是有效整數：{raw}");
        }

        return value;
    }
}

internal sealed class PersistentOptions
{
    public string Environment { get; private init; } = string.Empty;
    public int TotalDurationMs { get; private init; } = 120000;
    public string DeviceName { get; private init; } = "USB Camera";
    public MediaCaptureSharingMode SharingMode { get; private init; } = MediaCaptureSharingMode.ExclusiveControl;

    public static PersistentOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"參數格式錯誤：{args[index]}");
            }

            values[args[index][2..]] = args[++index];
        }

        if (!values.TryGetValue("environment", out var environment) || string.IsNullOrWhiteSpace(environment))
        {
            throw new ArgumentException("缺少 --environment");
        }

        var totalDurationMs = 120000;
        if (values.TryGetValue("total-duration-ms", out var rawDuration)
            && (!int.TryParse(rawDuration, NumberStyles.Integer, CultureInfo.InvariantCulture, out totalDurationMs) || totalDurationMs <= 0))
        {
            throw new ArgumentException($"--total-duration-ms 必須是正整數：{rawDuration}");
        }

        return new PersistentOptions
        {
            Environment = environment,
            TotalDurationMs = totalDurationMs,
            DeviceName = values.GetValueOrDefault("device-name", "USB Camera"),
            SharingMode = ColdStartCommand.ParseSharingMode(values.GetValueOrDefault("sharing-mode", "exclusive"))
        };
    }
}

internal sealed class AnalyzeOptions
{
    public string? CsvPath { get; private init; }
    public int StabilityWindow { get; private init; } = 15;
    public double StabilityThreshold { get; private init; } = 0.01;

    public static AnalyzeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"參數格式錯誤：{args[index]}");
            }

            values[args[index][2..]] = args[++index];
        }

        var window = ParseInt(values, "stability-window", 15, value => value > 0);
        var threshold = ParseDouble(values, "stability-threshold", 0.01, value => value >= 0);
        return new AnalyzeOptions
        {
            CsvPath = values.GetValueOrDefault("csv"),
            StabilityWindow = window,
            StabilityThreshold = threshold
        };
    }

    private static int ParseInt(Dictionary<string, string> values, string name, int fallback, Func<int, bool> valid)
    {
        if (!values.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || !valid(value))
        {
            throw new ArgumentException($"--{name} 必須是有效整數：{raw}");
        }

        return value;
    }

    private static double ParseDouble(Dictionary<string, string> values, string name, double fallback, Func<double, bool> valid)
    {
        if (!values.TryGetValue(name, out var raw))
        {
            return fallback;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !valid(value))
        {
            throw new ArgumentException($"--{name} 必須是有效數值：{raw}");
        }

        return value;
    }
}
