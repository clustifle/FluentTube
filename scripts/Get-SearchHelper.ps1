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
$denoVersion = 'v2.9.8'
$denoHash = 'cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf'
$archive = Join-Path $destination 'deno.zip'
Invoke-WebRequest "https://github.com/denoland/deno/releases/download/$denoVersion/deno-x86_64-pc-windows-msvc.zip" -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $denoHash) { throw 'Deno checksum mismatch' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
Remove-Item -LiteralPath $archive
Invoke-WebRequest "https://raw.githubusercontent.com/denoland/deno/$denoVersion/LICENSE.md" -OutFile (Join-Path $destination 'deno-LICENSE.txt')
Write-Output "Verified Deno $denoVersion"
