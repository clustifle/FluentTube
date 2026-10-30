namespace FluentTube;

internal readonly record struct ResizeBounds(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
}
internal static class PipResizeGeometry
{
    // WM_SIZING edges: left, right, top, top-left, top-right, bottom, bottom-left, bottom-right.
    internal static ResizeBounds Apply(ResizeBounds proposed, int edge, double ratio, int frameWidth, int frameHeight, bool heightDriven, int maxWidth, int maxHeight, int minWidth = 200, int minHeight = 100)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) ratio = 16d / 9;
        var upperWidth = Math.Max(1, Math.Min(maxWidth, maxHeight * ratio));
        var lowerWidth = Math.Min(upperWidth, Math.Max(minWidth, minHeight * ratio));
        var width = heightDriven ? (proposed.Height - frameHeight) * ratio : proposed.Width - frameWidth;
        width = Math.Clamp(width, lowerWidth, upperWidth);
        var outerWidth = (int)Math.Round(width) + frameWidth;
        var outerHeight = (int)Math.Round(width / ratio) + frameHeight;
        var left = edge is 1 or 4 or 7 ? proposed.Right - outerWidth : proposed.Left;
        var top = edge is 3 or 4 or 5 ? proposed.Bottom - outerHeight : proposed.Top;
        return new(left, top, left + outerWidth, top + outerHeight);
    }
}
