$ErrorActionPreference = 'Stop'
$version = '2026.10.26'
$expected = 'fd51b212cf8380c7a52ee08c0d6be7f3c59b2f2c832eaf7b45acfae6eab57419'
$destination = Join-Path $PSScriptRoot '../src/FluentTube/Tools'
New-Item -ItemType Directory -Force $destination | Out-Null
$helperArchive = Join-Path $destination 'yt-dlp_win.zip'
Invoke-WebRequest "https://github.com/yt-dlp/yt-dlp/releases/download/$version/yt-dlp_win.zip" -OutFile $helperArchive
if ((Get-FileHash $helperArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'yt-dlp checksum mismatch' }
Expand-Archive -LiteralPath $helperArchive -DestinationPath (Join-Path $destination 'yt-dlp') -Force
Remove-Item -LiteralPath $helperArchive
Invoke-WebRequest "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$version/LICENSE" -OutFile (Join-Path $destination 'yt-dlp-LICENSE.txt')
Invoke-WebRequest "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$version/THIRD_PARTY_LICENSES.txt" -OutFile (Join-Path $destination 'yt-dlp-THIRD_PARTY_LICENSES.txt')
Write-Output "Verified directory-build yt-dlp $version"
$denoVersion = 'v2.9.8'
$denoHash = 'cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf'
$archive = Join-Path $destination 'deno.zip'
Invoke-WebRequest "https://github.com/denoland/deno/releases/download/$denoVersion/deno-x86_64-pc-windows-msvc.zip" -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $denoHash) { throw 'Deno checksum mismatch' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
Remove-Item -LiteralPath $archive
Invoke-WebRequest "https://raw.githubusercontent.com/denoland/deno/$denoVersion/LICENSE.md" -OutFile (Join-Path $destination 'deno-LICENSE.txt')
Write-Output "Verified Deno $denoVersion"
