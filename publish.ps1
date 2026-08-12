param(
    [string]$Output = "dist",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$rid = "win-x64"
$publishArgs = @("-c", "Release", "-r", $rid, "--self-contained", ($SelfContained.ToString().ToLowerInvariant()))

Write-Host "Publishing CardShopModManager.Cli ..."
dotnet publish src/CardShopModManager.Cli/CardShopModManager.Cli.csproj @publishArgs -p:PublishSingleFile=true -o "$Output/cli"

Write-Host "Publishing CardShopModManager.App ..."
dotnet publish src/CardShopModManager.App/CardShopModManager.App.csproj @publishArgs -p:PublishSingleFile=true -o "$Output/app"

Write-Host ""
Write-Host "Publish complete. Output:"
Get-ChildItem -Recurse "$Output" -Include *.exe | ForEach-Object {
    Write-Host ("  {0}  ({1} MB)" -f $_.FullName, [math]::Round($_.Length / 1MB, 1))
}