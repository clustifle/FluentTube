namespace FluentTube;
internal static class PlaybackMath
{
    internal static (double Width, double Height) PipSize(double ratio, double maxWidth = 420, double maxHeight = 420)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) ratio = 16d / 9;
        var width = Math.Min(320, maxWidth);
        var height = width / ratio;
        if (height > maxHeight) { height = maxHeight; width = height * ratio; }
        return (width, height);
    }
    internal static string Time(double seconds) => !double.IsFinite(seconds) || seconds < 0 ? "0:00" : TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    internal static double Seek(double position, double offset, double duration) =>
        !double.IsFinite(duration) || duration <= 0 ? 0 : Math.Clamp(position + offset, 0, duration);
    internal static double Resume(double saved, double duration) =>
        double.IsFinite(saved) && saved >= 5 && duration > 0 && saved < duration - 10 ? saved : 0;
}
