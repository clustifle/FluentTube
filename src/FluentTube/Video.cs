namespace FluentTube;
public sealed record Video(string Id, string Title, string Channel)
{
    public string Thumbnail => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
}
