using System.Runtime.InteropServices;
using FluentTube;

internal static class PipSizingChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMax { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, nuint wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
    internal static void Run()
    {
        // No WS_VISIBLE: this window never appears or takes focus.
        var window = CreateWindowEx(0x08000000, "STATIC", "FluentTube native resize test", 0x00CF0000, 100, 100, 500, 400, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            GetWindowRect(window, out var outer); GetClientRect(window, out var client);
            var frameWidth = outer.Right - outer.Left - (client.Right - client.Left);
            var frameHeight = outer.Bottom - outer.Top - (client.Bottom - client.Top);
            var active = true; var ended = false;
            using var sizing = new PipWindowSizing(window, () => active, () => 16d / 9, () => (1600, 1000), _ => ended = true);
            foreach (var edge in Enumerable.Range(1, 8))
            {
                Marshal.StructureToPtr(new Rect { Left = 100, Top = 100, Right = 800, Bottom = 650 }, memory, false);
                if (SendMessage(window, 0x0214, (nuint)edge, memory) != new IntPtr(1)) throw new Exception("Native sizing message was not handled.");
                var result = Marshal.PtrToStructure<Rect>(memory);
                var ratio = (result.Right - result.Left - frameWidth) / (double)(result.Bottom - result.Top - frameHeight);
                if (Math.Abs(ratio - 16d / 9) > 0.01) throw new Exception("Native subclass did not preserve the client aspect ratio.");
            }
            SendMessage(window, 0x0231, 0, IntPtr.Zero);
            if (!sizing.IsResizing) throw new Exception("Native resize-start event missing.");
            SendMessage(window, 0x0232, 0, IntPtr.Zero);
            if (sizing.IsResizing || !ended) throw new Exception("Native resize-end event missing.");
            var minmaxMemory = Marshal.AllocHGlobal(Marshal.SizeOf<MinMax>());
            try
            {
                Marshal.StructureToPtr(new MinMax { MinTrack = new Point { X = 900, Y = 900 } }, minmaxMemory, false);
                SendMessage(window, 0x0024, 0, minmaxMemory);
                var minimum = Marshal.PtrToStructure<MinMax>(minmaxMemory);
                if (minimum.MinTrack.X > 300 || minimum.MinTrack.Y > 250) throw new Exception("PiP inherited a large main-window minimum.");
            }
            finally { Marshal.FreeHGlobal(minmaxMemory); }
            // An unchanged first resize proposal must not jump from the actual current bounds.
            SendMessage(window, 0x0231, 0, IntPtr.Zero);
            Marshal.StructureToPtr(outer, memory, false);
            SendMessage(window, 0x0214, 2, memory);
            var first = Marshal.PtrToStructure<Rect>(memory);
            if (first.Right - first.Left > outer.Right - outer.Left + 1) throw new Exception("PiP enlarged at drag start.");
            SendMessage(window, 0x0232, 0, IntPtr.Zero);
            active = false;
            Marshal.StructureToPtr(new Rect { Left = 1, Top = 2, Right = 555, Bottom = 333 }, memory, false);
            SendMessage(window, 0x0214, 8, memory);
            if (Marshal.PtrToStructure<Rect>(memory).Right != 555) throw new Exception("Normal window sizing was intercepted outside PiP.");
            active = true; sizing.Dispose();
            SendMessage(window, 0x0214, 8, memory);
            if (Marshal.PtrToStructure<Rect>(memory).Right != 555) throw new Exception("Native resize callback remained installed after disposal.");
            Console.WriteLine("PASS native Windows PiP subclass: eight resize edges, aspect ratio, small tracking minimum, no drag-start growth, resize lifecycle, passthrough, and cleanup");
        }
        finally { Marshal.FreeHGlobal(memory); DestroyWindow(window); }
    }
}
