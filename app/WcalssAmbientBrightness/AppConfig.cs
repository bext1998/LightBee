using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Media.Capture;

namespace Wcalss.AmbientBrightness;

internal sealed class AppConfig
{
    public bool AutoAdjustEnabled { get; set; } = true;
    public string DeviceName { get; set; } = "USB Camera";
    public string SharingMode { get; set; } = "shared"; // Test 10 發現 SharedReadOnly 起始讀數比 ExclusiveControl 穩定
    public int SampleIntervalMs { get; set; } = 5000;

    // 自適應取樣節奏（回應「環境亮度感知太慢」的需求，軟體層方案）：
    // 穩定時維持 SampleIntervalMs（Gate C 低佔用不變）；讀值大幅變化或逼近分級邊界時，
    // 縮短到 AdaptiveFastIntervalMs 讓防抖機制（以取樣次數計）在時間軸上自動縮短。
    // 快模式有次數上限（AdaptiveMaxFastCycles，超過強制一輪慢取樣），
    // 取樣失敗會立刻退回慢間隔——13.4 節實測高頻相機操作可能讓 FrameServer 卡死，不對疑似故障的相機連續開關。
    public int AdaptiveFastIntervalMs { get; set; } = 500;
    public double AdaptiveDeltaThreshold { get; set; } = 0.03;
    public double AdaptiveBoundaryMargin { get; set; } = 0.05;
    public int AdaptiveMaxFastCycles { get; set; } = 30;
    public double HysteresisMargin { get; set; } = 0.02;
    public List<LuminanceBandConfig> Bands { get; set; } = BrightnessMapper.DefaultBands
        .Select(b => new LuminanceBandConfig
        {
            Label = b.Label,
            UpperBound = b.UpperBound == double.MaxValue ? -1 : b.UpperBound,
            TargetBrightnessPercent = b.TargetBrightnessPercent,
            ValidatedBy = b.ValidatedBy
        }).ToList();

    [JsonIgnore]
    public MediaCaptureSharingMode ResolvedSharingMode =>
        string.Equals(SharingMode, "exclusive", StringComparison.OrdinalIgnoreCase)
            ? MediaCaptureSharingMode.ExclusiveControl
            : MediaCaptureSharingMode.SharedReadOnly;

    public IReadOnlyList<LuminanceBand> ToBands() =>
        Bands
            .OrderBy(b => b.UpperBound == -1 ? double.MaxValue : b.UpperBound)
            .Select(b => new LuminanceBand
            {
                Label = b.Label,
                UpperBound = b.UpperBound == -1 ? double.MaxValue : b.UpperBound,
                TargetBrightnessPercent = b.TargetBrightnessPercent,
                ValidatedBy = b.ValidatedBy
            }).ToList();

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WCALSS", "AmbientBrightness", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded is not null && loaded.Bands.Count > 0)
                {
                    loaded.SampleIntervalMs = Math.Clamp(loaded.SampleIntervalMs, 1000, 300000);
                    loaded.HysteresisMargin = ClampFinite(loaded.HysteresisMargin, 0, 1);
                    loaded.AdaptiveFastIntervalMs = Math.Clamp(loaded.AdaptiveFastIntervalMs, 200, 300000);
                    loaded.AdaptiveDeltaThreshold = ClampFinite(loaded.AdaptiveDeltaThreshold, 0, 1);
                    loaded.AdaptiveBoundaryMargin = ClampFinite(loaded.AdaptiveBoundaryMargin, 0, 1);
                    loaded.AdaptiveMaxFastCycles = Math.Clamp(loaded.AdaptiveMaxFastCycles, 1, 30);
                    return loaded;
                }
            }
        }
        catch
        {
            // 設定檔壞掉就退回預設值，不讓程式因此無法啟動。
        }

        return new AppConfig();
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    public void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

internal sealed class LuminanceBandConfig
{
    public string Label { get; set; } = string.Empty;
    /// <summary>-1 代表「以上」（沒有上界）。</summary>
    public double UpperBound { get; set; }
    public int TargetBrightnessPercent { get; set; }
    public string ValidatedBy { get; set; } = string.Empty;
}
