using Zwischenzug.Core;
using System.Numerics;

namespace Zwischenzug.Engine;

public interface IEvaluator
{
    int Evaluate(in Position pos, int ply);
}

/// <summary>Material plus tapered middle/end-game piece-square evaluation.</summary>
public sealed class MaterialPstEvaluator : IEvaluator
{
    private readonly EvalParams _parameters;

    public MaterialPstEvaluator(EvalParams? parameters = null) => _parameters = parameters ?? new EvalParams();

    public int Evaluate(in Position pos, int ply)
    {
        if (pos.HasInsufficientMaterial()) return 0;
        int middle = 0, end = 0, phase = 0;
        for (int piece = 1; piece <= 12; piece++)
        {
            ulong pieces = pos.Pieces[piece];
            if (pieces == 0) continue;
            int type = piece > 6 ? piece - 6 : piece;
            int sign = piece > 6 ? -1 : 1;
            int count = BitOperations.PopCount(pieces);
            int material = sign * count * _parameters.MaterialValues[type];
            middle += material; end += material;
            phase += count * _parameters.PhaseWeights[type];
            while (pieces != 0)
            {
                int square = BitOperations.TrailingZeroCount(pieces); pieces &= pieces - 1;
                int tableSquare = sign > 0 ? square : square ^ 56;
                int index = (type - 1) * 64 + tableSquare;
                middle += sign * _parameters.MiddleGamePst[index];
                end += sign * _parameters.EndGamePst[index];
            }
        }
        int maxPhase = _parameters.MaxPhase;
        phase = Math.Min(phase, maxPhase);
        int score = (middle * phase + end * (maxPhase - phase)) / maxPhase;
        return pos.SideToMove == Position.White ? score : -score;
    }
}
