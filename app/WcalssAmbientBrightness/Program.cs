using System.Windows.Forms;

namespace Wcalss.AmbientBrightness;

internal static class Program
{
    public static int Main(string[] args)
    {
        // 自檢模式：dotnet run -- --selftest（或 WCALSS.AmbientBrightness.exe --selftest）。
        // 不進入 WinForms 迴圈，純跑演算法邏輯的驗證，exit code 0 = 全部通過。
        if (args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            return SelfTest.RunAll(Console.Out);
        }

        if (args.Any(arg => string.Equals(arg, "--probe-metadata", StringComparison.OrdinalIgnoreCase)))
        {
            return CameraMetadataProbe.RunAsync(Console.Out).GetAwaiter().GetResult();
        }

        // 感光性能探測：--sensitivity-probe [次數]。用產品的 Lazy 取樣路徑連跑數次，
        // 印出 raw luminance 分布 + 曝光/ISO 能力，供換相機後重新校正（issue #13）。
        var sensitivityIndex = Array.FindIndex(args, arg => string.Equals(arg, "--sensitivity-probe", StringComparison.OrdinalIgnoreCase));
        if (sensitivityIndex >= 0)
        {
            var iterations = 12;
            if (sensitivityIndex + 1 < args.Length && int.TryParse(args[sensitivityIndex + 1], out var parsed))
            {
                iterations = parsed;
            }

            return CameraSensitivityProbe.RunAsync(Console.Out, iterations).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
        return 0;
    }
}
