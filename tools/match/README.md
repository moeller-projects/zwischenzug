# Engine matches

Build two engine revisions into separate directories, then run either script from the repository root:

```powershell
.\tools\match\fastchess.ps1 -EngineA artifacts\old\Zwischenzug.Cli.exe -EngineB artifacts\new\Zwischenzug.Cli.exe
.\tools\match\cutechess-cli.ps1 -EngineA artifacts\old\Zwischenzug.Cli.exe -EngineB artifacts\new\Zwischenzug.Cli.exe
```

The fastchess script runs ten games (five paired rounds), one game at a time, with a 10+0.1-second clock. Strict mode exits nonzero on warnings; crashes, illegal moves and time losses are failures, not accepted outcomes.

For a statistically meaningful strength comparison, add `-Sprt` to the fastchess command. It permits up to 100,000 games and stops when the sequential test reaches a decision. Parameters: H0 = 0 Elo, H1 = 5 Elo, alpha = 0.05, beta = 0.05. A ten-game acceptance smoke is not strength evidence.

Download [fastchess](https://github.com/Disservin/fastchess/releases) or cutechess-cli separately and ensure it is on `PATH`, or pass its executable path. The checked acceptance run used fastchess v1.8.2-alpha.
