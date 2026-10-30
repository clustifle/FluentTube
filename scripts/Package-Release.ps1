param(
    [string]$PublishDirectory = 'artifacts/FluentTube-win-x64',
    [string]$OutputDirectory = 'artifacts/release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $root 'src/FluentTube/FluentTube.csproj')
$version = $project.Project.PropertyGroup.Version
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Release tag does not match the project version' }
$publish = (Resolve-Path (Join-Path $root $PublishDirectory)).Path
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($file in @('FluentTube.exe','FluentTube.pri','Assets/FluentTube.ico','Assets/Icon32.png','Tools/yt-dlp/yt-dlp.exe','Tools/yt-dlp/_internal/python314.dll','Tools/deno.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $file))) { throw "Missing package file: $file" }
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'docs/INSTALL.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'docs/PRIVACY.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'docs/THIRD-PARTY-NOTICES.md') -Destination $publish
$archive = Join-Path $output "FluentTube-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n")
Write-Output "Created $([IO.Path]::GetFileName($archive)) with SHA-256 checksum."
