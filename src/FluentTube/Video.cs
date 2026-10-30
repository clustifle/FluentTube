namespace FluentTube;
public sealed record Video(string Id, string Title, string Channel, string? LocalPath = null)
{
    public string Thumbnail => LocalPath is null ? $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg" : "";
}
