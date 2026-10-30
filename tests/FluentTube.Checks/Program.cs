using FluentTube;
var cases = new (string Input, string? Expected)[]
{
    ("dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "dQw4w9WgXcQ"),
    ("https://youtu.be/dQw4w9WgXcQ?si=test", "dQw4w9WgXcQ"),
    ("https://m.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ"),
    ("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ", null),
    ("https://evil.example/watch?v=dQw4w9WgXcQ", null),
    ("javascript:alert(1)", null),
    ("https://youtube.com/watch?v=<script>", null),
    ("https://youtu.be/dQw4w9WgXcQ/extra", null),
    ("https://youtube.com/watch?v=short", null),
    ("relaxing music", null),
};
foreach (var (input, expected) in cases)
    if (YouTubeService.ParseVideoId(input) != expected) throw new Exception($"URL parsing failed: {input}");
Console.WriteLine($"Passed {cases.Length} video URL and untrusted-input checks.");
var parsed = YouTubeService.ParseSearchResults("""
{"entries":[null,{"id":"bad"},{"id":"dQw4w9WgXcQ","title":"A &amp; B","channel":"Creator"},{"id":"dQw4w9WgXcQ"},{"id":"jNQXAC9IVRw","uploader":"Uploader"}]}
""");
if (parsed.Count != 2 || parsed[0].Title != "A & B" || parsed[1].Channel != "Uploader") throw new Exception("Search metadata filtering failed.");
var hostileQuery = "music \" --exec calc.exe & echo test";
var start = YouTubeService.SearchProcess("yt-dlp.exe", hostileQuery);
if (start.UseShellExecute || !start.CreateNoWindow || start.ArgumentList.Last() != "ytsearch24:" + hostileQuery || start.ArgumentList[^2] != "--") throw new Exception("Search arguments must remain a single literal query.");
Console.WriteLine("Passed metadata filtering and literal process-argument checks.");
