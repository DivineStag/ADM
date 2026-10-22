using System.Runtime.InteropServices;
namespace ADM.Desktop.Verification;

internal static class NativeInputChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point(int x, int y) { public readonly int X = x, Y = y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    public static void AssertNoOutsideInput(nint window)
    {
        if (!GetWindowRect(window, out var rect)) throw new InvalidOperationException("Window bounds unavailable.");
        foreach (var point in new[] { new Point(rect.Left - 20, rect.Top + 100), new Point(rect.Right + 20, rect.Top + 100), new Point(rect.Left + 100, rect.Bottom + 20) })
        {
            GetWindowThreadProcessId(WindowFromPoint(point), out uint process);
            if (process == Environment.ProcessId) throw new InvalidOperationException("ADM intercepted input outside its window.");
        }
    }
}
