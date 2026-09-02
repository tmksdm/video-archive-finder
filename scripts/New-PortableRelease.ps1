param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version = "1.0.0-rc.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))

$solutionPath = Join-Path $repositoryRoot "VideoArchiveFinder.sln"
$desktopProjectPath = Join-Path `
    $repositoryRoot `
    "src\VideoArchiveFinder.Desktop\VideoArchiveFinder.Desktop.csproj"

$ffmpegPath = Join-Path `
    $repositoryRoot `
    "external\ffmpeg\bin\ffmpeg.exe"

$ffprobePath = Join-Path `
    $repositoryRoot `
    "external\ffmpeg\bin\ffprobe.exe"

$outputRoot = Join-Path $repositoryRoot "artifacts\releases"
$packageName = "VideoArchiveFinder-$Version-win-x64"
$releaseDirectory = Join-Path $outputRoot $packageName
$archivePath = Join-Path $outputRoot "$packageName.zip"
$checksumPath = "$archivePath.sha256"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

if (
    (Test-Path -LiteralPath $releaseDirectory) -or
    (Test-Path -LiteralPath $archivePath) -or
    (Test-Path -LiteralPath $checksumPath)
) {
    throw "Release output already exists: $packageName"
}

if (
    -not (Test-Path -LiteralPath $ffmpegPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ffprobePath -PathType Leaf)
) {
    Write-Host "Bundled FFmpeg tools are missing. Preparing them..."
    & (Join-Path $PSScriptRoot "Prepare-FfmpegTools.ps1")
}

Write-Host "Restoring dependencies..."
Invoke-DotNet -Arguments @("restore", $solutionPath)

Write-Host "Building Release..."
Invoke-DotNet -Arguments @(
    "build",
    $solutionPath,
    "-c", "Release",
    "--no-restore",
    "-p:Version=$Version"
)

Write-Host "Running tests..."
Invoke-DotNet -Arguments @(
    "test",
    $solutionPath,
    "-c", "Release",
    "--no-build",
    "-p:Version=$Version"
)

New-Item -ItemType Directory -Path $releaseDirectory | Out-Null

Write-Host "Publishing portable application..."
Invoke-DotNet -Arguments @(
    "publish",
    $desktopProjectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishTrimmed=false",
    "-p:Version=$Version",
    "-o", $releaseDirectory
)

$rootEntries = @(
    Get-ChildItem -LiteralPath $releaseDirectory |
        Select-Object -ExpandProperty Name |
        Sort-Object
)

$expectedRootEntries = @("app", "VideoArchiveFinder.exe")

if (
    [string]::Join("|", $rootEntries) -ne
    [string]::Join("|", $expectedRootEntries)
) {
    throw (
        "Unexpected portable root contents: " +
        [string]::Join(", ", $rootEntries)
    )
}

$requiredFiles = @(
    "VideoArchiveFinder.exe",
    "app\tools\ffmpeg.exe",
    "app\tools\ffprobe.exe",
    "app\libvlc\libvlc.dll",
    "app\libvlc\libvlccore.dll",
    "app\licenses\ffmpeg\SOURCE.txt",
    "app\licenses\libvlc\LICENSE.txt",
    "app\licenses\virtualizing-wrap-panel\LICENSE.txt"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $releaseDirectory $relativePath

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
}

$pluginsDirectory = Join-Path $releaseDirectory "app\libvlc\plugins"

if (-not (Test-Path -LiteralPath $pluginsDirectory -PathType Container)) {
    throw "Required LibVLC plugins directory is missing."
}

Write-Host "Creating ZIP archive..."
Compress-Archive `
    -LiteralPath $releaseDirectory `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$archiveHash = (
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
).Hash.ToLowerInvariant()

"$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Release candidate created successfully."
Write-Host "Folder: $releaseDirectory"
Write-Host "Archive: $archivePath"
Write-Host "SHA-256: $archiveHash"
