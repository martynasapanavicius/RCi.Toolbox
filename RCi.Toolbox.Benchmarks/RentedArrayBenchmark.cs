using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using RCi.Toolbox.Collections;

/*

BenchmarkDotNet v0.15.8
   AMD Ryzen 9 7950X, 1 CPU, 32 logical and 16 physical cores
   .NET SDK 10.0.112
     [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
     DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
   
    | Method                    | ArraySize | Mean           | Error      | StdDev     | Median         | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
    |-------------------------- |---------- |---------------:|-----------:|-----------:|---------------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
    | AllocateHeap              | 0         |      3.5087 ns |  0.1279 ns |  0.3771 ns |      3.6914 ns |  1.01 |    0.16 |   0.0014 |        - |        - |      24 B |        1.00 |
    | AllocateStack             | 0         |      0.3911 ns |  0.0004 ns |  0.0003 ns |      0.3911 ns |  0.11 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 0         |      4.1680 ns |  0.0039 ns |  0.0036 ns |      4.1685 ns |  1.20 |    0.14 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 0         |      4.4910 ns |  0.0071 ns |  0.0066 ns |      4.4897 ns |  1.30 |    0.15 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 0         |      4.1475 ns |  0.0072 ns |  0.0067 ns |      4.1477 ns |  1.20 |    0.14 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 0         |      4.1691 ns |  0.0050 ns |  0.0047 ns |      4.1688 ns |  1.20 |    0.14 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 0         |      4.1750 ns |  0.0067 ns |  0.0063 ns |      4.1729 ns |  1.20 |    0.14 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 1         |      3.1062 ns |  0.0609 ns |  0.0540 ns |      3.1121 ns |  1.00 |    0.02 |   0.0019 |        - |        - |      32 B |        1.00 |
    | AllocateStack             | 1         |      0.5569 ns |  0.0016 ns |  0.0015 ns |      0.5565 ns |  0.18 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 1         |      6.9296 ns |  0.0051 ns |  0.0048 ns |      6.9306 ns |  2.23 |    0.04 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 1         |      7.1916 ns |  0.0116 ns |  0.0109 ns |      7.1925 ns |  2.32 |    0.04 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 1         |      6.9065 ns |  0.0201 ns |  0.0188 ns |      6.9082 ns |  2.22 |    0.04 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 1         |      6.7757 ns |  0.0240 ns |  0.0187 ns |      6.7721 ns |  2.18 |    0.04 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 1         |      6.8891 ns |  0.0127 ns |  0.0118 ns |      6.8876 ns |  2.22 |    0.04 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 10        |      4.0688 ns |  0.0620 ns |  0.0580 ns |      4.0758 ns |  1.00 |    0.02 |   0.0038 |        - |        - |      64 B |        1.00 |
    | AllocateStack             | 10        |      0.9492 ns |  0.0024 ns |  0.0021 ns |      0.9493 ns |  0.23 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 10        |      6.9937 ns |  0.0069 ns |  0.0057 ns |      6.9939 ns |  1.72 |    0.02 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 10        |      7.1998 ns |  0.0247 ns |  0.0219 ns |      7.1985 ns |  1.77 |    0.03 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 10        |      6.8879 ns |  0.0194 ns |  0.0172 ns |      6.8853 ns |  1.69 |    0.02 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 10        |      6.7972 ns |  0.0177 ns |  0.0166 ns |      6.8017 ns |  1.67 |    0.02 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 10        |      7.1106 ns |  0.0109 ns |  0.0102 ns |      7.1116 ns |  1.75 |    0.02 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 100       |      8.5566 ns |  0.0935 ns |  0.0874 ns |      8.5388 ns |  1.00 |    0.01 |   0.0253 |        - |        - |     424 B |        1.00 |
    | AllocateStack             | 100       |      4.8905 ns |  0.0042 ns |  0.0033 ns |      4.8900 ns |  0.57 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 100       |      6.9584 ns |  0.0086 ns |  0.0077 ns |      6.9579 ns |  0.81 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 100       |     10.9132 ns |  0.0060 ns |  0.0056 ns |     10.9129 ns |  1.28 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 100       |      6.8935 ns |  0.0159 ns |  0.0141 ns |      6.8922 ns |  0.81 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 100       |      6.7358 ns |  0.0082 ns |  0.0072 ns |      6.7349 ns |  0.79 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 100       |      6.9425 ns |  0.0065 ns |  0.0061 ns |      6.9436 ns |  0.81 |    0.01 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 200       |     13.9641 ns |  0.1214 ns |  0.1136 ns |     13.9343 ns |  1.00 |    0.01 |   0.0492 |        - |        - |     824 B |        1.00 |
    | AllocateStack             | 200       |     10.2147 ns |  0.0143 ns |  0.0134 ns |     10.2156 ns |  0.73 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 200       |      6.9404 ns |  0.0082 ns |  0.0068 ns |      6.9429 ns |  0.50 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 200       |      7.1805 ns |  0.0103 ns |  0.0096 ns |      7.1779 ns |  0.51 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 200       |      6.8998 ns |  0.0108 ns |  0.0101 ns |      6.8988 ns |  0.49 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 200       |      6.7580 ns |  0.0120 ns |  0.0101 ns |      6.7583 ns |  0.48 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 200       |      6.8910 ns |  0.0079 ns |  0.0074 ns |      6.8904 ns |  0.49 |    0.00 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 500       |     32.7445 ns |  0.4809 ns |  0.4499 ns |     32.8163 ns |  1.00 |    0.02 |   0.1209 |        - |        - |    2024 B |        1.00 |
    | AllocateStack             | 500       |     28.1724 ns |  0.0322 ns |  0.0269 ns |     28.1698 ns |  0.86 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 500       |      6.9332 ns |  0.0067 ns |  0.0056 ns |      6.9330 ns |  0.21 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 500       |      7.1947 ns |  0.0145 ns |  0.0136 ns |      7.1921 ns |  0.22 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 500       |      6.9412 ns |  0.0108 ns |  0.0096 ns |      6.9412 ns |  0.21 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 500       |      6.7442 ns |  0.0072 ns |  0.0064 ns |      6.7424 ns |  0.21 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 500       |      6.8860 ns |  0.0074 ns |  0.0061 ns |      6.8878 ns |  0.21 |    0.00 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 1000      |     61.1700 ns |  0.7029 ns |  0.5488 ns |     61.1750 ns |  1.00 |    0.01 |   0.2404 |        - |        - |    4024 B |        1.00 |
    | AllocateStack             | 1000      |     52.2851 ns |  0.1102 ns |  0.1031 ns |     52.2504 ns |  0.85 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 1000      |      6.9988 ns |  0.0073 ns |  0.0065 ns |      6.9992 ns |  0.11 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 1000      |      7.1722 ns |  0.0051 ns |  0.0045 ns |      7.1721 ns |  0.12 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 1000      |      6.9108 ns |  0.0130 ns |  0.0121 ns |      6.9144 ns |  0.11 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 1000      |      6.7456 ns |  0.0068 ns |  0.0056 ns |      6.7454 ns |  0.11 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 1000      |      6.9135 ns |  0.0081 ns |  0.0076 ns |      6.9138 ns |  0.11 |    0.00 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 10000     |    477.5275 ns |  7.3203 ns |  6.8474 ns |    479.1071 ns |  1.00 |    0.02 |   2.3861 |        - |        - |   40024 B |        1.00 |
    | AllocateStack             | 10000     |    541.5554 ns |  0.5155 ns |  0.4822 ns |    541.4807 ns |  1.13 |    0.02 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 10000     |      6.9452 ns |  0.0112 ns |  0.0099 ns |      6.9450 ns |  0.01 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 10000     |      7.1790 ns |  0.0153 ns |  0.0136 ns |      7.1818 ns |  0.02 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 10000     |      6.8639 ns |  0.0074 ns |  0.0062 ns |      6.8638 ns |  0.01 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 10000     |      6.8478 ns |  0.0115 ns |  0.0102 ns |      6.8478 ns |  0.01 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 10000     |      6.8868 ns |  0.0081 ns |  0.0072 ns |      6.8857 ns |  0.01 |    0.00 |        - |        - |        - |         - |        0.00 |
    |                           |           |                |            |            |                |       |         |          |          |          |           |             |
    | AllocateHeap              | 100000    | 10,898.8245 ns | 40.1593 ns | 37.5650 ns | 10,899.2102 ns | 1.000 |    0.00 | 124.9847 | 124.9847 | 124.9847 |  400066 B |        1.00 |
    | AllocateStack             | 100000    |  5,390.9221 ns |  9.1940 ns |  8.1502 ns |  5,388.2916 ns | 0.495 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 100000    |      7.0671 ns |  0.0108 ns |  0.0101 ns |      7.0644 ns | 0.001 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 100000    |      7.1489 ns |  0.0049 ns |  0.0043 ns |      7.1480 ns | 0.001 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 100000    |      6.8820 ns |  0.0078 ns |  0.0069 ns |      6.8821 ns | 0.001 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayReadOnlyStruct | 100000    |      6.7451 ns |  0.0105 ns |  0.0093 ns |      6.7468 ns | 0.001 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 100000    |      6.9325 ns |  0.0073 ns |  0.0065 ns |      6.9332 ns | 0.001 |    0.00 |        - |        - |        - |         - |        0.00 |

*/

