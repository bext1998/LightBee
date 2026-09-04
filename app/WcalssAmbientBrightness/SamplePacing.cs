namespace Wcalss.AmbientBrightness;

/// <summary>
/// 自適應取樣間隔：穩定且遠離分級邊界時維持慢間隔（預設 5 秒，Gate C 的低佔用不變）；
/// 讀值大幅變化（|raw − 平滑值| ≥ deltaThreshold）或逼近任何分級邊界（距離 ≤ boundaryMargin）時切快間隔，
/// 讓「以取樣次數計」的防抖機制（EMA 收斂、連續兩次確認）在時間軸上自動縮短——
/// 防抖能力（次數門檻）完全不變，只是取樣密度跟著風險提高。
///
/// 對應 spike 報告的風險面：
/// - 13.4 節：高頻率相機操作可能讓 FrameServer 卡死。因此快模式有取樣次數上限（超過強制一輪慢取樣），
///   且取樣失敗立刻退回慢間隔，不對疑似故障的相機連續開關。
/// - 快間隔只會在「讀值正在變化或逼近邊界」的短暫期間生效，長期平均佔用跟原本幾乎相同。
/// </summary>
internal sealed class SamplePacing
{
    private readonly IReadOnlyList<double> boundaries;
    private readonly int slowIntervalMs;
    private readonly int fastIntervalMs;
    private readonly double deltaThreshold;
    private readonly double boundaryMargin;
    private readonly int maxFastCycles;
    private int fastCyclesUsed;

    public SamplePacing(
        IEnumerable<double> boundaries,
        int slowIntervalMs,
        int fastIntervalMs,
        double deltaThreshold,
        double boundaryMargin,
        int maxFastCycles)
    {
        if (fastIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fastIntervalMs), "快間隔必須為正整數。");
        }

        this.boundaries = boundaries.ToArray();
        this.slowIntervalMs = slowIntervalMs;
        this.fastIntervalMs = fastIntervalMs;
        this.deltaThreshold = deltaThreshold;
        this.boundaryMargin = boundaryMargin;
        this.maxFastCycles = maxFastCycles;
    }

    public bool IsFastMode { get; private set; }

    /// <summary>下一次取樣前應等待的時間。呼叫端在每次處理完取樣結果後重新設定 timer interval。</summary>
    public int NextIntervalMs() => IsFastMode ? fastIntervalMs : slowIntervalMs;

    /// <summary>每次取樣結束後回報結果，由本類別決定下一輪的節奏。失敗時一律退回慢間隔。</summary>
    public void OnSample(bool success, double raw, double smoothed)
    {
        if (!success)
        {
            IsFastMode = false;
            fastCyclesUsed = 0;
            return;
        }

        var shouldFast = Math.Abs(raw - smoothed) >= deltaThreshold || IsNearBoundary(raw) || IsNearBoundary(smoothed);
        if (!shouldFast)
        {
            IsFastMode = false;
            fastCyclesUsed = 0;
            return;
        }

        if (fastCyclesUsed >= maxFastCycles)
        {
            // 連續快取樣已達上限：強制一輪慢取稀，避免對相機無上限地高頻開關（13.4 節教訓）。
            // 下一輪取樣若仍符合快模式條件會自然重新進入。
            IsFastMode = false;
            fastCyclesUsed = 0;
            return;
        }

        IsFastMode = true;
        fastCyclesUsed++;
    }

    private bool IsNearBoundary(double value) =>
        boundaries.Any(boundary => Math.Abs(value - boundary) <= boundaryMargin);
}
