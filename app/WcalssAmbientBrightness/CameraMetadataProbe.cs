using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// P0 用的一次性硬體能力探測。只讀取本工具建立的 capture session 曝光與 ISO 值，不改變相機控制、取樣或亮度 Runtime。
/// </summary>
internal static class CameraMetadataProbe
{
    public static async Task<int> RunAsync(TextWriter output)
    {
        var config = AppConfig.Load();
        MediaCapture? mediaCapture = null;
        MediaFrameReader? reader = null;
        TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs>? onFrameArrived = null;
        var readerStarted = false;

        try
        {
            var source = await FindColorSourceAsync(config.DeviceName, config.ResolvedSharingMode);
            output.WriteLine($"Device: {config.DeviceName}");
            output.WriteLine($"Source: {source.Format.VideoFormat.Width}x{source.Format.VideoFormat.Height} / {source.Format.Subtype}");
            output.WriteLine($"SharingMode: {config.ResolvedSharingMode}");

            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = source.Group,
                SharingMode = config.ResolvedSharingMode,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });

            var frameSource = mediaCapture.FrameSources[source.Info.Id];
            await frameSource.SetFormatAsync(source.Format);
            // §17：與 AmbientLightSensor 一致——原生格式照協商，但一律要求 NV12 輸出。
            reader = await mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Nv12);
            var firstFrameArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            onFrameArrived = (sender, args) =>
            {
                using var frame = sender.TryAcquireLatestFrame();
                if (frame is not null)
                {
                    firstFrameArrived.TrySetResult();
                }
            };
            reader.FrameArrived += onFrameArrived;
            var startStatus = await reader.StartAsync();
            if (startStatus != MediaFrameReaderStartStatus.Success)
            {
                output.WriteLine($"Result: unavailable; MediaFrameReader.StartAsync = {startStatus}");
                return 1;
            }

            readerStarted = true;
            var frameReady = await Task.WhenAny(firstFrameArrived.Task, Task.Delay(TimeSpan.FromSeconds(1))) == firstFrameArrived.Task;
            output.WriteLine($"FrameReady: {frameReady}");
            await WriteMetadataAsync(output, mediaCapture.VideoDeviceController);
            return 0;
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message;
            output.WriteLine($"Result: probe failed; {ex.GetType().Name} (0x{ex.HResult:X8}): {detail}");
            return 1;
        }
        finally
        {
            if (reader is not null)
            {
                if (onFrameArrived is not null)
                {
                    reader.FrameArrived -= onFrameArrived;
                }

                if (readerStarted)
                {
                    await AsyncGuard.RunAsync(
                        async () => await reader.StopAsync(),
                        TimeSpan.FromSeconds(2));
                }

                reader.Dispose();
            }

            mediaCapture?.Dispose();
        }
    }

    private static async Task WriteMetadataAsync(TextWriter output, VideoDeviceController controller)
    {
        var exposure = controller.ExposureControl;
        output.WriteLine(exposure.Supported
            ? $"ExposureControl: available; Auto={exposure.Auto}; Value={exposure.Value.TotalMilliseconds:F3} ms; Range={exposure.Min.TotalMilliseconds:F3}..{exposure.Max.TotalMilliseconds:F3} ms; Step={exposure.Step.TotalMilliseconds:F3} ms"
            : "ExposureControl: unavailable (driver does not provide exposure-time metadata)");

        // 校正策略取決於能不能鎖手動曝光（issue #13 §3）：實際試一次 SetAuto(false)，再還原。
        if (exposure.Supported)
        {
            try
            {
                await exposure.SetAutoAsync(false);
                output.WriteLine($"  ExposureControl.SetAuto(false): OK; Auto now={exposure.Auto}; Value={exposure.Value.TotalMilliseconds:F3} ms");
                await exposure.SetAutoAsync(true);
            }
            catch (Exception ex)
            {
                output.WriteLine($"  ExposureControl.SetAuto(false): 失敗 {ex.GetType().Name} (0x{ex.HResult:X8})");
            }
        }

        var legacyExposure = controller.Exposure;
        var hasLegacyExposure = legacyExposure.TryGetValue(out var legacyExposureValue);
        var hasLegacyAuto = legacyExposure.TryGetAuto(out var legacyExposureAuto);
        output.WriteLine(hasLegacyExposure
            ? $"Exposure (legacy): available; Value={legacyExposureValue}; Auto={(hasLegacyAuto ? legacyExposureAuto : "unknown")}; unit is driver-defined"
            : "Exposure (legacy): unavailable or unreadable");

        if (hasLegacyExposure)
        {
            var lockedManual = legacyExposure.TrySetAuto(false);
            output.WriteLine($"  Exposure(legacy).TrySetAuto(false): {(lockedManual ? "OK" : "拒絕")}");
            legacyExposure.TrySetAuto(true);
        }

        var iso = controller.IsoSpeedControl;
        output.WriteLine(iso.Supported
            ? $"IsoSpeedControl: available; Auto={iso.Auto}; Value={iso.Value}; Range={iso.Min}..{iso.Max}; Step={iso.Step}"
            : "IsoSpeedControl: unavailable (driver does not provide ISO/gain metadata)");
    }

    private static async Task<MetadataProbeSource> FindColorSourceAsync(string deviceName, MediaCaptureSharingMode sharingMode)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        // §17：與 AmbientLightSensor 共用同一套裝置比對規則（完全同名 → 唯一部分符合 → 只有一台就採用）。
        var match = CameraCompatibility.ResolveDevice(devices.Select(d => d.Name).ToList(), deviceName);
        if (!match.Found)
        {
            throw new InvalidOperationException($"找不到視訊裝置：{deviceName}。{match.Note}");
        }

        var device = devices[match.Index];

        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var source = groups
            .SelectMany(group => group.SourceInfos.Select(info => new { Group = group, Info = info }))
            .Where(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color)
            .Where(candidate => string.Equals(candidate.Info.DeviceInformation?.Id, device.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Info.MediaStreamType == MediaStreamType.VideoPreview)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("MediaFrameSourceGroup 找不到目標裝置的 color MediaFrameSource。");

        using var mediaCapture = new MediaCapture();
        await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = source.Group,
            SharingMode = sharingMode,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            StreamingCaptureMode = StreamingCaptureMode.Video,
        });

        var frameSource = mediaCapture.FrameSources[source.Info.Id];
        // §17：與 AmbientLightSensor 共用同一套原生格式挑選（不再限定 NV12）。
        var format = AmbientLightSensor.SelectFormat(frameSource);

        return new MetadataProbeSource(source.Group, source.Info, format);
    }
}

internal sealed record MetadataProbeSource(MediaFrameSourceGroup Group, MediaFrameSourceInfo Info, MediaFrameFormat Format);
