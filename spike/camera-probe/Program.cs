using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace Wcalss.CameraProbe;

internal static class Program
{
    private const string CameraApi = "Windows.Media.Capture (VideoDeviceController)";
    private static readonly Regex VendorProductPattern = new(
        "VID_(?<vid>[0-9A-F]{4}).*PID_(?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0].Equals("coldstart", StringComparison.OrdinalIgnoreCase))
        {
            return await ColdStartCommand.RunAsync(args[1..]);
        }

        if (args.Length > 0 && args[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
        {
            return await ColdStartCommand.AnalyzeAsync(args[1..]);
        }

        if (args.Length > 0 && args[0].Equals("persistent", StringComparison.OrdinalIgnoreCase))
        {
            return await ColdStartCommand.PersistentAsync(args[1..]);
        }

        Console.WriteLine("WCALSS Camera Probe — Test 01 / Test 02 / Test 03");
        Console.WriteLine($"開始時間（UTC）：{DateTimeOffset.UtcNow:O}");

        var report = await ProbeAsync();
        PrintReport(report);

        var outputPath = WriteReport(report);
        Console.WriteLine();
        Console.WriteLine($"JSON 已寫入：{outputPath}");
        return 0;
    }

    private static async Task<ProbeReport> ProbeAsync()
    {
        IReadOnlyList<DeviceInformation> devices;
        try
        {
            devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        }
        catch (Exception ex)
        {
            return new ProbeReport
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                CameraApi = CameraApi,
                EnumerationError = FormatException(ex),
                Cameras = []
            };
        }

        var cameras = new List<CameraProbeResult>(devices.Count);
        foreach (var device in devices)
        {
            cameras.Add(await ProbeDeviceAsync(device));
        }

        return new ProbeReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CameraApi = CameraApi,
            Cameras = cameras
        };
    }

    private static async Task<CameraProbeResult> ProbeDeviceAsync(DeviceInformation device)
    {
        var (vendorId, productId) = ParseVendorProduct(device.Id);
        var result = new CameraProbeResult
        {
            DeviceName = device.Name,
            DeviceId = device.Id,
            VendorId = vendorId,
            ProductId = productId,
            Interface = ParseInterface(device.Id),
            CameraApi = CameraApi,
            CaptureModes = [],
            Controls = CreateUnknownControls()
        };

        MediaCapture? mediaCapture = null;
        try
        {
            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            var controller = mediaCapture.VideoDeviceController;
            result.CaptureModes = ReadCaptureModes(controller);
            result.Controls = ReadControls(controller);
        }
        catch (Exception ex)
        {
            result.ProbeError = FormatException(ex);
        }
        finally
        {
            mediaCapture?.Dispose();
        }

        return result;
    }

    private static List<CaptureMode> ReadCaptureModes(VideoDeviceController controller)
    {
        var modes = new List<CaptureMode>();
        var properties = controller.GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord);

        foreach (var property in properties)
        {
            if (property is not VideoEncodingProperties video)
            {
                continue;
            }

            var numerator = video.FrameRate.Numerator;
            var denominator = video.FrameRate.Denominator;
            double? fps = denominator == 0
                ? null
                : Math.Round((double)numerator / denominator, 3);

            modes.Add(new CaptureMode
            {
                Width = video.Width,
                Height = video.Height,
                Fps = fps,
                FrameRateNumerator = numerator,
                FrameRateDenominator = denominator,
                PixelFormat = video.Subtype,
                MediaType = video.Type
            });
        }

