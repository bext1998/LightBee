namespace Wcalss.AmbientBrightness;

/// <summary>
/// 一個亮度分級區間。上界 (UpperBound) 為 exclusive；最後一段用 double.MaxValue 表示「以上」。
/// 三段式門檻的預設值直接取自 docs/spike-report.md Test 06 / 5.1 節的實測平均值，
/// 不是任意假設：Dark ~0.0005、day-overcast（微光自然光）~0.022、有開燈 ~0.45-0.48。
/// Gate B 的結論明講 normal/bright 兩段測不出差異，所以這裡只做三段，不假裝能連續調光。
/// </summary>
internal sealed class LuminanceBand
{
    public required string Label { get; init; }
    public required double UpperBound { get; init; }
    public required int TargetBrightnessPercent { get; init; }
    public required string ValidatedBy { get; init; }
}

internal sealed class BrightnessMapper
{
    // 實測發現的 bug：EMA alpha 調高到 0.5（見 TrayContext 註解）之後，單一一次的異常讀值
    // （例如偶發雜訊突波，實測看過 0.003 附近突然單筆跳到 0.076）就能把平滑值直接推過邊界，
    // 遲滯緩衝量擋不住這麼大的單次突波，導致螢幕跟著雜訊「突然變亮又變暗」。
    // 修法：連續兩次判定都要換到同一個分級才真的切換，單一離群值不會有反應，
    // 真正持續性的環境變化（連續兩輪都指向同一個新分級）則不會被多擋一輪以上（約多 5 秒延遲，可接受）。
    private const int RequiredConsecutiveConfirmations = 2;

    private readonly List<LuminanceBand> bands;
    private readonly double hysteresis;
    private int? currentBandIndex;
    private int? pendingTargetIndex;
    private int pendingConfirmCount;

    public BrightnessMapper(IReadOnlyList<LuminanceBand> bands, double hysteresis)
    {
        if (bands.Count == 0)
        {
            throw new ArgumentException("至少需要一段亮度分級。", nameof(bands));
        }

        this.bands = bands.OrderBy(b => b.UpperBound).ToList();
        this.hysteresis = hysteresis;
    }

    public static IReadOnlyList<LuminanceBand> DefaultBands => new List<LuminanceBand>
    {
        new() { Label = "暗（無光）", UpperBound = 0.01, TargetBrightnessPercent = 15, ValidatedBy = "Gate B / Test 06 dark（平均 0.000536）" },
        new() { Label = "微光（僅自然光）", UpperBound = 0.20, TargetBrightnessPercent = 45, ValidatedBy = "Gate B / Test 06 §5.1 day-overcast（平均 0.022021）" },
        new() { Label = "有開燈", UpperBound = double.MaxValue, TargetBrightnessPercent = 80, ValidatedBy = "Gate B / Test 06 normal/bright/very-bright（平均 0.45～0.48，三段測不出差異，故合併為一段）" },
    };

    /// <summary>
    /// 依當前分級與遲滯區間決定是否切換，避免讀數在邊界附近抖動時反覆切換亮度。
    /// 回傳 null 代表維持原本的分級（仍在遲滯區間內，不動作）。
    /// </summary>
    public LuminanceBand? Evaluate(double meanLuminance)
    {
        var targetIndex = FindBandIndex(meanLuminance);

        if (currentBandIndex is null)
        {
            currentBandIndex = targetIndex;
            pendingTargetIndex = null;
            pendingConfirmCount = 0;
            return bands[targetIndex];
        }

        if (targetIndex == currentBandIndex)
        {
            pendingTargetIndex = null;
            pendingConfirmCount = 0;
            return null;
        }

        // 只有超過目前分級邊界 + 遲滯量才真的切換，避免在門檻附近反覆抖動。
        var movingUp = targetIndex > currentBandIndex;
        var boundary = movingUp
            ? bands[currentBandIndex.Value].UpperBound
            : bands[currentBandIndex.Value - 1].UpperBound;

        // 實測發現的 bug：往下切換時，若 boundary（例如「暗」分級門檻 0.01）比 hysteresis（預設 0.02）還小，
        // boundary - hysteresis 會變成負數，而亮度讀數不可能是負值，導致永遠無法真的切到最低那一段——
        // 畫面上會一直顯示分級標籤是「暗」（GetBand 是無狀態查詢），但 Evaluate 從未真的觸發切換套用。
        // 用 boundary/2 當底線，確保往下的有效邊界永遠是正值、且落在該分級實測讀數範圍內可以被跨過。
        var effectiveBoundary = movingUp ? boundary + hysteresis : Math.Max(boundary / 2, boundary - hysteresis);

        var crossed = movingUp ? meanLuminance >= effectiveBoundary : meanLuminance < effectiveBoundary;
        if (!crossed)
        {
            pendingTargetIndex = null;
            pendingConfirmCount = 0;
            return null;
        }

        if (pendingTargetIndex == targetIndex)
        {
            pendingConfirmCount++;
        }
        else
        {
            pendingTargetIndex = targetIndex;
            pendingConfirmCount = 1;
        }

        if (pendingConfirmCount < RequiredConsecutiveConfirmations)
        {
            return null;
        }

        pendingTargetIndex = null;
        pendingConfirmCount = 0;
        currentBandIndex = targetIndex;
        return bands[targetIndex];
    }

    public void Reset()
    {
        currentBandIndex = null;
        pendingTargetIndex = null;
        pendingConfirmCount = 0;
    }

    /// <summary>純查詢，不影響遲滯狀態；用於顯示「這個讀數目前落在哪一段」，即使還沒切換套用。</summary>
    public LuminanceBand GetBand(double meanLuminance) => bands[FindBandIndex(meanLuminance)];

    private int FindBandIndex(double meanLuminance)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            if (meanLuminance < bands[i].UpperBound)
            {
                return i;
            }
        }

        return bands.Count - 1;
    }
}
