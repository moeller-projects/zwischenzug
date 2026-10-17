using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Zwischenzug.Core;

namespace Zwischenzug.Engine;

/// <summary>Chess768 evaluator with AVX2 and portable scalar feature-update backends.</summary>
/// <remarks>Retains mutable per-ply caches; do not share one instance across concurrent searches.</remarks>
public sealed class NnueEvaluator : IEvaluator
{
    private const int FeatureCount = 768;
    private const int HiddenSize = 128;
    private const int Qa = 255;
    private const int Qb = 64;
    private const int EvalScale = 400;
    private const int MaxSearchPly = 96;
    private const int AccumulatorSize = HiddenSize * 2;
    private const int HeaderSize = 72;
    private const int PayloadSize = (FeatureCount * HiddenSize + HiddenSize + AccumulatorSize + 1) * sizeof(short) + 62;
    private static ReadOnlySpan<byte> Magic => "ZNNUE768"u8;

    private readonly short[] _FeatureWeights = new short[FeatureCount * HiddenSize];
    private readonly short[] _FeatureBias = new short[HiddenSize];
    private readonly short[] _OutputWeights = new short[AccumulatorSize];
    private readonly short _OutputBias;
    private readonly int[] _Accumulators = new int[MaxSearchPly * AccumulatorSize];
    private readonly int[] _BoardSnapshots = new int[MaxSearchPly * 64];
    private readonly int[] _SideSnapshots = new int[MaxSearchPly];
    private readonly bool[] _CacheValid = new bool[MaxSearchPly];
    private readonly bool _UseAvx2;

    public NnueEvaluator(string modelPath) : this(modelPath, Avx2.IsSupported) { }

    internal NnueEvaluator(string modelPath, bool useAvx2)
    {
        if (useAvx2 && !Avx2.IsSupported) throw new PlatformNotSupportedException("AVX2 is not supported on this processor.");
        _UseAvx2 = useAvx2;
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        byte[] file = File.ReadAllBytes(modelPath);
        if (file.Length != HeaderSize + PayloadSize)
            throw new InvalidDataException("Invalid NNUE model length.");

        ReadOnlySpan<byte> header = file.AsSpan(0, HeaderSize);
        if (!header[..Magic.Length].SequenceEqual(Magic) ||
            ReadUInt32(header, 8) != 1 || ReadUInt32(header, 12) != HeaderSize ||
            ReadUInt32(header, 16) != FeatureCount || ReadUInt32(header, 20) != HiddenSize ||
            ReadUInt32(header, 24) != Qa || ReadUInt32(header, 28) != Qb ||
            ReadUInt32(header, 32) != EvalScale || ReadUInt32(header, 36) != PayloadSize)
            throw new InvalidDataException("Unsupported NNUE model format or architecture.");

        ReadOnlySpan<byte> payload = file.AsSpan(HeaderSize);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(payload, digest);
        if (!CryptographicOperations.FixedTimeEquals(digest, header[40..72]))
            throw new InvalidDataException("NNUE model checksum mismatch.");

        int offset = 0;
        ReadWeights(payload, ref offset, _FeatureWeights);
        ReadWeights(payload, ref offset, _FeatureBias);
        ReadWeights(payload, ref offset, _OutputWeights);
        _OutputBias = BinaryPrimitives.ReadInt16LittleEndian(payload[offset..]);
    }

    /// <summary>Wraps a pinned Bullet Chess768 quantised.bin in the versioned Zwischenzug model format.</summary>
    public static void PackBulletQuantizedNetwork(string bulletPath, string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bulletPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        byte[] payload = File.ReadAllBytes(bulletPath);
        if (payload.Length != PayloadSize)
            throw new InvalidDataException($"Expected a Bullet Chess768 quantised payload of {PayloadSize} bytes.");

        byte[] file = new byte[HeaderSize + PayloadSize];
        Span<byte> header = file.AsSpan(0, HeaderSize);
        Magic.CopyTo(header);
        WriteUInt32(header, 8, 1);
        WriteUInt32(header, 12, HeaderSize);
        WriteUInt32(header, 16, FeatureCount);
        WriteUInt32(header, 20, HiddenSize);
        WriteUInt32(header, 24, Qa);
        WriteUInt32(header, 28, Qb);
        WriteUInt32(header, 32, EvalScale);
        WriteUInt32(header, 36, PayloadSize);
        payload.CopyTo(file, HeaderSize);
        SHA256.HashData(file.AsSpan(HeaderSize), header[40..72]);
        File.WriteAllBytes(modelPath, file);
    }

