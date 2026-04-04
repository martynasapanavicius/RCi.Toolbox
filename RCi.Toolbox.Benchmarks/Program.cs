using BenchmarkDotNet.Running;

namespace RCi.Toolbox.Benchmarks
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<ArrayAllocationBenchmark>();
            BenchmarkRunner.Run<JobQueueBenchmark>();
        }
    }
}
