using System.Globalization;
using System.Text;

namespace Wcalss.AmbientBrightness;

/// <summary>一次相機取樣的低階時序與釋放診斷。對應 docs/spike-report.md §16 的釋放路徑加固與待做的 Test 13。</summary>
internal sealed record SampleDiagnostics
{
    public long InitializeMs { get; init; }
    public string StartStatus { get; init; } = "";
    public int FramesArrived { get; init; }
    public long SampleWindowMs { get; init; }
    public long StopAsyncMs { get; init; }

    /// <summary>StopAsync 是否卡住到逾時（true 就是 §13.3 觀察到的那種卡死的直接證據）。</summary>
    public bool StopAsyncTimedOut { get; init; }
    public long ReaderDisposeMs { get; init; }
    public long MediaCaptureDisposeMs { get; init; }

    /// <summary>none／initialize／start／post-init／no-frames，指出失敗發生在哪一步。</summary>
    public string FailedStep { get; init; } = "none";
}

/// <summary>一次裝置與格式協商（PrepareAsync）的診斷，用來看「開機後相機到底有沒有被列舉」。</summary>
internal sealed record PrepareDiagnostics
{
    public long DeviceEnumerationMs { get; init; }
    public IReadOnlyList<string> EnumeratedDevices { get; init; } = Array.Empty<string>();
    public bool TargetDeviceFound { get; init; }
    public long SourceNegotiationMs { get; init; }
    public string ResolvedFormat { get; init; } = "";
}

/// <summary>
/// 相機工作階段本身的健康紀錄，寫到獨立於 validation-log.csv 的 CSV：
/// validation-log 是「取樣→分級→套用」對應 Spike Gate/Test 的業務紀錄；
/// 這裡是「相機工作階段」的低階時序——init 花多久、收到幾個 frame、StopAsync 是否卡住、
/// Dispose 是否跑到——用來佐證 spike-report §13.3／§13.4 的相機卡死與尚未正式化的 Test 13。
/// 寫入失敗（例如被 Excel 鎖住）不影響主要行為，與 <see cref="ValidationLog"/> 一致。
/// </summary>
internal sealed class CameraDiagnosticsLog
{
    private const string Header =
        "timestamp_utc,phase,success,detail,initialize_ms,start_status,frames_arrived,sample_window_ms," +
        "stop_async_ms,stop_async_timed_out,reader_dispose_ms,media_capture_dispose_ms,failed_step," +
        "device_enum_ms,enumerated_devices,target_device_found,source_negotiation_ms,resolved_format";

    private readonly string path;
    private readonly object gate = new();

    public CameraDiagnosticsLog()
    {
        path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WCALSS", "AmbientBrightness", "camera-diagnostics.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Header + Environment.NewLine, Encoding.UTF8);
        }
    }

    public string Path_ => path;

    public void Append(string phase, bool success, string? detail, SampleDiagnostics? sample = null, PrepareDiagnostics? prepare = null)
    {
        var line = string.Join(",",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            phase,
            success,
            Escape(detail),
            Num(sample?.InitializeMs),
            Escape(sample?.StartStatus),
            Num(sample?.FramesArrived),
            Num(sample?.SampleWindowMs),
            Num(sample?.StopAsyncMs),
            Flag(sample?.StopAsyncTimedOut),
            Num(sample?.ReaderDisposeMs),
            Num(sample?.MediaCaptureDisposeMs),
            Escape(sample?.FailedStep),
            Num(prepare?.DeviceEnumerationMs),
            Escape(prepare is null ? null : string.Join(" | ", prepare.EnumeratedDevices)),
            Flag(prepare?.TargetDeviceFound),
            Num(prepare?.SourceNegotiationMs),
            Escape(prepare?.ResolvedFormat));

        lock (gate)
        {
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 診斷紀錄寫入失敗不影響主要行為。
            }
        }
    }

    private static string Num(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Num(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Flag(bool? value) => value?.ToString() ?? "";

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{escaped}\"" : escaped;
    }
}
