using BenchmarkDotNet.Running;
using Zwischenzug.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ChessBenchmarks).Assembly).Run(args);
