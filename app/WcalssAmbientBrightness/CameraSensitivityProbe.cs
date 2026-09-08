namespace Wcalss.AmbientBrightness;

/// <summary>
/// `--sensitivity-probe [次數]`：用產品實際的 Lazy 取樣路徑（<see cref="AmbientLightSensor.SampleOnceAsync"/>）
/// 連跑數次，印出每次的 raw mean luminance 與統計（min/max/mean/stddev/range），再接一段曝光／ISO
/// 能力探測。用來比較同一顆相機在不同光照條件下的讀值分布——換相機後重新校正（issue #13）的第一步。
/// 只讀不寫，不動 config、不動螢幕亮度。
/// </summary>
internal static class CameraSensitivityProbe
{
    public static async Task<int> RunAsync(TextWriter output, int iterations)
    {
        iterations = Math.Clamp(iterations, 3, 60);
        var config = AppConfig.Load();

        var sensor = new AmbientLightSensor(config.DeviceName, config.ResolvedSharingMode);
        try
        {
            await sensor.PrepareAsync();
        }
        catch (Exception ex)
        {
            output.WriteLine($"PrepareAsync 失敗：{ex.GetType().Name} (0x{ex.HResult:X8}): {(string.IsNullOrWhiteSpace(ex.Message) ? "(無訊息文字)" : ex.Message)}");
            var names = sensor.LastPrepareDiagnostics?.EnumeratedDevices ?? Array.Empty<string>();
            output.WriteLine($"列舉到的視訊裝置：{(names.Count == 0 ? "(無)" : string.Join(", ", names))}");
            return 1;
        }

        output.WriteLine($"Device   : {config.DeviceName}  [{sensor.ResolvedDeviceHardwareId}]");
        output.WriteLine($"Format   : {sensor.ResolvedFormatDescription}");
        output.WriteLine($"Sharing  : {config.ResolvedSharingMode}");
        output.WriteLine($"Samples  : {iterations}（每次為一輪 Open→~1.2s 取樣→Release，與產品相同）");
        output.WriteLine(new string('-', 60));

        var readings = new List<double>();
        var failures = 0;
        for (var i = 1; i <= iterations; i++)
        {
            var r = await sensor.SampleOnceAsync();
            if (r.Success)
            {
                readings.Add(r.MeanLuminance);
                output.WriteLine($"  #{i,2}  mean={r.MeanLuminance:F6}  frames={r.UsableFrames}/{r.TotalFrames}");
            }
            else
            {
                failures++;
                output.WriteLine($"  #{i,2}  FAIL  {r.Error}");
            }
        }

        output.WriteLine(new string('-', 60));
        if (readings.Count > 0)
        {
            var min = readings.Min();
            var max = readings.Max();
            var mean = readings.Average();
            var stddev = Math.Sqrt(readings.Average(v => (v - mean) * (v - mean)));
            output.WriteLine($"n={readings.Count}  失敗={failures}");
            output.WriteLine($"min={min:F6}  max={max:F6}  mean={mean:F6}  stddev={stddev:F6}  range={max - min:F6}");
        }
        else
        {
            output.WriteLine($"沒有任何成功取樣（失敗 {failures} 次），無法統計。");
        }

        output.WriteLine();
        output.WriteLine("在下列每種條件各跑一次，比較 mean 與 range：");
        output.WriteLine("  1) 關燈，相機朝螢幕      → 螢幕背光散射的影響");
        output.WriteLine("  2) 關燈，相機轉開不對螢幕  → 這套 rig 的『純環境暗基準』");
        output.WriteLine("  3) 關螢幕（或蓋住），關燈  → 對照 (2)");
        output.WriteLine("  4) 微光 / 只開一盞");
        output.WriteLine("  5) 一般開燈 / 最亮");
        output.WriteLine("  三段門檻（暗 / 微光 / 開燈）要相對 (2) 的實測最低值往上鋪，不要沿用舊的絕對值。");
        output.WriteLine();
        output.WriteLine("=== 曝光 / ISO 能力 ===");
        return await CameraMetadataProbe.RunAsync(output);
    }
}