        return modes;
    }

    private static CameraControls ReadControls(VideoDeviceController controller)
    {
        return new CameraControls
        {
            AutoExposure = Supported(controller.ExposureControl.Supported),
            Exposure = Supported(controller.Exposure.Capabilities.Supported),
            Gain = Supported(controller.IsoSpeedControl.Supported),
            WhiteBalance = Supported(controller.WhiteBalance.Capabilities.Supported),
            AutoWhiteBalance = CapabilityStatus.Unknown,
            BacklightCompensation = Supported(controller.BacklightCompensation.Capabilities.Supported),
            LowLightCompensation = CapabilityStatus.Unknown
        };
    }

    private static CameraControls CreateUnknownControls() => new()
    {
        AutoExposure = CapabilityStatus.Unknown,
        Exposure = CapabilityStatus.Unknown,
        Gain = CapabilityStatus.Unknown,
        WhiteBalance = CapabilityStatus.Unknown,
        AutoWhiteBalance = CapabilityStatus.Unknown,
        BacklightCompensation = CapabilityStatus.Unknown,
        LowLightCompensation = CapabilityStatus.Unknown
    };

    private static string Supported(bool supported) =>
        supported ? CapabilityStatus.Yes : CapabilityStatus.No;

    private static (string? VendorId, string? ProductId) ParseVendorProduct(string deviceId)
    {
        var match = VendorProductPattern.Match(deviceId);
        return match.Success
            ? (match.Groups["vid"].Value.ToUpperInvariant(), match.Groups["pid"].Value.ToUpperInvariant())
            : (null, null);
    }

    private static string? ParseInterface(string deviceId)
    {
        var hashIndex = deviceId.IndexOf('#');
        if (hashIndex <= 0)
        {
            return null;
        }

        var prefix = deviceId[..hashIndex];
        var separatorIndex = Math.Max(prefix.LastIndexOf('\\'), prefix.LastIndexOf('/'));
        return prefix[(separatorIndex + 1)..] is { Length: > 0 } value
            ? value.ToUpperInvariant()
            : null;
    }

    private static void PrintReport(ProbeReport report)
    {
        Console.WriteLine();
        Console.WriteLine("=== Test 01 — Device Enumeration ===");
        if (report.EnumerationError is not null)
        {
            Console.WriteLine($"列舉失敗：{report.EnumerationError}");
        }
        else if (report.Cameras.Count == 0)
        {
            Console.WriteLine("未偵測到視訊擷取裝置。");
        }
        else
        {
            foreach (var camera in report.Cameras)
            {
                Console.WriteLine($"- {camera.DeviceName}");
                Console.WriteLine($"  Device ID: {camera.DeviceId}");
                Console.WriteLine($"  VID/PID: {camera.VendorId ?? "null"}/{camera.ProductId ?? "null"}");
                Console.WriteLine($"  Interface: {camera.Interface ?? "null"}");
                Console.WriteLine($"  Camera API: {camera.CameraApi}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Test 02 — Capture Capability Probe ===");
        foreach (var camera in report.Cameras)
        {
            Console.WriteLine($"[{camera.DeviceName}]");
            if (camera.ProbeError is not null)
            {
                Console.WriteLine($"  探測失敗：{camera.ProbeError}");
                continue;
            }

            if (camera.CaptureModes.Count == 0)
            {
                Console.WriteLine("  未公布 VideoRecord Capture Mode。");
                continue;
            }

            foreach (var mode in camera.CaptureModes)
            {
                var fps = mode.Fps?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
                Console.WriteLine($"  - {mode.Width}x{mode.Height} @ {fps} FPS / {mode.PixelFormat} / {mode.MediaType}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Test 03 — Camera Control Capability ===");
        foreach (var camera in report.Cameras)
        {
            Console.WriteLine($"[{camera.DeviceName}]");
            PrintControl("Auto Exposure", camera.Controls.AutoExposure);
            PrintControl("Exposure", camera.Controls.Exposure);
            PrintControl("Gain (IsoSpeedControl)", camera.Controls.Gain);
            PrintControl("White Balance", camera.Controls.WhiteBalance);
            PrintControl("Auto White Balance", camera.Controls.AutoWhiteBalance);
            PrintControl("Backlight Compensation", camera.Controls.BacklightCompensation);
            PrintControl("Low Light Compensation", camera.Controls.LowLightCompensation);
        }
    }

    private static void PrintControl(string name, string status) =>
        Console.WriteLine($"  {name,-25} {status}");

    private static string WriteReport(ProbeReport report)
    {
        var projectDirectory = FindProjectDirectory();
        var rawDataDirectory = Path.Combine(projectDirectory, "raw-data");
        Directory.CreateDirectory(rawDataDirectory);

        var fileName = $"camera-probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss'Z'}.json";
        var outputPath = Path.Combine(rawDataDirectory, fileName);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(outputPath, json, new System.Text.UTF8Encoding(false));
        return outputPath;
    }

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

    private static string FormatException(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message}";
}

internal static class CapabilityStatus
{
    public const string Yes = "Yes";
    public const string No = "No";
    public const string Unknown = "Unknown";
}

internal sealed class ProbeReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    [JsonPropertyName("camera_api")]
    public string CameraApi { get; init; } = string.Empty;

    [JsonPropertyName("enumeration_error")]
    public string? EnumerationError { get; init; }

    [JsonPropertyName("cameras")]
    public IReadOnlyList<CameraProbeResult> Cameras { get; init; } = [];
}

internal sealed class CameraProbeResult
{
    [JsonPropertyName("device_name")]
    public string DeviceName { get; init; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("vendor_id")]
    public string? VendorId { get; init; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; init; }

    [JsonPropertyName("interface")]
    public string? Interface { get; init; }

    [JsonPropertyName("camera_api")]
    public string CameraApi { get; init; } = string.Empty;

    [JsonPropertyName("capture_modes")]
    public IReadOnlyList<CaptureMode> CaptureModes { get; set; } = [];

    [JsonPropertyName("controls")]
    public CameraControls Controls { get; set; } = new();

    [JsonPropertyName("probe_error")]
    public string? ProbeError { get; set; }
}

internal sealed class CaptureMode
{
    [JsonPropertyName("width")]
    public uint Width { get; init; }

    [JsonPropertyName("height")]
    public uint Height { get; init; }

    [JsonPropertyName("fps")]
    public double? Fps { get; init; }

    [JsonPropertyName("frame_rate_numerator")]
    public uint FrameRateNumerator { get; init; }

    [JsonPropertyName("frame_rate_denominator")]
    public uint FrameRateDenominator { get; init; }

    [JsonPropertyName("pixel_format")]
    public string PixelFormat { get; init; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;
}

internal sealed class CameraControls
{
    [JsonPropertyName("auto_exposure")]
    public string AutoExposure { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("exposure")]
    public string Exposure { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("gain")]
    public string Gain { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("white_balance")]
    public string WhiteBalance { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("auto_white_balance")]
    public string AutoWhiteBalance { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("backlight_compensation")]
    public string BacklightCompensation { get; init; } = CapabilityStatus.Unknown;

    [JsonPropertyName("low_light_compensation")]
    public string LowLightCompensation { get; init; } = CapabilityStatus.Unknown;
}
