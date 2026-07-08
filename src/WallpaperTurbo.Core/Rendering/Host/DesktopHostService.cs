// DesktopHostService.cs

using System;
using System.Collections.Generic;
using WallpaperTurbo.Core.Display;
using WallpaperTurbo.Core.Interop;

namespace WallpaperTurbo.Core.Rendering.Host;

public sealed class DesktopHostService
{
    private readonly List<IDesktopHostStrategy>
        _strategies;

    public DesktopHostService()
        : this(
            new IDesktopHostStrategy[]
            {
                new DesktopCompositionStrategy(),
                new DesktopShellStrategy(),
                new WorkerWStrategy()
            })
    {
    }

    internal DesktopHostService(IEnumerable<IDesktopHostStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = new List<IDesktopHostStrategy>(strategies);
    }

    public bool Attach(
        IntPtr hwnd,
        MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        foreach (IDesktopHostStrategy strategy in _strategies)
        {
            try
            {
                Console.WriteLine(
                    $"[Host] Evaluating strategy: {strategy.Name}");

                if (!strategy.IsSupported())
                {
                    Console.WriteLine(
                        $"[Host] Unsupported strategy: {strategy.Name}");

                    continue;
                }

                Console.WriteLine(
                    $"[Host] Trying strategy: {strategy.Name}");

                bool attached =
                    strategy.TryAttach(
                        hwnd,
                        monitor);

                if (!attached)
                {
                    Console.WriteLine(
                        $"[Host] Strategy failed: {strategy.Name}");

                    continue;
                }

                LogWindowState(
                    hwnd,
                    strategy.Name);

                Console.WriteLine(
                    $"[Host] Strategy succeeded: {strategy.Name}");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Host] Strategy exception ({strategy.Name}): {ex}");
            }
        }

        Console.WriteLine(
            "[Host] All desktop host strategies failed.");

        return false;
    }

    private static void LogWindowState(
        IntPtr hwnd,
        string strategyName)
    {
        IntPtr parent =
            NativeMethods.GetParent(hwnd);

        int style =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_STYLE);

        int exStyle =
            NativeMethods.GetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE);

        Console.WriteLine(
            $"[Host:{strategyName}] HWND=0x{hwnd.ToInt64():X}");

        Console.WriteLine(
            $"[Host:{strategyName}] Parent=0x{parent.ToInt64():X}");

        Console.WriteLine(
            $"[Host:{strategyName}] Style=0x{style:X8}");

        Console.WriteLine(
            $"[Host:{strategyName}] ExStyle=0x{exStyle:X8}");

        Console.WriteLine(
            $"[Host:{strategyName}] Visible={NativeMethods.IsWindowVisible(hwnd)}");
    }
}
