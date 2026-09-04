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

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
        return 0;
    }
}