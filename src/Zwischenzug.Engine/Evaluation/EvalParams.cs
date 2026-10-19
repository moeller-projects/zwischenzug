namespace Zwischenzug.Engine;

/// <summary>Evaluation weights and tables; keep them fixed for the duration of a search.</summary>
public sealed class EvalParams
{
    public int[] MaterialValues { get; }
    public int[] PhaseWeights { get; }
    public int[] MiddleGamePst { get; }
    public int[] EndGamePst { get; }
    internal int MaxPhase { get; }

    /// <summary>Copies optional material, phase, and piece-square weights so a search sees stable parameters.</summary>
    public EvalParams(int[]? materialValues = null, int[]? phaseWeights = null, int[]? middleGamePst = null, int[]? endGamePst = null)
    {
        MaterialValues = materialValues is null ? [0, 100, 320, 330, 500, 900, 0] : ValidateWeights(materialValues);
        PhaseWeights = phaseWeights is null ? [0, 0, 1, 1, 2, 4, 0] : ValidateWeights(phaseWeights);
        MiddleGamePst = middleGamePst is null ? CreatePst(false) : ValidateTable(middleGamePst);
        EndGamePst = endGamePst is null ? CreatePst(true) : ValidateTable(endGamePst);
        MaxPhase = Math.Max(1, 16 * PhaseWeights[1] + 4 * PhaseWeights[2] + 4 * PhaseWeights[3] + 4 * PhaseWeights[4] + 2 * PhaseWeights[5] + 2 * PhaseWeights[6]);
    }

    /// <summary>Checks the six piece kinds plus unused zero entry and returns a defensive copy.</summary>
    private static int[] ValidateWeights(int[] values) => values.Length >= 7 ? (int[])values.Clone() : throw new ArgumentException("Evaluation arrays require seven entries.");
    /// <summary>Checks six 64-square tables and returns a defensive copy.</summary>
    private static int[] ValidateTable(int[] table) => table.Length >= 384 ? (int[])table.Clone() : throw new ArgumentException("Piece-square tables require 384 entries.");

    /// <summary>Builds simple center and advancement bonuses for all six piece kinds.</summary>
    private static int[] CreatePst(bool endgame)
    {
        var table = new int[384];
        for (int type = 1; type <= 6; type++)
            for (int square = 0; square < 64; square++)
            {
                int rank = square >> 3, file = square & 7;
                int center = 4 - Math.Abs(file - 3) - Math.Abs(rank - 3);
                table[(type - 1) * 64 + square] = type switch
                {
                    1 => endgame ? rank : rank * 2,
                    2 or 3 => center * (endgame ? 3 : 4),
                    4 => center * (endgame ? 1 : 2),
                    5 => center,
                    _ => 0
                };
            }
        return table;
    }
}
