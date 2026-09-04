using System.Globalization;
using System.Text;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 每一次「取樣 → 判定分級 → 套用亮度」的循環都寫一筆紀錄，並標註對應到 Spike 報告的哪個 Gate/Test。
/// 這是回應「確認測過的項目都能被驗證」這個目的的核心機制：不是只讓程式跑起來，
/// 而是讓每一次自動調整都能回溯到 docs/spike-report.md 裡的哪一筆實測結論。
/// </summary>
internal sealed class ValidationLogEntry
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required bool SampleSucceeded { get; init; }
    public double? MeanLuminance { get; init; }
    public string? BandLabel { get; init; }
    public int? AppliedBrightnessPercent { get; init; }
    public bool BrightnessApplySucceeded { get; init; }
    public required string ValidatedBy { get; init; }
    public string? Note { get; init; }
}

internal sealed class ValidationLog
{
    private const string Header = "timestamp_utc,sample_succeeded,mean_luminance,band_label,applied_brightness_percent,brightness_apply_succeeded,validated_by,note";
    private readonly string path;
    private readonly object gate = new();
    private readonly List<ValidationLogEntry> recent = new();

    public ValidationLog()
    {
        path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WCALSS", "AmbientBrightness", "validation-log.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Header + Environment.NewLine, Encoding.UTF8);
        }
    }

    public string Path_ => path;

    public IReadOnlyList<ValidationLogEntry> RecentEntries
    {
        get { lock (gate) { return recent.ToList(); } }
    }

    public void Append(ValidationLogEntry entry)
    {
        lock (gate)
        {
            recent.Add(entry);
            if (recent.Count > 200)
            {
                recent.RemoveAt(0);
            }
        }

        var line = string.Join(",",
            entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.SampleSucceeded,
            entry.MeanLuminance?.ToString("F6", CultureInfo.InvariantCulture) ?? "",
            Escape(entry.BandLabel),
            entry.AppliedBrightnessPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.BrightnessApplySucceeded,
            Escape(entry.ValidatedBy),
            Escape(entry.Note));

        try
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // CSV 可能被 Excel 鎖住或因儲存空間不足而無法寫入；記憶體中的近期紀錄仍可正常顯示。
        }
    }

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
