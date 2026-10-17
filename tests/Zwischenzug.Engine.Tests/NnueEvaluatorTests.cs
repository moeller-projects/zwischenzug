using System.Buffers.Binary;
using System.Runtime.Intrinsics.X86;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Zwischenzug.Core;
using Zwischenzug.Engine;

namespace Zwischenzug.Engine.Tests;

[TestClass]
public sealed class NnueEvaluatorTests
{
    private const int FeatureCount = 768;
    private const int HiddenSize = 128;
    private const int PayloadSize = 197440;

    [TestMethod]
    public void PackagesBulletWeightsAndEvaluatesSideToMoveFeatures()
    {
        byte[] payload = new byte[PayloadSize];
        WriteWeight(payload, 12 * HiddenSize, 1);
        WriteWeight(payload, FeatureCount * HiddenSize, 1);
        WriteWeight(payload, (FeatureCount * HiddenSize + HiddenSize), short.MaxValue);
        string path = WriteModel(payload);
        try
        {
            var evaluator = new NnueEvaluator(path);
            Position white = Fen.Parse("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1");
            Position black = Fen.Parse("4k3/8/8/8/8/8/4P3/4K3 b - - 0 1");

            Assert.AreEqual(12, evaluator.Evaluate(white, 0));
            Assert.AreEqual(3, evaluator.Evaluate(black, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IncrementalAccumulatorMatchesFreshRecomputationAcrossSiblingMoves()
    {
        byte[] payload = new byte[PayloadSize];
        for (int feature = 0; feature < FeatureCount; feature++)
            WriteWeight(payload, feature * HiddenSize, (short)(feature % 7 - 3));
        WriteWeight(payload, FeatureCount * HiddenSize, 20);
        WriteWeight(payload, FeatureCount * HiddenSize + HiddenSize, 2048);
        WriteWeight(payload, FeatureCount * HiddenSize + HiddenSize + 128, -1024);
        string path = WriteModel(payload);
        try
        {
            var cached = new NnueEvaluator(path);
            Position position = Fen.Parse(Fen.StartPosition);
            int rootScore = cached.Evaluate(position, 0);

            position.MakeMove(Move.Parse(position, "e2e4"));
            int firstScore = cached.Evaluate(position, 1);
            Assert.AreEqual(new NnueEvaluator(path).Evaluate(position, 1), firstScore);
            position.UnmakeMove();

            position.MakeMove(Move.Parse(position, "g1f3"));
            int secondScore = cached.Evaluate(position, 1);
            Assert.AreEqual(new NnueEvaluator(path).Evaluate(position, 1), secondScore);
            position.UnmakeMove();

            Assert.AreEqual(rootScore, cached.Evaluate(position, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CacheRefreshesAcrossCapturesCastlingEnPassantAndPromotion()
    {
        byte[] payload = new byte[PayloadSize];
        for (int feature = 0; feature < FeatureCount; feature++)
            WriteWeight(payload, feature * HiddenSize, (short)(feature % 7 - 3));
        WriteWeight(payload, FeatureCount * HiddenSize, 20);
        WriteWeight(payload, FeatureCount * HiddenSize + HiddenSize, 2048);
        WriteWeight(payload, FeatureCount * HiddenSize + HiddenSize + 128, -1024);
        string path = WriteModel(payload);
        try
        {
            var cached = new NnueEvaluator(path);
            (string Fen, string Move)[] scenarios =
            [
                ("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1", "e4d5"),
                ("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1"),
                ("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e5d6"),
                ("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8q"),
            ];
            foreach ((string fen, string moveText) in scenarios)
            {
                Position position = Fen.Parse(fen);
                _ = cached.Evaluate(position, 0);
                position.MakeMove(Move.Parse(position, moveText));

                int actual = cached.Evaluate(position, 1);
                int expected = new NnueEvaluator(path).Evaluate(position, 1);
                Assert.AreEqual(expected, actual, moveText);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Avx2AndPortableOutputsMatchAcrossIncrementalTransitions()
    {
        if (!Avx2.IsSupported) Assert.Inconclusive("AVX2 is not supported on this processor.");

        byte[] payload = new byte[PayloadSize];
        for (int feature = 0; feature < FeatureCount; feature++)
            for (int hidden = 0; hidden < HiddenSize; hidden++)
                WriteWeight(payload, feature * HiddenSize + hidden, (short)((feature * 3 + hidden * 7) % 17 - 8));
        for (int hidden = 0; hidden < HiddenSize; hidden++)
            WriteWeight(payload, FeatureCount * HiddenSize + hidden, (short)(hidden % 9 - 4));
        for (int weight = 0; weight < HiddenSize * 2; weight++)
            WriteWeight(payload, FeatureCount * HiddenSize + HiddenSize + weight, (short)(weight % 13 - 6));
        string path = WriteModel(payload);
        try
        {
            var portable = new NnueEvaluator(path, useAvx2: false);
            var vectorized = new NnueEvaluator(path, useAvx2: true);
            Position position = Fen.Parse(Fen.StartPosition);
            Assert.AreEqual(portable.Evaluate(position, 0), vectorized.Evaluate(position, 0));

            position.MakeMove(Move.Parse(position, "e2e4"));
            Assert.AreEqual(portable.Evaluate(position, 1), vectorized.Evaluate(position, 1));
            position.UnmakeMove();

            position.MakeMove(Move.Parse(position, "g1f3"));
            Assert.AreEqual(portable.Evaluate(position, 1), vectorized.Evaluate(position, 1));
            position.UnmakeMove();
            Assert.AreEqual(portable.Evaluate(position, 0), vectorized.Evaluate(position, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [TestMethod]
    public void RejectsCorruptModelChecksumAndInvalidBulletPayloadLength()
    {
        byte[] payload = new byte[PayloadSize];
        string path = WriteModel(payload);
        try
        {
            byte[] model = File.ReadAllBytes(path);
            model[^1] ^= 1;
            File.WriteAllBytes(path, model);
            Assert.Throws<InvalidDataException>(() => new NnueEvaluator(path));

            string invalidBullet = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(invalidBullet, new byte[PayloadSize - 1]);
                Assert.Throws<InvalidDataException>(() => NnueEvaluator.PackBulletQuantizedNetwork(invalidBullet, path));
            }
            finally
            {
                File.Delete(invalidBullet);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteModel(byte[] payload)
    {
        string bulletPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string modelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(bulletPath, payload);
        try
        {
            NnueEvaluator.PackBulletQuantizedNetwork(bulletPath, modelPath);
            return modelPath;
        }
        finally
        {
            File.Delete(bulletPath);
        }
    }

    private static void WriteWeight(byte[] payload, int shortIndex, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(shortIndex * sizeof(short)), value);
}
