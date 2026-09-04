param(
    [ValidateRange(0, 100)]
    [int] $TargetPercent = -1
)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DdcCiProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PhysicalMonitor
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
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr handle, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr handle, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint count, [In, Out] PhysicalMonitor[] monitors);

    public static string[] Probe(int targetPercent)
    {
        var output = new List<string>();
        var callback = new MonitorEnumProc((hMonitor, hdcMonitor, lprcMonitor, data) =>
        {
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count))
            {
                output.Add(string.Format("HMONITOR {0}: enumerate failed, Win32={1}", hMonitor, Marshal.GetLastWin32Error()));
                return true;
            }

            var monitors = new PhysicalMonitor[(int)count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
            {
                output.Add(string.Format("HMONITOR {0}: physical monitor lookup failed, Win32={1}", hMonitor, Marshal.GetLastWin32Error()));
                return true;
            }

            try
            {
                foreach (var monitor in monitors)
                {
                    uint minimum;
                    uint current;
                    uint maximum;
                    if (GetMonitorBrightness(monitor.Handle, out minimum, out current, out maximum))
                    {
                        output.Add(string.Format("{0}: DDC/CI brightness supported, raw={1}, range={2}-{3}", monitor.Description, current, minimum, maximum));
                        if (targetPercent >= 0)
                        {
                            var target = minimum + (uint)Math.Round((maximum - minimum) * targetPercent / 100.0);
                            if (SetMonitorBrightness(monitor.Handle, target))
                            {
                                output.Add(string.Format("{0}: DDC/CI brightness set to raw={1} ({2}%)", monitor.Description, target, targetPercent));
                            }
                            else
                            {
                                output.Add(string.Format("{0}: DDC/CI brightness write failed, Win32={1}", monitor.Description, Marshal.GetLastWin32Error()));
                            }
                        }
                    }
                    else
                    {
                        output.Add(string.Format("{0}: DDC/CI brightness unavailable, Win32={1}", monitor.Description, Marshal.GetLastWin32Error()));
                    }
                }
            }
            finally
            {
                if (!DestroyPhysicalMonitors(count, monitors))
                {
                    output.Add(string.Format("HMONITOR {0}: physical monitor cleanup failed, Win32={1}", hMonitor, Marshal.GetLastWin32Error()));
                }
            }

            return true;
        });

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            output.Add(string.Format("EnumDisplayMonitors failed, Win32={0}", Marshal.GetLastWin32Error()));
        }

        return output.ToArray();
    }
}
'@

[DdcCiProbe]::Probe($TargetPercent)
