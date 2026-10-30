using System.IO;
using System.Text.Json;
namespace FluentTube;
internal sealed class Preferences
{
    public string Theme { get; set; } = "System";
    public string Accent { get; set; } = "Blue";
    public bool AutoPlay { get; set; } = true;
    public bool Loop { get; set; }
    public bool CompactCards { get; set; }
    public bool StartWithSaved { get; set; }
    public bool PrepareOnHover { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public bool AutoPlayNext { get; set; }
    public bool ShortsAutoNext { get; set; } = true;
    public int QualityHeight { get; set; }
    public double Volume { get; set; } = 0.8;
    public double Speed { get; set; } = 1;
    public static Preferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(LibraryStore.Folder, "settings.json"))) ?? new();
            value.Volume = double.IsFinite(value.Volume) ? Math.Clamp(value.Volume, 0, 1) : 0.8;
            value.Speed = PlaybackMath.Speed(value.Speed);
            value.QualityHeight = value.QualityHeight == 0 || value.QualityHeight is >= 144 and <= 1080 ? value.QualityHeight : 0;
            return value;
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(LibraryStore.Folder);
        var path = Path.Combine(LibraryStore.Folder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
