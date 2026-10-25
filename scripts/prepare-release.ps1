param(
    [string]$BuildDirectory = 'ProxyPlugin/bin/Release',
    [string]$OutputDirectory = 'dist'
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content (Join-Path $PSScriptRoot '../ProxyPlugin/ProxyPlugin.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Four numeric version parts required' }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$package = Join-Path $OutputDirectory 'ProxyPlugin.zip'
Copy-Item (Join-Path $BuildDirectory 'ProxyPlugin/latest.zip') $package -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open((Join-Path $PWD $package), [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('ProxyPlugin.json')
    if (-not $entry) { throw 'Package manifest missing' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ([version]$manifest.AssemblyVersion -ne [version]$version) { throw 'Build and source versions differ' }
    # Version serializers normalize 01 to 1. Preserve the requested public spelling.
    $manifest.AssemblyVersion = $version
    $json = $manifest | ConvertTo-Json -Depth 32
    $entry.Delete()
    $entry = $zip.CreateEntry('ProxyPlugin.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write($json) } finally { $writer.Dispose() }
    $json | Set-Content (Join-Path $OutputDirectory 'ProxyPlugin.json') -Encoding utf8NoBOM
    $names = @($zip.Entries.FullName)
    foreach ($required in 'ProxyPlugin.dll', 'ProxyPlugin.Core.dll', 'ProxyPlugin.json', 'ProxyPlugin.deps.json') {
        if ($required -notin $names) { throw "Package is missing $required" }
    }
    if ($names -match '0Harmony|MonoMod') { throw 'Package must use host Harmony' }
} finally { $zip.Dispose() }
& (Join-Path $PSScriptRoot 'make-repo.ps1') -ManifestPath (Join-Path $OutputDirectory 'ProxyPlugin.json') -OutputPath (Join-Path $OutputDirectory 'repo.json')
