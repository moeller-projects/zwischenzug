param(
    [string]$EngineA = "artifacts\engine-a\Zwischenzug.Cli.exe",
    [string]$EngineB = "artifacts\engine-b\Zwischenzug.Cli.exe",
    [string]$Fastchess = "fastchess.exe",
    [switch]$Sprt
)

$rounds = if ($Sprt) { 50000 } else { 5 }
$openings = Join-Path $PSScriptRoot "openings.epd"
$arguments = @(
    "-engine", "cmd=$EngineA", "name=zwischenzug-a",
    "-engine", "cmd=$EngineB", "name=zwischenzug-b",
    "-each", "proto=uci", "tc=10+0.1", "option.Hash=16",
    "-openings", "file=$openings", "format=epd", "order=sequential",
    "-games", "2", "-rounds", "$rounds", "-concurrency", "1", "-strict"
)
if ($Sprt) { $arguments += @("-sprt", "elo0=0", "elo1=5", "alpha=0.05", "beta=0.05") }
& $Fastchess @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
