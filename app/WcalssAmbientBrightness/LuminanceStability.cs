namespace Wcalss.AmbientBrightness;

/// <summary>
/// 取樣窗提前結束的判定。門檻沿用 Test 04/05 的收斂定義：
/// 「最近 N 個 frame 的 Mean Luminance max−min ≤ 0.01」——只是把「收滿固定窗長才檢查」
/// 改成「窗內每 100ms 檢查一次，一穩定就提前結束」，收斂快的取樣不必每次都等滿 1.2 秒。
/// </summary>
internal static class LuminanceStability
{
    /// <summary>window 是收斂期之後的 frame 平均亮度序列（依時間排序）。</summary>
    public static bool IsStable(IReadOnlyList<double> window, int minimumCount, double tolerance)
    {
        if (window.Count < minimumCount)
        {
            return false;
        }

        double min = double.MaxValue, max = double.MinValue;
        foreach (var value in window)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        return max - min <= tolerance;
    }
}
