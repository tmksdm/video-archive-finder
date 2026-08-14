$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$packageVersion = "9.0.1"
$packageName = "ffmpeg-$packageVersion-essentials_build.zip"
$downloadUrl = "https://www.gyan.dev/ffmpeg/builds/packages/$packageName"
$expectedSha256 = "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9"

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))

$destinationRoot = Join-Path $repositoryRoot "external\ffmpeg"
$binDirectory = Join-Path $destinationRoot "bin"
$licensesDirectory = Join-Path $destinationRoot "licenses"

$temporaryRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ("VideoArchiveFinder-FFmpeg-" + [Guid]::NewGuid().ToString("N"))

$archivePath = Join-Path $temporaryRoot $packageName
$extractDirectory = Join-Path $temporaryRoot "extracted"

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

    Write-Host "Downloading FFmpeg $packageVersion..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    $actualSha256 = (
        Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()

    if ($actualSha256 -ne $expectedSha256) {
        throw "FFmpeg archive SHA-256 verification failed."
    }

    Write-Host "SHA-256 verified."

    Expand-Archive `
        -LiteralPath $archivePath `
        -DestinationPath $extractDirectory

    $ffmpegFile = Get-ChildItem `
        -LiteralPath $extractDirectory `
        -Recurse `
        -File `
        -Filter "ffmpeg.exe" |
        Select-Object -First 1

    $ffprobeFile = Get-ChildItem `
        -LiteralPath $extractDirectory `
        -Recurse `
        -File `
        -Filter "ffprobe.exe" |
        Select-Object -First 1

    if ($null -eq $ffmpegFile -or $null -eq $ffprobeFile) {
        throw "The archive does not contain ffmpeg.exe and ffprobe.exe."
    }

    New-Item -ItemType Directory -Path $binDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null

    Copy-Item `
        -LiteralPath $ffmpegFile.FullName `
        -Destination (Join-Path $binDirectory "ffmpeg.exe") `
        -Force

    Copy-Item `
        -LiteralPath $ffprobeFile.FullName `
        -Destination (Join-Path $binDirectory "ffprobe.exe") `
        -Force

    $packageRoot = $ffmpegFile.Directory.Parent.FullName

    foreach ($fileName in @("LICENSE", "README.txt")) {
        $sourceFile = Join-Path $packageRoot $fileName

        if (Test-Path -LiteralPath $sourceFile -PathType Leaf) {
            Copy-Item `
                -LiteralPath $sourceFile `
                -Destination $licensesDirectory `
                -Force
        }
    }

    @(
        "Package: $packageName"
        "Download: $downloadUrl"
        "SHA-256: $expectedSha256"
        "Build provider: https://www.gyan.dev/ffmpeg/builds/"
        "FFmpeg project: https://ffmpeg.org/"
        "Legal information: https://ffmpeg.org/legal.html"
    ) | Set-Content `
        -LiteralPath (Join-Path $licensesDirectory "SOURCE.txt") `
        -Encoding utf8

    Write-Host "FFmpeg tools prepared successfully."
    Write-Host "Destination: $destinationRoot"
}
finally {
    $systemTemporaryPath = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())

    $actualTemporaryPath = [System.IO.Path]::GetFullPath(
        $temporaryRoot)

    if (
        $actualTemporaryPath.StartsWith(
            $systemTemporaryPath,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $actualTemporaryPath)
    ) {
        Remove-Item `
            -LiteralPath $actualTemporaryPath `
            -Recurse `
            -Force
    }
}
