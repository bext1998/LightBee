namespace Wcalss.AmbientBrightness;

/// <summary>
/// 換相機時的相容性協商：裝置名稱比對與原生格式挑選都不再假設特定型號（對應 spike-report §17）。
/// 全部是純邏輯、不碰 WinRT，方便 --selftest 直接驗證。
/// </summary>
internal static class CameraCompatibility
{
    internal enum DeviceMatchKind
    {
        ExactName,
        UniquePartialName,
        SoleDevice,
        None,
    }

    internal readonly record struct DeviceMatch(int Index, DeviceMatchKind Kind, string Note)
    {
        public bool Found => Index >= 0;

        public static DeviceMatch NotFound(IReadOnlyList<string> names) => new(
            -1,
            DeviceMatchKind.None,
            names.Count == 0
                ? "沒有列舉到任何視訊裝置"
                : $"設定的裝置名稱對不上，且列舉到多台、無法自動判斷：{string.Join(" | ", names)}");
    }

    /// <summary>
    /// 依序嘗試：完全同名（大小寫不敏感）→ 唯一的部分符合（互為子字串）→ 只列舉到一台就直接採用。
    /// 都不成立時 <see cref="DeviceMatch.Found"/> 為 false，<see cref="DeviceMatch.Note"/> 說明原因。
    /// </summary>
    public static DeviceMatch ResolveDevice(IReadOnlyList<string> deviceNames, string? configuredName)
    {
        var configured = (configuredName ?? string.Empty).Trim();

        var exact = IndexWhere(deviceNames, n => string.Equals(n, configured, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0)
        {
            return new DeviceMatch(exact, DeviceMatchKind.ExactName, $"完全符合設定的裝置名稱「{configured}」");
        }

        if (configured.Length > 0)
        {
            var partial = IndicesWhere(deviceNames, n =>
                n.Contains(configured, StringComparison.OrdinalIgnoreCase) ||
                configured.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (partial.Count == 1)
            {
                return new DeviceMatch(
                    partial[0],
                    DeviceMatchKind.UniquePartialName,
                    $"設定「{configured}」與唯一相符的裝置「{deviceNames[partial[0]]}」部分吻合");
            }
        }

        if (deviceNames.Count == 1)
        {
            return new DeviceMatch(
                0,
                DeviceMatchKind.SoleDevice,
                configured.Length == 0
                    ? $"未設定裝置名稱，採用唯一列舉到的「{deviceNames[0]}」"
                    : $"設定「{configured}」對不上，但只列舉到一台，改用「{deviceNames[0]}」");
        }

        return DeviceMatch.NotFound(deviceNames);
    }

    /// <summary>單一原生擷取格式的可比較特徵，對應 <c>MediaFrameFormat</c> 但不依賴 WinRT。</summary>
    internal readonly record struct FormatCandidate(int Width, int Height, string Subtype, double FramesPerSecond);

    /// <summary>
    /// 不再要求 NV12：優先未壓縮格式（NV12 &gt; YUY2 &gt; 其他未壓縮 &gt; MJPG），再優先接近 640x480
    /// 的解析度與 ~30fps。回傳挑中的 index；沒有任何有效格式回傳 -1。
    /// 影像最終會在 MediaFrameReader 輸出端統一轉成 NV12（見 <see cref="AmbientLightSensor"/>），
    /// 所以這裡挑的是「原生擷取格式」，能協商成功即可。
    /// </summary>
    public static int SelectFormatIndex(IReadOnlyList<FormatCandidate> formats)
    {
        var best = -1;
        (int Subtype, int AreaDelta, double FpsDelta) bestKey = default;

        for (var i = 0; i < formats.Count; i++)
        {
            var format = formats[i];
            if (format.Width <= 0 || format.Height <= 0)
            {
                continue;
            }

            var key = (
                Subtype: SubtypeRank(format.Subtype),
                AreaDelta: Math.Abs(format.Width * format.Height - (640 * 480)),
                FpsDelta: Math.Abs(format.FramesPerSecond - 30.0));

            if (best < 0 || IsBetter(key, bestKey))
            {
                best = i;
                bestKey = key;
            }
        }

        return best;
    }

    private static bool IsBetter(
        (int Subtype, int AreaDelta, double FpsDelta) candidate,
        (int Subtype, int AreaDelta, double FpsDelta) incumbent)
    {
        if (candidate.Subtype != incumbent.Subtype)
        {
            return candidate.Subtype < incumbent.Subtype;
        }

        if (candidate.AreaDelta != incumbent.AreaDelta)
        {
            return candidate.AreaDelta < incumbent.AreaDelta;
        }

        return candidate.FpsDelta < incumbent.FpsDelta;
    }

    private static int SubtypeRank(string subtype) => (subtype ?? string.Empty).ToUpperInvariant() switch
    {
        "NV12" => 0,
        "YUY2" or "YUYV" => 1,
        "MJPG" or "MJPEG" => 3,
        _ => 2, // 其他未壓縮（RGB24/RGB32/UYVY…）：能轉就試，比 MJPG 的整張解碼便宜。
    };

    private static int IndexWhere(IReadOnlyList<string> items, Func<string, bool> predicate)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (predicate(items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<int> IndicesWhere(IReadOnlyList<string> items, Func<string, bool> predicate)
    {
        var result = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (predicate(items[i]))
            {
                result.Add(i);
            }
        }

        return result;
    }
}
