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