    public int Evaluate(in Position pos, int ply)
    {
        if ((uint)ply >= MaxSearchPly) throw new ArgumentOutOfRangeException(nameof(ply));
        UpdateAccumulators(in pos, ply);

        int offset = ply * AccumulatorSize;
        long output = 0;
        for (int i = 0; i < HiddenSize; i++)
        {
            int stm = Math.Clamp(_Accumulators[offset + i], 0, Qa);
            int ntm = Math.Clamp(_Accumulators[offset + HiddenSize + i], 0, Qa);
            output += (long)stm * stm * _OutputWeights[i];
            output += (long)ntm * ntm * _OutputWeights[HiddenSize + i];
        }

        output = output / Qa + _OutputBias;
        output = output * EvalScale / (Qa * Qb);
        return (int)output;
    }

    private void UpdateAccumulators(in Position pos, int ply)
    {
        int snapshotOffset = ply * 64;
        int accumulatorOffset = ply * AccumulatorSize;
        if (!_CacheValid[ply] || _SideSnapshots[ply] != pos.SideToMove)
        {
            for (int i = 0; i < HiddenSize; i++)
            {
                int bias = _FeatureBias[i];
                _Accumulators[accumulatorOffset + i] = bias;
                _Accumulators[accumulatorOffset + HiddenSize + i] = bias;
            }
            for (int square = 0; square < 64; square++)
            {
                int piece = pos.Board[square];
                _BoardSnapshots[snapshotOffset + square] = piece;
                if (piece != Position.Empty) UpdatePiece(accumulatorOffset, pos.SideToMove, square, piece, 1);
            }
            _SideSnapshots[ply] = pos.SideToMove;
            _CacheValid[ply] = true;
            return;
        }

        for (int square = 0; square < 64; square++)
        {
            int snapshotIndex = snapshotOffset + square;
            int oldPiece = _BoardSnapshots[snapshotIndex];
            int newPiece = pos.Board[square];
            if (oldPiece == newPiece) continue;
            if (oldPiece != Position.Empty) UpdatePiece(accumulatorOffset, pos.SideToMove, square, oldPiece, -1);
            if (newPiece != Position.Empty) UpdatePiece(accumulatorOffset, pos.SideToMove, square, newPiece, 1);
            _BoardSnapshots[snapshotIndex] = newPiece;
        }
    }

    private void UpdatePiece(int accumulatorOffset, int sideToMove, int square, int piece, int direction)
    {
        int color = piece > 6 ? Position.Black : Position.White;
        int type = (piece - 1) % 6;
        int normalizedSquare = sideToMove == Position.Black ? square ^ 56 : square;
        int normalizedColor = sideToMove == Position.Black ? color ^ 1 : color;
        int stmFeature = 384 * normalizedColor + 64 * type + normalizedSquare;
        int ntmFeature = 384 * (normalizedColor ^ 1) + 64 * type + (normalizedSquare ^ 56);
        AddFeature(accumulatorOffset, stmFeature, direction);
        AddFeature(accumulatorOffset + HiddenSize, ntmFeature, direction);
    }

    private void AddFeature(int accumulatorOffset, int feature, int direction)
    {
        int featureOffset = feature * HiddenSize;
        Span<int> accumulator = _Accumulators.AsSpan(accumulatorOffset, HiddenSize);
        if (_UseAvx2)
        {
            Span<Vector128<short>> weights = MemoryMarshal.Cast<short, Vector128<short>>(_FeatureWeights.AsSpan(featureOffset, HiddenSize));
            Span<Vector256<int>> values = MemoryMarshal.Cast<int, Vector256<int>>(accumulator);
            for (int i = 0; i < weights.Length; i++)
            {
                Vector256<int> delta = Avx2.ConvertToVector256Int32(weights[i]);
                if (direction < 0) delta = Avx2.Subtract(Vector256<int>.Zero, delta);
                values[i] = Avx2.Add(values[i], delta);
            }
            return;
        }
        ReadOnlySpan<short> scalarWeights = _FeatureWeights.AsSpan(featureOffset, HiddenSize);
        for (int i = 0; i < HiddenSize; i++)
            accumulator[i] += direction * scalarWeights[i];
    }

    private static void ReadWeights(ReadOnlySpan<byte> payload, ref int offset, Span<short> destination)
    {
        for (int i = 0; i < destination.Length; i++, offset += sizeof(short))
            destination[i] = BinaryPrimitives.ReadInt16LittleEndian(payload[offset..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void WriteUInt32(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], (uint)value);
}
