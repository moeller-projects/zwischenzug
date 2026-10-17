[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repoRoot "src/Zwischenzug.Cli/Zwischenzug.Cli.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK/runtime was not found on PATH. Install it before starting Zwischenzug."
}

& dotnet run --project $project --configuration $Configuration --no-build --no-restore -- uci
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
