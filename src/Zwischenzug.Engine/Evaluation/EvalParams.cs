namespace Zwischenzug.Engine;

/// <summary>Immutable evaluation weights and piece-square tables.</summary>
public sealed class EvalParams
{
    public int[] MaterialValues { get; }
    public int[] PhaseWeights { get; }
    public int[] MiddleGamePst { get; }
    public int[] EndGamePst { get; }

    public EvalParams(int[]? materialValues = null, int[]? phaseWeights = null, int[]? middleGamePst = null, int[]? endGamePst = null)
    {
        MaterialValues = materialValues is null ? [0, 100, 320, 330, 500, 900, 0] : ValidateWeights(materialValues);
        PhaseWeights = phaseWeights is null ? [0, 0, 1, 1, 2, 4, 0] : ValidateWeights(phaseWeights);
        MiddleGamePst = middleGamePst is null ? CreatePst(false) : ValidateTable(middleGamePst);
        EndGamePst = endGamePst is null ? CreatePst(true) : ValidateTable(endGamePst);
    }

    private static int[] ValidateWeights(int[] values) => values.Length >= 7 ? (int[])values.Clone() : throw new ArgumentException("Evaluation arrays require seven entries.");
    private static int[] ValidateTable(int[] table) => table.Length >= 384 ? (int[])table.Clone() : throw new ArgumentException("Piece-square tables require 384 entries.");

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