namespace RCi.Toolbox.Benchmarks
{
    [MemoryDiagnoser]
    public unsafe class RentedArrayBenchmark
    {
        [Params(0, 1, 10, 100, 200, 500, 1_000, 10_000, 100_000)]
        public int ArraySize;

        [Benchmark(Baseline = true)]
        public int AllocateHeap()
        {
            var array = new int[ArraySize];
            var span = array.AsSpan();
            if (span.Length > 0)
            {
                span[0] = 42;
            }
            return span.Length;
        }

        [Benchmark]
        public int AllocateStack()
        {
            Span<int> span = stackalloc int[ArraySize];
            if (span.Length > 0)
            {
                span[0] = 42;
            }
            return span.Length;
        }

        [Benchmark]
        public int ArrayPool()
        {
            var array = ArrayPool<int>.Shared.Rent(ArraySize);
            try
            {
                var span = array.AsSpan(0, ArraySize);
                if (span.Length > 0)
                {
                    span[0] = 42;
                }
                return span.Length;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(array);
            }
        }

        [Benchmark]
        public int ArrayPoolScoped()
        {
            var length = 0;
            RentedCollectionsBenchmarks.RentArrayHelper.RentArrayScoped(
                ArraySize,
                ref length,
                static (Span<int> span, ref int state) =>
                {
                    if (span.Length > 0)
                    {
                        span[0] = 42;
                    }
                    state = span.Length;
                }
            );
            return length;
        }

