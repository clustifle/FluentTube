using System.Runtime.InteropServices;
namespace FluentTube;

// Keep native resize cursors and the Windows live-resize loop; constrain its proposed rectangle.
internal sealed class PipWindowSizing : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMax { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr window, uint message, nuint wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, WindowProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, WindowProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, nuint wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    private const nuint Id = 0x46545049;
    private readonly IntPtr window;
    private readonly WindowProc callback;
    private readonly Func<bool> enabled;
    private readonly Func<double> ratio;
    private readonly Func<(int Width, int Height)> bounds;
    private readonly Action<double> resized;
    private bool disposed;
    private Rect? dragWindow, dragClient;
    private bool? cornerHeightDriven;
    internal bool IsResizing { get; private set; }
    internal PipWindowSizing(IntPtr window, Func<bool> enabled, Func<double> ratio, Func<(int Width, int Height)> bounds, Action<double> resized)
    {
        this.window = window; this.enabled = enabled; this.ratio = ratio; this.bounds = bounds; this.resized = resized;
        callback = Handle;
        if (!SetWindowSubclass(window, callback, Id, 0)) throw new InvalidOperationException("Windows could not enable PiP edge resizing.");
    }
    private IntPtr Handle(IntPtr handle, uint message, nuint wParam, IntPtr lParam, nuint id, nuint data)
    {
        try
        {
            if (!disposed && enabled())
            {
                if (message == 0x0084)
                {
                    var hit = DefSubclassProc(handle, message, wParam, lParam);
                    // The custom PiP header owns its buttons and drag area. Keep native edge hits.
                    return hit.ToInt64() is 2 or 8 or 9 or 20 ? new IntPtr(1) : hit;
                }
                if (message == 0x0112 && (wParam & 0xFFF0) == 0xF030) return IntPtr.Zero; // No maximize in PiP.
                if (message == 0x0024 && GetWindowRect(handle, out var tracked) && GetClientRect(handle, out var trackedClient))
                {
                    var result = DefSubclassProc(handle, message, wParam, lParam);
                    var info = Marshal.PtrToStructure<MinMax>(lParam);
                    var dpi = Math.Max(1, GetDpiForWindow(handle) / 96d);
                    var minWidth = Math.Max(1, Math.Min(140 * dpi, trackedClient.Right - trackedClient.Left));
                    var minHeight = Math.Max(1, Math.Min(80 * dpi, trackedClient.Bottom - trackedClient.Top));
                    var width = Math.Max(minWidth, minHeight * ratio());
                    info.MinTrack.X = (int)Math.Round(width) + tracked.Right - tracked.Left - (trackedClient.Right - trackedClient.Left);
                    info.MinTrack.Y = (int)Math.Round(width / ratio()) + tracked.Bottom - tracked.Top - (trackedClient.Bottom - trackedClient.Top);
                    var maximum = bounds(); info.MaxTrack.X = maximum.Width; info.MaxTrack.Y = maximum.Height;
                    Marshal.StructureToPtr(info, lParam, false);
                    return result;
                }
                if (message == 0x0231)
                {
                    IsResizing = true; cornerHeightDriven = null;
                    if (GetWindowRect(handle, out var initial)) dragWindow = initial;
                    if (GetClientRect(handle, out var initialClient)) dragClient = initialClient;
                } // WM_ENTERSIZEMOVE
                if (message == 0x0214 && GetWindowRect(handle, out var current) && GetClientRect(handle, out var client))
                {
                    var edge = (int)wParam;
                    var rect = Marshal.PtrToStructure<Rect>(lParam);
                    if (IsResizing) { current = dragWindow ?? current; client = dragClient ?? client; }
                    var frameWidth = current.Right - current.Left - (client.Right - client.Left);
                    var frameHeight = current.Bottom - current.Top - (client.Bottom - client.Top);
                    var deltaWidth = Math.Abs((rect.Right - rect.Left) - (current.Right - current.Left));
                    var deltaHeight = Math.Abs((rect.Bottom - rect.Top) - (current.Bottom - current.Top));
                    if (IsResizing && (edge is 4 or 5 or 7 or 8) && cornerHeightDriven is null && (deltaWidth > 1 || deltaHeight > 1)) cornerHeightDriven = deltaHeight * ratio() > deltaWidth;
                    var heightDriven = edge is 3 or 6 || (edge is 4 or 5 or 7 or 8) && (cornerHeightDriven ?? deltaHeight * ratio() > deltaWidth);
                    var limit = bounds();
                    var scale = Math.Max(1, GetDpiForWindow(handle) / 96d);
                    var result = PipResizeGeometry.Apply(new(rect.Left, rect.Top, rect.Right, rect.Bottom), edge, ratio(), frameWidth, frameHeight, heightDriven,
                        Math.Max(1, limit.Width - frameWidth), Math.Max(1, limit.Height - frameHeight),
                        Math.Max(1, Math.Min((int)(140 * scale), client.Right - client.Left)), Math.Max(1, Math.Min((int)(80 * scale), client.Bottom - client.Top)));
                    Marshal.StructureToPtr(new Rect { Left = result.Left, Top = result.Top, Right = result.Right, Bottom = result.Bottom }, lParam, false);
                    return new IntPtr(1);
                }
                if (message == 0x0232)
                {
                    IsResizing = false;
                    dragWindow = dragClient = null; cornerHeightDriven = null;
                    if (GetClientRect(handle, out var final)) resized((final.Right - final.Left) / Math.Max(1, GetDpiForWindow(handle) / 96d));
                } // WM_EXITSIZEMOVE
            }
            if (message == 0x0082) Dispose(); // WM_NCDESTROY
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        return DefSubclassProc(handle, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RemoveWindowSubclass(window, callback, Id);
    }
}
