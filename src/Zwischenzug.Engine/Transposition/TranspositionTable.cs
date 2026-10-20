using System.Runtime.InteropServices;
using Zwischenzug.Core;

namespace Zwischenzug.Engine;

public enum Bound : byte
{
    /// <summary>The stored score is exact for the searched depth.</summary>
    Exact,
    /// <summary>The true score is at least the stored score.</summary>
    Lower,
    /// <summary>The true score is at most the stored score.</summary>
    Upper
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct TranspositionEntry
{
    /// <summary>Creates an immutable snapshot of one matching table entry.</summary>
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

/// <summary>Fixed-size transposition table with stripe-locked bucket operations.</summary>
/// <remarks>Call clear, resize, and generation operations only when all searchers are idle.</remarks>
public sealed class TranspositionTable
{
    private const int SlotsPerBucket = 4;
    private const int SlotBytes = 32;
    private const int MateValue = 30000;
    private const int MateThreshold = MateValue - 1000;
    private const int LockCount = 1024;

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

    private Slot[] _Slots;
    private int _BucketMask;
    private int _Generation = 1;
    private int _Used;
    private long _Probes;
    private long _Hits;
    private readonly object[] _Locks = CreateLocks();

    /// <summary>Allocates a four-way table with the requested memory budget.</summary>
    public TranspositionTable(int megabytes = 16)
    {
        ValidateMegabytes(megabytes);
        (_Slots, _BucketMask) = Allocate(megabytes);
    }

    public long Probes => Interlocked.Read(ref _Probes);
    public long Hits => Interlocked.Read(ref _Hits);

    /// <summary>Approximate occupancy in per mille (0 through 1000).</summary>
    public int Hashfull
    {
        get
        {
            int value = (int)(((long)Volatile.Read(ref _Used) * 1000 + (_Slots.Length / 2)) / _Slots.Length);
            return Math.Clamp(value, 0, 1000);
        }
    }

    /// <summary>Reallocates the table and resets occupancy and statistics; callers must ensure workers are idle.</summary>
    public void Resize(int megabytes)
    {
        ValidateMegabytes(megabytes);
        (_Slots, _BucketMask) = Allocate(megabytes);
        Interlocked.Exchange(ref _Used, 0);
        Interlocked.Exchange(ref _Probes, 0);
        Interlocked.Exchange(ref _Hits, 0);
    }

    /// <summary>Invalidates every entry while retaining the allocated storage.</summary>
    public void Clear()
    {
        Array.Clear(_Slots);
        Interlocked.Exchange(ref _Used, 0);
    }

    /// <summary>Marks a new root search so old entries are preferred for replacement.</summary>
    public void NewGeneration()
    {
        if (_Generation == int.MaxValue)
        {
            Clear();
            _Generation = 1;
        }
        else
        {
            _Generation++;
        }

        Interlocked.Exchange(ref _Probes, 0);
        Interlocked.Exchange(ref _Hits, 0);
    }

    /// <summary>Looks up a full position key and converts any stored mate score to this search ply.</summary>
    public bool TryGet(ulong key, int ply, out TranspositionEntry entry)
    {
        Interlocked.Increment(ref _Probes);
        int bucket = (int)(key & (uint)_BucketMask);
        lock (_Locks[bucket & (LockCount - 1)])
        {
            int offset = bucket * SlotsPerBucket;
            for (int i = 0; i < SlotsPerBucket; i++)
            {
                ref Slot slot = ref _Slots[offset + i];
                if (slot.Occupied != 0 && slot.Key == key)
                {
                    Interlocked.Increment(ref _Hits);
                    entry = new TranspositionEntry(slot.Key, slot.Depth, UnnormalizeMate(slot.Score, ply), slot.Bound, slot.BestMove, slot.Age);
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }

    /// <summary>Stores a bound and best move, replacing a weaker or older entry in the key's four-slot bucket.</summary>
    public void Store(ulong key, int depth, int score, Bound bound, Move bestMove, int ply)
    {
        int bucket = (int)(key & (uint)_BucketMask);
        lock (_Locks[bucket & (LockCount - 1)])
        {
            int offset = bucket * SlotsPerBucket;
            int replacement = -1;
            int weakestDepth = int.MaxValue;
            int weakestAge = int.MaxValue;

            for (int i = 0; i < SlotsPerBucket; i++)
            {
                ref Slot slot = ref _Slots[offset + i];
                if (slot.Occupied == 0)
                {
                    replacement = i;
                    break;
                }

                if (slot.Key == key)
                {
                    if (slot.Age == _Generation && slot.Depth > depth) return;
                    replacement = i;
                    break;
                }

                // Prefer replacing old-generation entries first, then the shallowest entry in that age group.
                int ageRank = slot.Age == _Generation ? 1 : 0;
                if (ageRank < weakestAge || (ageRank == weakestAge && slot.Depth < weakestDepth))
                {
                    weakestAge = ageRank;
                    weakestDepth = slot.Depth;
                    replacement = i;
                }
            }

            ref Slot target = ref _Slots[offset + replacement];
            if (target.Occupied == 0) Interlocked.Increment(ref _Used);
            target.Key = key;
            target.Depth = depth;
            target.Score = NormalizeMate(score, ply);
            target.Bound = bound;
            target.BestMove = bestMove;
            target.Age = _Generation;
            target.Occupied = 1;
        }
    }

    /// <summary>Stores mate distance relative to the root so the same position can be reused at another ply.</summary>
    private static int NormalizeMate(int score, int ply)
    {
        if (score > MateThreshold) return score + ply;
        if (score < -MateThreshold) return score - ply;
        return score;
    }

    /// <summary>Restores mate distance from root-relative storage to the probing node's ply.</summary>
    private static int UnnormalizeMate(int score, int ply)
    {
        if (score > MateThreshold) return score - ply;
        if (score < -MateThreshold) return score + ply;
        return score;
    }

    /// <summary>Creates stripe locks; unrelated buckets may share a lock to keep synchronization storage small.</summary>
    private static object[] CreateLocks()
    {
        var locks = new object[LockCount];
        for (int i = 0; i < locks.Length; i++) locks[i] = new object();
        return locks;
    }

    /// <summary>Allocates a power-of-two number of four-entry buckets within the requested memory budget.</summary>
    private static (Slot[] Slots, int Mask) Allocate(int megabytes)
    {
        long capacity = (long)megabytes * 1024 * 1024 / (SlotBytes * SlotsPerBucket);
        int buckets = 1;
        while ((long)buckets << 1 <= capacity && buckets < 1 << 30) buckets <<= 1;
        return (new Slot[buckets * SlotsPerBucket], buckets - 1);
    }

    /// <summary>Enforces the public table-size range before allocation.</summary>
    private static void ValidateMegabytes(int megabytes)
    {
        if (megabytes is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(megabytes), "Table size must be between 1 and 1024 megabytes.");
    }
}
