using System.Management;
using System.Runtime.InteropServices;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 先透過 WMI 控制內建 ACPI 面板；WMI 不可用或失敗時，改用 DDC/CI 控制外接螢幕。
/// DDC/CI 控制代碼必須在程式結束前釋放，避免保留 DXVA2 實體螢幕資源。
/// </summary>
internal sealed class DisplayBrightnessController : IDisposable
{
    private readonly List<DdcMonitor> ddcMonitors = [];
    private BrightnessControlMethod method;
    private bool probed;
    private string? wmiUnavailableReason;
    private string? ddcUnavailableReason;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public string ControlMethodDescription => method switch
    {
        BrightnessControlMethod.Wmi => "WMI / ACPI",
        BrightnessControlMethod.DdcCi => $"DDC/CI（{ddcMonitors.Count} 個實體螢幕）",
        _ => "不可用"
    };

    public int? CurrentBrightnessPercent
    {
        get
        {
            EnsureProbed();

            var current = method switch
            {
                BrightnessControlMethod.Wmi => TryGetWmiBrightness(),
                BrightnessControlMethod.DdcCi => TryGetDdcBrightness(),
                _ => null
            };

            return current ?? (method == BrightnessControlMethod.Wmi ? TryGetDdcBrightness() : TryGetWmiBrightness());
        }
    }

    public void Probe()
    {
        if (probed)
        {
            return;
        }

        probed = true;
        var wmiAvailable = ProbeWmi();
        var ddcAvailable = ProbeDdcCi();

        method = wmiAvailable
            ? BrightnessControlMethod.Wmi
            : ddcAvailable
                ? BrightnessControlMethod.DdcCi
                : BrightnessControlMethod.None;
        IsAvailable = method != BrightnessControlMethod.None;
        UnavailableReason = IsAvailable
            ? null
            : $"WMI：{wmiUnavailableReason} DDC/CI：{ddcUnavailableReason}";
    }

    /// <summary>設定亮度，成功回傳 true。WMI 失敗時會在有 DDC/CI 目標的情況下自動後備。</summary>
    public bool TrySetBrightness(int percent, out string? error)
    {
        EnsureProbed();
        percent = Math.Clamp(percent, 0, 100);

        if (method == BrightnessControlMethod.Wmi)
        {
            if (TrySetWmiBrightness(percent, out error))
            {
                return true;
            }

            var wmiError = error;
            string? ddcError = null;
            if (ddcMonitors.Count > 0 && TrySetDdcBrightness(percent, out ddcError))
            {
                method = BrightnessControlMethod.DdcCi;
                error = null;
                return true;
            }

            error = $"WMI：{wmiError} DDC/CI：{ddcError ?? ddcUnavailableReason}";
            return false;
        }

        if (method == BrightnessControlMethod.DdcCi)
        {
            if (TrySetDdcBrightness(percent, out error))
            {
                return true;
            }

            var ddcError = error;
            if (TrySetWmiBrightness(percent, out var wmiError))
            {
                method = BrightnessControlMethod.Wmi;
                error = null;
                return true;
            }

            error = $"DDC/CI：{ddcError} WMI：{wmiError}";
            return false;
        }

        error = UnavailableReason ?? "找不到可用的 WMI 或 DDC/CI 螢幕亮度控制。";
        return false;
    }

    public void Dispose()
    {
        foreach (var monitor in ddcMonitors)
        {
            DestroyPhysicalMonitor(monitor.Handle);
        }

        ddcMonitors.Clear();
    }

    private void EnsureProbed()
    {
        if (!probed)
        {
            Probe();
        }
    }

    private bool ProbeWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            if (searcher.Get().Count > 0)
            {
                return true;
            }

            wmiUnavailableReason = "系統回報 0 個 WmiMonitorBrightnessMethods 執行個體。";
        }
        catch (Exception ex)
        {
            wmiUnavailableReason = $"查詢 root\\wmi 失敗：{ex.GetType().Name}: {ex.Message}";
        }

        return false;
    }

    private bool ProbeDdcCi()
    {
        var errors = new List<string>();
        MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count))
            {
                errors.Add($"取得實體螢幕數量失敗（Win32 {Marshal.GetLastWin32Error()}）。");
                return true;
            }

            var physicalMonitors = new PhysicalMonitor[(int)count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
            {
                errors.Add($"取得實體螢幕控制代碼失敗（Win32 {Marshal.GetLastWin32Error()}）。");
                return true;
            }

            foreach (var physicalMonitor in physicalMonitors)
            {
                if (GetMonitorBrightness(physicalMonitor.Handle, out var minimum, out _, out var maximum))
                {
                    ddcMonitors.Add(new DdcMonitor(physicalMonitor.Handle, physicalMonitor.Description, minimum, maximum));
                }
                else
                {
                    errors.Add($"{physicalMonitor.Description} 不支援 DDC/CI 亮度（Win32 {Marshal.GetLastWin32Error()}）。");
                    DestroyPhysicalMonitor(physicalMonitor.Handle);
                }
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            errors.Add($"列舉顯示器失敗（Win32 {Marshal.GetLastWin32Error()}）。");
        }

        ddcUnavailableReason = ddcMonitors.Count > 0
            ? null
            : (errors.Count > 0 ? string.Join(" ", errors) : "系統沒有可用的 DDC/CI 實體螢幕。");
        return ddcMonitors.Count > 0;
    }

    private int? TryGetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementObject item in searcher.Get())
            {
                return Convert.ToInt32(item["CurrentBrightness"]);
            }
        }
        catch
        {
            // 無法讀取目前亮度不影響後續的設定操作。
        }

        return null;
    }

    private int? TryGetDdcBrightness()
    {
        var readings = new List<int>();
        foreach (var monitor in ddcMonitors)
        {
            if (GetMonitorBrightness(monitor.Handle, out var minimum, out var current, out var maximum) && maximum > minimum)
            {
                readings.Add((int)Math.Round((current - minimum) * 100d / (maximum - minimum)));
            }
        }

        return readings.Count == 0 ? null : (int)Math.Round(readings.Average());
    }

    private static bool TrySetWmiBrightness(int percent, out string? error)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            var found = false;
            foreach (ManagementObject item in searcher.Get())
            {
                item.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)percent });
                found = true;
            }

            error = found ? null : "找不到 WmiMonitorBrightnessMethods 執行個體。";
            return found;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private bool TrySetDdcBrightness(int percent, out string? error)
    {
        var errors = new List<string>();
        foreach (var monitor in ddcMonitors)
        {
            var rawBrightness = monitor.Minimum + (uint)Math.Round((monitor.Maximum - monitor.Minimum) * percent / 100d);
            if (!SetMonitorBrightness(monitor.Handle, rawBrightness))
            {
                errors.Add($"{monitor.Description}（Win32 {Marshal.GetLastWin32Error()}）");
            }
        }

        error = errors.Count == 0 ? null : $"無法設定 DDC/CI 亮度：{string.Join("、", errors)}";
        return errors.Count == 0 && ddcMonitors.Count > 0;
    }

    private enum BrightnessControlMethod
    {
        None,
        Wmi,
        DdcCi
    }

    private sealed record DdcMonitor(IntPtr Handle, string Description, uint Minimum, uint Maximum);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimumBrightness, out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint newBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
