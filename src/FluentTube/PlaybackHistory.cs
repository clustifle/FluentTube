using System.IO;
using System.Text.Json;
namespace FluentTube;
internal sealed class PlaybackHistory
{
    private readonly string path = Path.Combine(LibraryStore.Folder, "positions.json");
    private Dictionary<string, double> positions = [];
    public PlaybackHistory()
    {
        try { positions = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { }
    }
    public double Get(string id, double duration) => PlaybackMath.Resume(positions.GetValueOrDefault(id), duration);
    public void Save(string id, double position, double duration)
    {
        positions.Remove(id);
        var resume = PlaybackMath.Resume(position, duration);
        if (resume > 0) positions[id] = resume;
        while (positions.Count > 100) positions.Remove(positions.Keys.First());
        Directory.CreateDirectory(LibraryStore.Folder);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(positions));
        File.Move(path + ".tmp", path, true);
    }
}
