using System.IO.Compression;
using System.IO;
using System.Text;
namespace FluentTube;

internal sealed record TakeoutResult(List<ChannelSubscription> Channels, int InvalidRows, int DuplicateRows);
internal static class TakeoutImport
{
    private const int MaxBytes = 8 * 1024 * 1024;
    internal static async Task<TakeoutResult> Read(string path)
    {
        using var file = File.OpenRead(path);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Parse(await ReadText(file));
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(e => e.FullName.Replace('\\', '/').Split('/').Last().Equals("subscriptions.csv", StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0) throw new InvalidOperationException("This ZIP has no subscriptions.csv. In Google Takeout, select YouTube and YouTube Music → subscriptions and create a new export.");
        if (entries.Count > 20) throw new InvalidOperationException("This archive contains too many subscription files. Extract and choose one subscriptions.csv instead.");
        var channels = new Dictionary<string, ChannelSubscription>(StringComparer.Ordinal);
        var invalid = 0; var duplicates = 0;
        foreach (var entry in entries)
        {
            if (entry.Length > MaxBytes) throw new InvalidOperationException("The subscription file is too large. Choose a subscriptions.csv under 8 MB.");
            using var stream = entry.Open();
            var result = Parse(await ReadText(stream));
            invalid += result.InvalidRows; duplicates += result.DuplicateRows;
            foreach (var channel in result.Channels) if (!channels.TryAdd(channel.Id, channel)) duplicates++;
        }
        return new(channels.Values.ToList(), invalid, duplicates);
    }
    private static async Task<string> ReadText(Stream stream)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + count > MaxBytes) throw new InvalidOperationException("Choose a subscriptions.csv under 8 MB.");
            await memory.WriteAsync(buffer.AsMemory(0, count));
        }
        return new UTF8Encoding(false, true).GetString(memory.ToArray()).TrimStart('\uFEFF');
    }
    internal static TakeoutResult Parse(string csv)
    {
        if (csv.Length > MaxBytes) throw new InvalidOperationException("The subscription file is too large.");
        var rows = Rows(csv.TrimStart('\uFEFF')).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count == 0) throw new InvalidOperationException("This subscription file is empty.");
        static string Header(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var headers = rows[0].Select(Header).ToList();
        var idColumn = headers.IndexOf("channelid");
        var urlColumn = headers.IndexOf("channelurl");
        var titleColumn = headers.IndexOf("channeltitle");
        if (titleColumn < 0 || idColumn < 0 && urlColumn < 0) throw new InvalidOperationException("Choose YouTube's subscriptions.csv containing Channel Id, Channel Url, and Channel Title columns.");
        var channels = new Dictionary<string, ChannelSubscription>(StringComparer.Ordinal);
        var invalid = 0; var duplicates = 0;
        foreach (var row in rows.Skip(1))
        {
            string Cell(int index) => index >= 0 && index < row.Count ? row[index].Trim() : "";
            var id = Cell(idColumn);
            if (!YouTubeService.IsChannelId(id))
            {
                var url = YouTubeService.ParseChannelUrl(Cell(urlColumn));
                id = url?.Split('/').Last() ?? "";
            }
            if (!YouTubeService.IsChannelId(id)) { invalid++; continue; }
            var name = string.Join(" ", Cell(titleColumn).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length > 512) name = name[..512];
            if (!channels.TryAdd(id, new(id, name.Length == 0 ? id : name))) duplicates++;
        }
        return new(channels.Values.ToList(), invalid, duplicates);
    }
    private static IEnumerable<List<string>> Rows(string csv)
    {
        var row = new List<string>(); var cell = new StringBuilder(); var quoted = false; var closedQuote = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closedQuote = true; } }
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0 && !closedQuote) quoted = true;
            else if (c == ',' || c == '\r' || c == '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closedQuote = false;
                if (c != ',') { if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; yield return row; row = []; }
            }
            else if (closedQuote && !char.IsWhiteSpace(c) || c == '"') throw new InvalidOperationException("The subscription CSV has invalid quoting. Export it again or choose the original Takeout file.");
            else if (!closedQuote) cell.Append(c);
        }
        if (quoted) throw new InvalidOperationException("The subscription CSV is incomplete. Download or export it again.");
        if (cell.Length > 0 || row.Count > 0 || closedQuote) { row.Add(cell.ToString()); yield return row; }
    }
    internal static List<ChannelSubscription> Merge(IEnumerable<ChannelSubscription> existing, IEnumerable<ChannelSubscription> imported) =>
        existing.Concat(imported).DistinctBy(c => c.Id).ToList();
}
