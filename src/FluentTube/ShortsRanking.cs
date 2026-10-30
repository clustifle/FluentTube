namespace FluentTube;

internal static class ShortsRanking
{
    internal static string Creator(Video video) => video.ChannelId ?? video.Channel;
    internal static double Feedback(double watched, double duration) => watched < 2 ? -1 :
        duration > 0 && watched / duration >= 0.7 ? 2 : watched >= 8 ? 1 : 0;

    internal static List<Video> Rank(IEnumerable<Video> candidates, IReadOnlyDictionary<string, double> affinity, IEnumerable<string> seen)
    {
        var viewed = seen.ToHashSet();
        var pool = candidates.Where(v => v.IsShort).DistinctBy(v => v.Id)
            .Select(v => (Video: v, Score: (viewed.Contains(v.Id) ? -20 : 0) +
                Math.Clamp(affinity.GetValueOrDefault(Creator(v)), -4, 4) + Random.Shared.NextDouble() * 3))
            .OrderByDescending(v => v.Score).Select(v => v.Video).ToList();
        var result = new List<Video>();
        while (pool.Count > 0)
        {
            var index = result.Count == 0 ? 0 : pool.FindIndex(v => Creator(v) != Creator(result[^1]));
            if (index < 0) index = 0;
            result.Add(pool[index]); pool.RemoveAt(index);
        }
        return result;
    }
}
