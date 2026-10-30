namespace FluentTube;
internal static class PlaybackMath
{
    internal static string Time(double seconds) => !double.IsFinite(seconds) || seconds < 0 ? "0:00" : TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    internal static double Seek(double position, double offset, double duration) =>
        !double.IsFinite(duration) || duration <= 0 ? 0 : Math.Clamp(position + offset, 0, duration);
    internal static double Resume(double saved, double duration) =>
        double.IsFinite(saved) && saved >= 5 && duration > 0 && saved < duration - 10 ? saved : 0;
}
