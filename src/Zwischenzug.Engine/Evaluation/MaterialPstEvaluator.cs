using Zwischenzug.Core;

namespace Zwischenzug.Engine;

public interface IEvaluator
{
    int Evaluate(in Position pos, int ply);
}

/// <summary>Material plus tapered middle/end-game piece-square evaluation.</summary>
public sealed class MaterialPstEvaluator : IEvaluator
{
    private const int MaxPhase = 24;
    private readonly EvalParams _parameters;

    public MaterialPstEvaluator(EvalParams? parameters = null) => _parameters = parameters ?? new EvalParams();

    public int Evaluate(in Position pos, int ply)
    {
        if (pos.HasInsufficientMaterial()) return 0;
        int middle = 0, end = 0, phase = 0;
        for (int square = 0; square < 64; square++)
        {
            int piece = pos.Board[square];
            if (piece == 0) continue;
            int type = piece > 6 ? piece - 6 : piece;
            int sign = piece > 6 ? -1 : 1;
            int tableSquare = sign > 0 ? square : square ^ 56;
            int index = (type - 1) * 64 + tableSquare;
            middle += sign * (_parameters.MaterialValues[type] + _parameters.MiddleGamePst[index]);
            end += sign * (_parameters.MaterialValues[type] + _parameters.EndGamePst[index]);
            phase += _parameters.PhaseWeights[type];
        }
        if (phase > MaxPhase) phase = MaxPhase;
        int score = (middle * phase + end * (MaxPhase - phase)) / MaxPhase;
        return pos.SideToMove == Position.White ? score : -score;
    }
}
