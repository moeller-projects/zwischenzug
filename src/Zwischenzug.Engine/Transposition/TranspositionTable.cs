using System.Runtime.InteropServices;
using Zwischenzug.Core;

namespace Zwischenzug.Engine;

public enum Bound : byte
{
    Exact,
    Lower,
    Upper
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct TranspositionEntry
{
    public TranspositionEntry(ulong key, int depth, int score, Bound bound, Move bestMove, int age)
    {
        Key = key;
        Depth = depth;
        Score = score;
        Bound = bound;
        BestMove = bestMove;
        Age = age;
    }

    public ulong Key { get; }
    public int Depth { get; }
    public int Score { get; }
    public Bound Bound { get; }
    public Move BestMove { get; }
    public int Age { get; }
}

/// <summary>A fixed-size, single-owner transposition table.</summary>
public sealed class TranspositionTable
{
    private const int SlotsPerBucket = 4;
    private const int SlotBytes = 32;
    private const int MateValue = 30000;
    private const int MateThreshold = MateValue - 1000;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Slot
    {
        public ulong Key;
        public int Depth;
        public int Score;
        public Move BestMove;
        public Bound Bound;
        public int Age;
        public byte Occupied;
    }

    private Slot[] _slots;
    private int _bucketMask;
    private int _generation = 1;
    private int _used;
    private long _probes;
    private long _hits;

    public TranspositionTable(int megabytes = 16)
    {
        ValidateMegabytes(megabytes);
        (_slots, _bucketMask) = Allocate(megabytes);
    }

    public long Probes => _probes;
    public long Hits => _hits;

    /// <summary>Approximate occupancy in per mille (0 through 1000).</summary>
    public int Hashfull
    {
        get
        {
            int value = (int)(((long)_used * 1000 + (_slots.Length / 2)) / _slots.Length);
            return Math.Clamp(value, 0, 1000);
        }
    }

    public void Resize(int megabytes)
    {
        ValidateMegabytes(megabytes);
        (_slots, _bucketMask) = Allocate(megabytes);
        _used = 0;
        _probes = 0;
        _hits = 0;
    }

    public void Clear()
    {
        Array.Clear(_slots);
        _used = 0;
    }

    public void NewGeneration()
    {
        if (_generation == int.MaxValue)
        {
            Clear();
            _generation = 1;
        }
        else
        {
            _generation++;
        }

        _probes = 0;
        _hits = 0;
    }

    public bool TryGet(ulong key, int ply, out TranspositionEntry entry)
    {
        _probes++;
        int offset = (int)(key & (uint)_bucketMask) * SlotsPerBucket;
        for (int i = 0; i < SlotsPerBucket; i++)
        {
            ref Slot slot = ref _slots[offset + i];
            if (slot.Occupied != 0 && slot.Key == key)
            {
                _hits++;
                entry = new TranspositionEntry(slot.Key, slot.Depth, UnnormalizeMate(slot.Score, ply), slot.Bound, slot.BestMove, slot.Age);
                return true;
            }
        }

        entry = default;
        return false;
    }

    public void Store(ulong key, int depth, int score, Bound bound, Move bestMove, int ply)
    {
        int offset = (int)(key & (uint)_bucketMask) * SlotsPerBucket;
        int replacement = -1;
        int weakestDepth = int.MaxValue;
        int weakestAge = int.MaxValue;

        for (int i = 0; i < SlotsPerBucket; i++)
        {
            ref Slot slot = ref _slots[offset + i];
            if (slot.Occupied == 0)
            {
                replacement = i;
                break;
            }

            if (slot.Key == key)
            {
                if (slot.Age == _generation && slot.Depth > depth) return;
                replacement = i;
                break;
            }

            int ageRank = slot.Age == _generation ? 1 : 0;
            if (ageRank < weakestAge || (ageRank == weakestAge && slot.Depth < weakestDepth))
            {
                weakestAge = ageRank;
                weakestDepth = slot.Depth;
                replacement = i;
            }

        }

        ref Slot target = ref _slots[offset + replacement];
        if (target.Occupied == 0) _used++;
        target.Key = key;
        target.Depth = depth;
        target.Score = NormalizeMate(score, ply);
        target.Bound = bound;
        target.BestMove = bestMove;
        target.Age = _generation;
        target.Occupied = 1;
    }

    private static int NormalizeMate(int score, int ply)
    {
        if (score > MateThreshold) return score + ply;
        if (score < -MateThreshold) return score - ply;
        return score;
    }

    private static int UnnormalizeMate(int score, int ply)
    {
        if (score > MateThreshold) return score - ply;
        if (score < -MateThreshold) return score + ply;
        return score;
    }

    private static (Slot[] Slots, int Mask) Allocate(int megabytes)
    {
        long capacity = (long)megabytes * 1024 * 1024 / (SlotBytes * SlotsPerBucket);
        int buckets = 1;
        while ((long)buckets << 1 <= capacity && buckets < 1 << 30) buckets <<= 1;
        return (new Slot[buckets * SlotsPerBucket], buckets - 1);
    }

    private static void ValidateMegabytes(int megabytes)
    {
        if (megabytes is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(megabytes), "Table size must be between 1 and 1024 megabytes.");
    }
}
