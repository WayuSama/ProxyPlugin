param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [string]$OutputPath = 'repo.json',
    [string]$Repository = 'WayuSama/ProxyPlugin'
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$version = $manifest.AssemblyVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Version must have four numeric parts' }
$download = "https://github.com/$Repository/releases/download/v$version/ProxyPlugin.zip"
$manifest | Add-Member RepoUrl "https://github.com/$Repository" -Force
foreach ($key in 'DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting') {
    $manifest | Add-Member $key $download -Force
}
$manifest | Add-Member TestingAssemblyVersion $version -Force
$manifest | Add-Member TestingDalamudApiLevel $manifest.DalamudApiLevel -Force
$manifest | Add-Member LastUpdate ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
ConvertTo-Json -InputObject @($manifest) -Depth 32 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
