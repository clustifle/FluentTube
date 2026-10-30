using System.IO;
using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
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
    internal static async Task<string> LoadKey()
    {
        var path = FilePath("key.bin");
        if (!File.Exists(path)) return "";
        var bytes = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(path));
        var result = await new DataProtectionProvider().UnprotectAsync(bytes);
        return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, result);
    }
    internal static async Task SaveKey(string key)
    {
        var bytes = CryptographicBuffer.ConvertStringToBinary(key, BinaryStringEncoding.Utf8);
        var result = await new DataProtectionProvider("LOCAL=user").ProtectAsync(bytes);
        CryptographicBuffer.CopyToByteArray(result, out var encrypted);
        await File.WriteAllBytesAsync(FilePath("key.bin"), encrypted);
    }
}
