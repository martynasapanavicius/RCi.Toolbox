using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

/*

BenchmarkDotNet v0.15.8
   AMD Ryzen 9 7950X 4.50GHz, 1 CPU, 32 logical and 16 physical cores
   .NET SDK 10.0.201
     [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
     DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4

    | Method      | NumberOfJobs | WorkloadSize | Mean           | Error       | StdDev      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
    |------------ |------------- |------------- |---------------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
    | ParallelFor | 10000        | 10           |       163.6 us |     1.34 us |     1.25 us |  1.00 |    0.01 | 0.4883 |    8087 B |        1.00 |
    | JobQueue    | 10000        | 10           |     2,627.8 us |    48.84 us |    47.97 us | 16.06 |    0.31 |      - |     600 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 10000        | 10000        |   127,087.4 us |   209.79 us |   175.19 us |  1.00 |    0.00 |      - |    8600 B |        1.00 |
    | JobQueue    | 10000        | 10000        |   123,874.1 us |   415.39 us |   368.23 us |  0.97 |    0.00 |      - |     600 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 100000       | 10           |     1,316.3 us |     3.70 us |     3.09 us |  1.00 |    0.00 |      - |    8123 B |        1.00 |
    | JobQueue    | 100000       | 10           |    20,759.2 us |   394.27 us |   404.89 us | 15.77 |    0.30 |      - |     600 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 100000       | 10000        | 1,230,003.6 us | 2,029.68 us | 1,694.87 us |  1.00 |    0.00 |      - |    9984 B |        1.00 |
    | JobQueue    | 100000       | 10000        | 1,229,103.8 us | 2,078.98 us | 1,944.68 us |  1.00 |    0.00 |      - |     600 B |        0.06 |

*/

namespace RCi.Toolbox.Benchmarks
{
    [MemoryDiagnoser]
    public class JobQueueBenchmark
    {
        private ParallelOptions? _parallelOptions;
        private JobQueue? _jobQueue;

        // Test a high number of jobs to stress the queue's lock
        [Params(10_000, 100_000)]
        public int NumberOfJobs { get; set; }

        // 10 = Tiny workload (exposes scheduling overhead)
        // 10_000 = Heavy workload (hides scheduling overhead, shows raw execution)
        [Params(10, 10_000)]
        public int WorkloadSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
            };

            _jobQueue = new JobQueue(
                new JobQueueParameters
                {
                    WorkerCount = _parallelOptions.MaxDegreeOfParallelism,
                    UseBackgroundThreads = true,
                }
            );
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _jobQueue?.Dispose();
            _jobQueue = null;
        }

        private static void DoWork(int workloadSize) => Thread.SpinWait(workloadSize);

        [Benchmark(Baseline = true)]
        public void ParallelFor()
        {
            var workloadSize = WorkloadSize;
            Parallel.For(0, NumberOfJobs, _parallelOptions!, _ => DoWork(workloadSize));
        }

        [Benchmark]
        public void JobQueue()
        {
            var numberOfJobs = NumberOfJobs;
            var workloadSize = WorkloadSize;
            var jobQueue = _jobQueue!;

            for (var i = 0; i < numberOfJobs; i++)
            {
                jobQueue.Post(_ => DoWork(workloadSize));
            }

            jobQueue.WaitForIdle();
        }
    }
}
