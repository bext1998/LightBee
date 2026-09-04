namespace Wcalss.AmbientBrightness;

/// <summary>
/// 自適應 EMA 平滑：小幅擾動維持原本的 α=0.5（13.2 節實測校準過的值，防單次雜訊突波）；
/// 大幅變化（|raw − 平滑值| ≥ StrongDeltaThreshold，例如開關燈造成 0.45 以上的落差）改用更高的 α，
/// 讓平滑值在 2-3 次取樣內追上真實變化，避免指數逼近的長尾把「已經發生的環境變化」拖慢數十秒。
///
/// 強 α 不只作用一次：第一次大落差把平滑值拉近 raw 之後，下一次的 delta 自然變小，
/// 若只看單次 delta 會立刻退回 α=0.5、又掉回指數長尾。因此強 α 觸發後會持續
/// StrongAlphaTailSamples 次取樣（期間即使 delta 變小仍用強 α），除非讀值已回到原範圍
/// （這正是單次突波的情況：下一次 raw 跳回原側，大落差會重新觸發強 α 把平滑值快速拉回去）。
///
/// 防抖語意不變：突波即使被高 α 拉動，分級切換仍受「連續兩次確認」（14.5 節）與遲滯機制把關。
/// </summary>
internal sealed class SampleSmoother
{
    private const double BaseAlpha = 0.5;
    private const double StrongAlpha = 0.9;
    private const double StrongDeltaThreshold = 0.1;
    private const int StrongAlphaTailSamples = 2;
    private int strongTailRemaining;

    /// <summary>回傳新的平滑值；previous 為 null（第一筆）時直接採用 raw。alphaUsed 回報本輪實際使用的係數，供驗證紀錄診斷。</summary>
    public double? Smooth(double? previous, double raw, out double alphaUsed)
    {
        if (previous is null)
        {
            alphaUsed = 1.0;
            strongTailRemaining = 0;
            return raw;
        }

        var delta = Math.Abs(raw - previous.Value);
        if (delta >= StrongDeltaThreshold)
        {
            strongTailRemaining = StrongAlphaTailSamples;
        }

        var strong = strongTailRemaining > 0;
        if (strong)
        {
            strongTailRemaining--;
        }

        alphaUsed = strong ? StrongAlpha : BaseAlpha;
        return (alphaUsed * raw) + ((1 - alphaUsed) * previous.Value);
    }
}