using BenchmarkDotNet.Running;

namespace RCi.Toolbox.Benchmarks
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            _ = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
