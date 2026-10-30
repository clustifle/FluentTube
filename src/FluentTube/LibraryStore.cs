using System.IO;
using System.Text.Json;
namespace FluentTube;
internal static class LibraryStore
{
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTube");
    private static string FilePath(string name) { Directory.CreateDirectory(Folder); return Path.Combine(Folder, name); }
    internal static List<Video> LoadVideos()
    {
        try { return JsonSerializer.Deserialize<List<Video>>(File.ReadAllText(FilePath("saved.json"))) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }
    internal static void SaveVideos(List<Video> videos)
    {
        var path = FilePath("saved.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(videos));
        File.Move(path + ".tmp", path, true);
    }
}
