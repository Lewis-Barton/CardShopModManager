param(
    [string]$Output = "dist",
    [bool]$SelfContained = $true,
    [string]$VersionOverride = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$rid = "win-x64"
$publishArgs = @("-c", "Release", "-r", $rid, "--self-contained", ($SelfContained.ToString().ToLowerInvariant()))
if (-not [string]::IsNullOrWhiteSpace($VersionOverride)) {
    $publishArgs += "-p:Version=$VersionOverride"
}

Write-Host "Publishing TCGCardShopSimModManager.Cli ..."
dotnet publish src/TCGCardShopSimModManager.Cli/TCGCardShopSimModManager.Cli.csproj @publishArgs -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$Output/cli"

Write-Host "Publishing TCGCardShopSimModManager.App ..."
dotnet publish src/TCGCardShopSimModManager.App/TCGCardShopSimModManager.App.csproj @publishArgs -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$Output/app"

Copy-Item LICENSE, PRIVACY.md, THIRD-PARTY-NOTICES.md -Destination $Output

Write-Host ""
Write-Host "Publish complete. Output:"
Get-ChildItem -Recurse "$Output" -Include *.exe | ForEach-Object {
    Write-Host ("  {0}  ({1} MB)" -f $_.FullName, [math]::Round($_.Length / 1MB, 1))
}