        [Benchmark]
        public int RentedArray()
        {
            using var array = new RentedArray<int>(
                ArraySize,
                clearOnInit: false,
                clearOnReturn: false
            );
            var span = array.Span;
            if (span.Length > 0)
            {
                span[0] = 42;
            }
            return span.Length;
        }

        [Benchmark]
        public int RentedArrayReadOnlyStruct()
        {
            using var array = new RentedCollectionsBenchmarks.RentedArrayReadOnlyStruct<int>(
                ArraySize
            );
            var span = array.Span;
            if (span.Length > 0)
            {
                span[0] = 42;
            }
            return span.Length;
        }

        [Benchmark]
        public int RentedArrayMutableStruct()
        {
            using var array = new RentedCollectionsBenchmarks.RentedArrayMutableStruct<int>(
                ArraySize
            );
            var span = array.Span;
            if (span.Length > 0)
            {
                span[0] = 42;
            }
            return span.Length;
        }
    }

    public static class RentedCollectionsBenchmarks
    {
        public static class RentArrayHelper
        {
            // The required signature for a zero-allocation delegate (passing state by ref)
            public delegate void ActionDelegate<T, TState>(Span<T> span, ref TState state);

            public static void RentArrayScoped<T, TState>(
                int length,
                ref TState state,
                ActionDelegate<T, TState> action
            )
            {
                var array = ArrayPool<T>.Shared.Rent(length);
                try
                {
                    action(array.AsSpan(0, length), ref state);
                }
                finally
                {
                    ArrayPool<T>.Shared.Return(array);
                }
            }
        }

        public readonly struct RentedArrayReadOnlyStruct<T>(int length) : IDisposable
        {
            public readonly int Length = length;
            private readonly T[] _array = ArrayPool<T>.Shared.Rent(length);

            public Span<T> Span => _array.AsSpan(0, Length);

            public void Dispose() => ArrayPool<T>.Shared.Return(_array);
        }

        public struct RentedArrayMutableStruct<T>(int length) : IDisposable
        {
            public readonly int Length = length;
            private T[]? _array = ArrayPool<T>.Shared.Rent(length);

            public Span<T> Span => _array.AsSpan(0, Length);

            public void Dispose()
            {
                if (_array is null)
                {
                    return;
                }
                ArrayPool<T>.Shared.Return(_array);
                _array = null;
            }
        }
    }
}
