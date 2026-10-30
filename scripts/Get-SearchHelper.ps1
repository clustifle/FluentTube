$ErrorActionPreference = 'Stop'
$version = '2026.10.26'
$expected = 'f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d'
$destination = Join-Path $PSScriptRoot '../src/FluentTube/Tools'
New-Item -ItemType Directory -Force $destination | Out-Null
$executable = Join-Path $destination 'yt-dlp.exe'
if (!(Test-Path $executable) -or (Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest "https://github.com/yt-dlp/yt-dlp/releases/download/$version/yt-dlp.exe" -OutFile $executable
}
if ((Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'yt-dlp checksum mismatch' }
Invoke-WebRequest "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$version/LICENSE" -OutFile (Join-Path $destination 'yt-dlp-LICENSE.txt')
Invoke-WebRequest "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$version/THIRD_PARTY_LICENSES.txt" -OutFile (Join-Path $destination 'yt-dlp-THIRD_PARTY_LICENSES.txt')
Write-Output "Verified yt-dlp $version"
