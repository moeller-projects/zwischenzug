using Zwischenzug.Core;
using System.Numerics;

namespace Zwischenzug.Engine;

public interface IEvaluator
{
    /// <summary>Returns a score from the position's side-to-move perspective.</summary>
    int Evaluate(in Position pos, int ply);
}

public interface IWorkerEvaluatorFactory
{
    /// <summary>Creates independent mutable evaluator state for one parallel search worker.</summary>
    IEvaluator CreateWorkerEvaluator();
}

/// <summary>Material plus tapered middle/end-game piece-square evaluation.</summary>
public sealed class MaterialPstEvaluator : IEvaluator, IWorkerEvaluatorFactory
{
    private readonly EvalParams _Parameters;

    /// <summary>Creates a static evaluator using supplied weights or the built-in material and square tables.</summary>
    public MaterialPstEvaluator(EvalParams? parameters = null) => _Parameters = parameters ?? new EvalParams();

    IEvaluator IWorkerEvaluatorFactory.CreateWorkerEvaluator() =>
        new MaterialPstEvaluator(new EvalParams(
            _Parameters.MaterialValues,
            _Parameters.PhaseWeights,
            _Parameters.MiddleGamePst,
            _Parameters.EndGamePst));

    /// <summary>Scores material and piece placement from the side-to-move perspective.</summary>
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
            int material = sign * count * _Parameters.MaterialValues[type];
            middle += material; end += material;
            phase += count * _Parameters.PhaseWeights[type];
            while (pieces != 0)
            {
                int square = BitOperations.TrailingZeroCount(pieces); pieces &= pieces - 1;
                int tableSquare = sign > 0 ? square : square ^ 56;
                int index = (type - 1) * 64 + tableSquare;
                middle += sign * _Parameters.MiddleGamePst[index];
                end += sign * _Parameters.EndGamePst[index];
            }
        }
        int maxPhase = _Parameters.MaxPhase;
        phase = Math.Min(phase, maxPhase);
        int score = (middle * phase + end * (maxPhase - phase)) / maxPhase;
        return pos.SideToMove == Position.White ? score : -score;
    }
}
