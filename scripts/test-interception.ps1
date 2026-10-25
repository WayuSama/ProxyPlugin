$ErrorActionPreference = 'Stop'
dotnet run --project ProxyPlugin.Harness -c Release
if ($LASTEXITCODE -ne 0) { throw 'HTTP harness failed' }
$folder = 'ProxyPlugin.Harness/bin/Release/net10.0-windows'
dotnet run --project ProxyPlugin.LoadContextHarness -c Release -- $folder
if ($LASTEXITCODE -ne 0) { throw 'Isolated host harness failed' }
dotnet run --project ProxyPlugin.LoadContextHarness -c Release --no-build -- $folder --legacy-default
if ($LASTEXITCODE -ne 0) { throw 'Legacy warm-upgrade harness failed' }
