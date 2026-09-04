namespace Wcalss.AmbientBrightness;

/// <summary>
/// 把「分級判定出來的目標亮度」跟「實際寫入螢幕的亮度」拆開，逐步逼近目標，
/// 而不是每次取樣就直接跳到目標值。這是使用者跟 codex 討論後採用的短期方案：
/// 手機/平板螢幕看起來「慢慢變暗」，主要不是感測端做連續映射，而是驅動層對目標值做速率限制的漸進套用。
/// 變亮跟變暗刻意設不同速度（變亮快、變暗慢），比對稱速度更接近手機的體感。
/// </summary>
internal sealed class BrightnessRamp
{
    private const double BrightenPercentPerSecond = 15.0;
    private const double DimPercentPerSecond = 8.0;

    /// <summary>差距小於這個值就視為已到達，不再送控制指令，避免無意義的高頻 DDC/CI 寫入。</summary>
    private const double MinApplyDeltaPercent = 1.0;

    private double? currentPercent;
    private double targetPercent;

    public bool HasTarget { get; private set; }
    public double TargetPercent => targetPercent;

    /// <summary>用螢幕實際回報的目前亮度校正起點，避免程式啟動時「以為」目前亮度跟第一個目標值一樣。</summary>
    public void SyncCurrent(double percent) => currentPercent = Math.Clamp(percent, 0, 100);

    public void SetTarget(double percent)
    {
        targetPercent = Math.Clamp(percent, 0, 100);
        HasTarget = true;
        currentPercent ??= targetPercent;
    }

    /// <summary>
    /// 前進一小步。回傳 null 代表已經到達目標（或還沒設定過目標），呼叫端不需要送控制指令。
    /// 回傳非 null 時是這一步該套用的亮度百分比（未四捨五入，呼叫端決定精度）。
    /// </summary>
    public double? Step(double elapsedMs)
    {
        if (!HasTarget || currentPercent is null)
        {
            return null;
        }

        var diff = targetPercent - currentPercent.Value;
        if (Math.Abs(diff) < MinApplyDeltaPercent)
        {
            currentPercent = targetPercent;
            return null;
        }

        var ratePerSecond = diff > 0 ? BrightenPercentPerSecond : DimPercentPerSecond;
        var maxStep = ratePerSecond * elapsedMs / 1000.0;
        var step = Math.Clamp(diff, -maxStep, maxStep);
        currentPercent = Math.Clamp(currentPercent.Value + step, 0, 100);
        return currentPercent;
    }

    public bool IsSettled => !HasTarget || currentPercent is null || Math.Abs(targetPercent - currentPercent.Value) < MinApplyDeltaPercent;
}
