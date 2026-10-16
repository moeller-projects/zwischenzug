param(
    [string]$EngineA = "artifacts\engine-a\Zwischenzug.Cli.exe",
    [string]$EngineB = "artifacts\engine-b\Zwischenzug.Cli.exe",
    [string]$Cutechess = "cutechess-cli.exe"
)

& $Cutechess `
    -engine cmd=$EngineA name=zwischenzug-a proto=uci `
    -engine cmd=$EngineB name=zwischenzug-b proto=uci `
    -each tc=10+0.1 option.Hash=16 `
    -games 2 -rounds 5 -repeat -concurrency 1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
