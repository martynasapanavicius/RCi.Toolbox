using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

/*

BenchmarkDotNet v0.15.8
   AMD Ryzen 9 7950X, 1 CPU, 32 logical and 16 physical cores
   .NET SDK 10.0.112
     [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
     DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

    | Method      | NumberOfJobs | WorkloadSize | Mean           | Error       | StdDev      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
    |------------ |------------- |------------- |---------------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
    | ParallelFor | 10000        | 10           |       163.1 us |     2.97 us |     2.63 us |  1.00 |    0.02 | 0.2441 |    7778 B |        1.00 |
    | JobQueue    | 10000        | 10           |     2,788.0 us |    55.28 us |    79.28 us | 17.10 |    0.55 |      - |     576 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 10000        | 10000        |   131,464.1 us | 1,244.36 us | 1,039.10 us |  1.00 |    0.01 |      - |    8456 B |        1.00 |
    | JobQueue    | 10000        | 10000        |   126,532.6 us | 1,532.72 us | 1,433.71 us |  0.96 |    0.01 |      - |     576 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 100000       | 10           |     1,387.4 us |    14.07 us |    12.47 us |  1.00 |    0.01 |      - |    7906 B |        1.00 |
    | JobQueue    | 100000       | 10           |    23,877.5 us |   468.75 us |   946.91 us | 17.21 |    0.69 |      - |     576 B |        0.07 |
    |             |              |              |                |             |             |       |         |        |           |             |
    | ParallelFor | 100000       | 10000        | 1,225,118.4 us | 3,060.59 us | 2,713.13 us |  1.00 |    0.00 |      - |   10240 B |        1.00 |
    | JobQueue    | 100000       | 10000        | 1,222,565.9 us | 5,440.44 us | 5,089.00 us |  1.00 |    0.00 |      - |     576 B |        0.06 |

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
