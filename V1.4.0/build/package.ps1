<#
    Builds a Jellyfin-installable ZIP for Jellyfin Startup Ads and refreshes manifest.json
    with the real MD5 checksum and file size.

    Usage:  pwsh ./build/package.ps1 [-Version 1.4.1.0]
#>
param(
    [string]$Version = "1.4.1.0"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo "Jellyfin.Plugin.StartupAds/Jellyfin.Plugin.StartupAds.csproj"
$artifacts = Join-Path $repo "artifacts"
$stage = Join-Path $artifacts "stage"
$tag = "v" + ($Version -replace '\.\d+$','')          # 1.4.1.0 -> v1.4.1
$zipName = "jellyfin-startup-ads_$Version.zip"
$zipPath = Join-Path $artifacts $zipName

Write-Host "==> Publishing $proj"
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

dotnet publish $proj -c Release -o $stage --nologo -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Jellyfin only needs the plugin assembly + meta.json in the plugin folder.
Get-ChildItem $stage -Exclude "Jellyfin.Plugin.StartupAds.dll" | Remove-Item -Recurse -Force

# meta.json is copied verbatim (its 'timestamp' is the fixed release timestamp) so the
# resulting ZIP - and therefore its checksum - is reproducible.
$metaRaw = Get-Content (Join-Path $repo "build/meta.json") -Raw
$meta = $metaRaw | ConvertFrom-Json
[System.IO.File]::WriteAllText((Join-Path $stage "meta.json"), $metaRaw, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "==> Creating $zipPath"
# Build the ZIP with fixed entry timestamps so the checksum is reproducible.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$fixedDate = [DateTimeOffset]::Parse($meta.timestamp)
$fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($name in @("Jellyfin.Plugin.StartupAds.dll", "meta.json")) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = $fixedDate
    $es = $entry.Open()
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $stage $name))
    $es.Write($bytes, 0, $bytes.Length)
    $es.Dispose()
}
$zip.Dispose()
$fs.Dispose()

$md5 = (Get-FileHash $zipPath -Algorithm MD5).Hash.ToLower()
$size = (Get-Item $zipPath).Length

$summary = [ordered]@{
    zip         = $zipName
    version     = $Version
    targetAbi   = "10.11.0.0"
    md5         = $md5
    sizeBytes   = $size
    timestamp   = $meta.timestamp
    sourceUrl   = "https://github.com/franciscocolon22/Jellyfin-Startup-Ads/releases/download/$tag/$zipName"
}
($summary | ConvertTo-Json) | Set-Content (Join-Path $artifacts "release-info.json") -Encoding ascii

Write-Host "==> Done."
Write-Host "    ZIP:   $zipPath"
Write-Host "    MD5:   $md5"
Write-Host "    size:  $size bytes"
Write-Host ""
Write-Host "Update manifest.json 'checksum' with the MD5 above, then:"
Write-Host "  1. git tag $tag && git push origin $tag"
Write-Host "  2. create GitHub release '$tag', upload '$zipName'"
Write-Host "  3. commit manifest.json"
