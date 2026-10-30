namespace FluentTube;
internal static class PlaybackMath
{
    internal static double Seek(double position, double offset, double duration) =>
        !double.IsFinite(duration) || duration <= 0 ? 0 : Math.Clamp(position + offset, 0, duration);
    internal static double Resume(double saved, double duration) =>
        double.IsFinite(saved) && saved >= 5 && duration > 0 && saved < duration - 10 ? saved : 0;
}
