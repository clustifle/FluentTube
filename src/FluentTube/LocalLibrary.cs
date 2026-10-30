using System.IO;
using System.Text.Json;
namespace FluentTube;
public sealed record ChannelSubscription(string Id, string Name)
{
    public string Url => "https://www.youtube.com/channel/" + Id;
    public override string ToString() => Name;
}
internal sealed class LocalLibrary
{
    public List<ChannelSubscription> Subscriptions { get; set; } = [];
    public List<Video> Watched { get; set; } = [];
    public List<Video> Feed { get; set; } = [];
    public static LocalLibrary Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<LocalLibrary>(File.ReadAllText(Path.Combine(LibraryStore.Folder, "library.json"))) ?? new();
            value.Subscriptions = (value.Subscriptions ?? []).Where(c => c is not null && YouTubeService.IsChannelId(c.Id)).DistinctBy(c => c.Id).ToList();
            value.Watched = (value.Watched ?? []).Where(v => v is not null).Take(200).ToList();
            value.Feed ??= [];
            return value;
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Record(Video video)
    {
        Watched.RemoveAll(v => v.Id == video.Id);
        Watched.Insert(0, video);
        if (Watched.Count > 200) Watched.RemoveRange(200, Watched.Count - 200);
    }
    public void Save()
    {
        Directory.CreateDirectory(LibraryStore.Folder);
        var path = Path.Combine(LibraryStore.Folder, "library.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
