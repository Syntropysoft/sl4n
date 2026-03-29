using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sl4n.Benchmarks.Sl4nLoggerBenchmark).Assembly).Run(args);
