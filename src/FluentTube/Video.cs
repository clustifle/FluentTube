namespace FluentTube;
public sealed record Video(string Id, string Title, string Channel, string? LocalPath = null,
    string? ChannelId = null, double? Duration = null, long? Views = null, DateTimeOffset? Published = null)
{
    public string Thumbnail => LocalPath is null ? $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg" : "";
    public string DurationText => Duration is > 0 ? (Duration >= 3600 ? TimeSpan.FromSeconds(Duration.Value).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(Duration.Value).ToString(@"m\:ss")) : "";
    public string Details => LocalPath is not null ? "Local video" : string.Join(" · ", new[]
    {
        Views is >= 1000000 ? $"{Views / 1000000d:0.#}M views" : Views is >= 1000 ? $"{Views / 1000d:0.#}K views" : Views is not null ? $"{Views} views" : "",
        Published?.ToLocalTime().ToString("d MMM yyyy") ?? ""
    }.Where(s => s.Length > 0));
}
