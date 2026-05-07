// EN: Entry point. Runs every [MemoryDiagnoser]-annotated benchmark in the assembly.
// TR: Giriş noktası. Assembly'deki [MemoryDiagnoser]-anotasyonlu tüm benchmark'ları çalıştırır.

using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
