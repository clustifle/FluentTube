# Keep one local application build. Source and %LOCALAPPDATA%\FluentTube are never cleanup targets.
$ErrorActionPreference = 'Stop'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (!$dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (!(Test-Path -LiteralPath $dotnet)) { throw 'Install the .NET 10 SDK before updating FluentTube.' }
$root = Split-Path $PSScriptRoot -Parent
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if ((Get-Item -LiteralPath $artifacts).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The artifacts folder must not be a link.' }
$prefix = $artifacts + [IO.Path]::DirectorySeparatorChar
$current = Join-Path $artifacts 'FluentTube-current'
$stage = Join-Path $artifacts ('.FluentTube-stage-' + [Guid]::NewGuid())
$previous = Join-Path $artifacts ('.FluentTube-previous-' + [Guid]::NewGuid())
function Assert-BuildPath([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    if (!$absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe build path: $absolute" }
    if (Test-Path -LiteralPath $absolute) {
        $item = Get-Item -LiteralPath $absolute
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Build path is a link: $absolute" }
        if ($item.PSIsContainer -and @(Get-ChildItem -LiteralPath $absolute -Recurse -Force -Attributes ReparsePoint).Count -gt 0) { throw "Build contains a linked path: $absolute" }
    }
}
Assert-BuildPath $current; Assert-BuildPath $stage; Assert-BuildPath $previous
$running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and $_.Name -in @('FluentTube.exe','yt-dlp.exe','deno.exe') })
if ($running.Count -gt 0) { throw 'Close FluentTube and its playback helpers before updating the current build.' }
Push-Location $root
try {
    if (!(Test-Path -LiteralPath (Join-Path $root 'src/FluentTube/Tools/yt-dlp/yt-dlp.exe')) -or !(Test-Path -LiteralPath (Join-Path $root 'src/FluentTube/Tools/deno.exe'))) { & "$PSScriptRoot/Get-SearchHelper.ps1" }
    & $dotnet run --project tests/FluentTube.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Application checks failed; the current build was kept.' }
    & $dotnet run --project tests/FluentTube.PlaybackChecks -c Release -- --pip-resize
    if ($LASTEXITCODE -ne 0) { throw 'Native resize checks failed; the current build was kept.' }
    & $dotnet publish src/FluentTube/FluentTube.csproj -c Release -p:Platform=x64 -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; the current build was kept.' }
    foreach ($file in @('FluentTube.exe','FluentTube.dll','FluentTube.pri','Assets/FluentTube.ico','Assets/Icon32.png','Tools/yt-dlp/yt-dlp.exe','Tools/yt-dlp/_internal/python314.dll','Tools/deno.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $file))) { throw "Missing build file: $file" }
    }
    Assert-BuildPath $stage; Assert-BuildPath $current; Assert-BuildPath $previous
    if (Test-Path -LiteralPath $current) { Move-Item -LiteralPath $current -Destination $previous }
    try { Move-Item -LiteralPath $stage -Destination $current }
    catch {
        if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $current }
        throw
    }
    & "$PSScriptRoot/Package-Release.ps1" -PublishDirectory 'artifacts/FluentTube-current'
    $oldBuilds = @(Get-ChildItem -LiteralPath $artifacts -Directory | Where-Object { $_.Name -like 'FluentTube-*' -and $_.FullName -ne $current })
    # Verify the entire deletion set before removing any previous version.
    foreach ($build in $oldBuilds) { Assert-BuildPath $build.FullName }
    Assert-BuildPath $previous
    foreach ($build in $oldBuilds) { Remove-Item -LiteralPath $build.FullName -Recurse -Force }
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/FluentTube/FluentTube.csproj')
    $archiveName = "FluentTube-$($project.Project.PropertyGroup.Version)-win-x64.zip"
    $release = Join-Path $artifacts 'release'
    $oldArchives = @(Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Name -like 'FluentTube-*.zip*' -and $_.Name -notin @($archiveName, "$archiveName.sha256") })
    foreach ($archive in $oldArchives) { Assert-BuildPath $archive.FullName }
    foreach ($archive in $oldArchives) { Remove-Item -LiteralPath $archive.FullName -Force }
    Write-Output "Current build: $current"
    Write-Output "Removed $($oldBuilds.Count) older build folders and $($oldArchives.Count) stale archive files."
}
finally {
    if (Test-Path -LiteralPath $stage) { Assert-BuildPath $stage; Remove-Item -LiteralPath $stage -Recurse -Force }
    Pop-Location
}
