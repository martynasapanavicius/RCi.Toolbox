using System;
using System.Buffers;
using BenchmarkDotNet.Attributes;
using RCi.Toolbox.Collections;

/*

BenchmarkDotNet v0.15.8
   AMD Ryzen 9 7950X 4.50GHz, 1 CPU, 32 logical and 16 physical cores
   .NET SDK 10.0.201
     [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
     DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4
   
    | Method                    | ArraySize | Mean          | Error      | StdDev     | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
    |-------------------------- |---------- |--------------:|-----------:|-----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
    | AllocateHeap              | 0         |     1.6447 ns |  0.0287 ns |  0.0268 ns |  1.00 |    0.02 |   0.0014 |        - |        - |      24 B |        1.00 |
    | AllocateStack             | 0         |     0.5285 ns |  0.0046 ns |  0.0041 ns |  0.32 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 0         |     4.0019 ns |  0.0138 ns |  0.0129 ns |  2.43 |    0.04 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 0         |     4.0628 ns |  0.0059 ns |  0.0053 ns |  2.47 |    0.04 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 0         |    26.9553 ns |  0.1727 ns |  0.1531 ns | 16.39 |    0.27 |   0.0024 |        - |        - |      40 B |        1.67 |
    | RentedArrayReadOnlyStruct | 0         |     3.5157 ns |  0.0010 ns |  0.0008 ns |  2.14 |    0.03 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 0         |     3.6818 ns |  0.0028 ns |  0.0025 ns |  2.24 |    0.04 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 1         |     1.8505 ns |  0.0082 ns |  0.0076 ns |  1.00 |    0.01 |   0.0019 |        - |        - |      32 B |        1.00 |
    | AllocateStack             | 1         |     0.8912 ns |  0.0019 ns |  0.0015 ns |  0.48 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 1         |     5.6009 ns |  0.0024 ns |  0.0020 ns |  3.03 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 1         |     5.9567 ns |  0.0073 ns |  0.0057 ns |  3.22 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 1         |    29.3648 ns |  0.1687 ns |  0.1578 ns | 15.87 |    0.10 |   0.0024 |        - |        - |      40 B |        1.25 |
    | RentedArrayReadOnlyStruct | 1         |     5.4033 ns |  0.0097 ns |  0.0091 ns |  2.92 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 1         |     5.5531 ns |  0.0049 ns |  0.0041 ns |  3.00 |    0.01 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 10        |     2.2121 ns |  0.0043 ns |  0.0036 ns |  1.00 |    0.00 |   0.0038 |        - |        - |      64 B |        1.00 |
    | AllocateStack             | 10        |     1.2513 ns |  0.0042 ns |  0.0039 ns |  0.57 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 10        |     5.6346 ns |  0.0164 ns |  0.0153 ns |  2.55 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 10        |     5.9877 ns |  0.0131 ns |  0.0122 ns |  2.71 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 10        |    29.1666 ns |  0.2143 ns |  0.2005 ns | 13.18 |    0.09 |   0.0024 |        - |        - |      40 B |        0.62 |
    | RentedArrayReadOnlyStruct | 10        |     5.4252 ns |  0.0145 ns |  0.0135 ns |  2.45 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 10        |     5.5848 ns |  0.0165 ns |  0.0146 ns |  2.52 |    0.01 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 100       |     6.4014 ns |  0.0520 ns |  0.0461 ns |  1.00 |    0.01 |   0.0253 |        - |        - |     424 B |        1.00 |
    | AllocateStack             | 100       |     5.2491 ns |  0.0040 ns |  0.0034 ns |  0.82 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 100       |     5.5896 ns |  0.0051 ns |  0.0043 ns |  0.87 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 100       |     5.9948 ns |  0.0041 ns |  0.0034 ns |  0.94 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 100       |    28.8898 ns |  0.1702 ns |  0.1592 ns |  4.51 |    0.04 |   0.0024 |        - |        - |      40 B |        0.09 |
    | RentedArrayReadOnlyStruct | 100       |     5.3812 ns |  0.0058 ns |  0.0049 ns |  0.84 |    0.01 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 100       |     5.5531 ns |  0.0199 ns |  0.0186 ns |  0.87 |    0.01 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 200       |    10.8747 ns |  0.0421 ns |  0.0352 ns |  1.00 |    0.00 |   0.0492 |        - |        - |     824 B |        1.00 |
    | AllocateStack             | 200       |    10.1837 ns |  0.0077 ns |  0.0072 ns |  0.94 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 200       |     5.6090 ns |  0.0093 ns |  0.0083 ns |  0.52 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 200       |     5.9866 ns |  0.0100 ns |  0.0093 ns |  0.55 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 200       |    29.1049 ns |  0.1926 ns |  0.1802 ns |  2.68 |    0.02 |   0.0024 |        - |        - |      40 B |        0.05 |
    | RentedArrayReadOnlyStruct | 200       |     5.4253 ns |  0.0154 ns |  0.0145 ns |  0.50 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 200       |     5.5941 ns |  0.0703 ns |  0.0657 ns |  0.51 |    0.01 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 500       |    25.4513 ns |  0.2338 ns |  0.1826 ns |  1.00 |    0.01 |   0.1210 |        - |        - |    2024 B |        1.00 |
    | AllocateStack             | 500       |    28.4755 ns |  0.0525 ns |  0.0491 ns |  1.12 |    0.01 |        - |        - |        - |         - |        0.00 |
    | ArrayPool                 | 500       |     5.6085 ns |  0.0045 ns |  0.0042 ns |  0.22 |    0.00 |        - |        - |        - |         - |        0.00 |
    | ArrayPoolScoped           | 500       |     5.9777 ns |  0.0066 ns |  0.0058 ns |  0.23 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArray               | 500       |    29.5508 ns |  0.1577 ns |  0.1475 ns |  1.16 |    0.01 |   0.0024 |        - |        - |      40 B |        0.02 |
    | RentedArrayReadOnlyStruct | 500       |     5.4065 ns |  0.0054 ns |  0.0048 ns |  0.21 |    0.00 |        - |        - |        - |         - |        0.00 |
    | RentedArrayMutableStruct  | 500       |     5.5980 ns |  0.0053 ns |  0.0044 ns |  0.22 |    0.00 |        - |        - |        - |         - |        0.00 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 1000      |    49.2399 ns |  0.5187 ns |  0.4852 ns |  1.00 |    0.01 |   0.2405 |        - |        - |    4024 B |       1.000 |
    | AllocateStack             | 1000      |    51.5785 ns |  0.3072 ns |  0.2723 ns |  1.05 |    0.01 |        - |        - |        - |         - |       0.000 |
    | ArrayPool                 | 1000      |     5.5949 ns |  0.0065 ns |  0.0057 ns |  0.11 |    0.00 |        - |        - |        - |         - |       0.000 |
    | ArrayPoolScoped           | 1000      |     5.9580 ns |  0.0060 ns |  0.0056 ns |  0.12 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArray               | 1000      |    29.2921 ns |  0.1686 ns |  0.1577 ns |  0.59 |    0.01 |   0.0024 |        - |        - |      40 B |       0.010 |
    | RentedArrayReadOnlyStruct | 1000      |     5.3936 ns |  0.0047 ns |  0.0042 ns |  0.11 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArrayMutableStruct  | 1000      |     5.5422 ns |  0.0160 ns |  0.0125 ns |  0.11 |    0.00 |        - |        - |        - |         - |       0.000 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 10000     |   392.8146 ns |  2.7327 ns |  2.5561 ns |  1.00 |    0.01 |   2.3866 |        - |        - |   40024 B |       1.000 |
    | AllocateStack             | 10000     |   536.0949 ns |  0.5711 ns |  0.5062 ns |  1.36 |    0.01 |        - |        - |        - |         - |       0.000 |
    | ArrayPool                 | 10000     |     5.6084 ns |  0.0051 ns |  0.0043 ns |  0.01 |    0.00 |        - |        - |        - |         - |       0.000 |
    | ArrayPoolScoped           | 10000     |     5.9502 ns |  0.0038 ns |  0.0036 ns |  0.02 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArray               | 10000     |    28.9446 ns |  0.1916 ns |  0.1792 ns |  0.07 |    0.00 |   0.0024 |        - |        - |      40 B |       0.001 |
    | RentedArrayReadOnlyStruct | 10000     |     5.4014 ns |  0.0033 ns |  0.0031 ns |  0.01 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArrayMutableStruct  | 10000     |     5.5203 ns |  0.0091 ns |  0.0085 ns |  0.01 |    0.00 |        - |        - |        - |         - |       0.000 |
    |                           |           |               |            |            |       |         |          |          |          |           |             |
    | AllocateHeap              | 100000    | 7,106.5186 ns | 29.4350 ns | 26.0934 ns | 1.000 |    0.01 | 124.9924 | 124.9924 | 124.9924 |  400066 B |       1.000 |
    | AllocateStack             | 100000    | 5,322.2507 ns |  5.0730 ns |  4.4971 ns | 0.749 |    0.00 |        - |        - |        - |         - |       0.000 |
    | ArrayPool                 | 100000    |     5.6915 ns |  0.0033 ns |  0.0030 ns | 0.001 |    0.00 |        - |        - |        - |         - |       0.000 |
    | ArrayPoolScoped           | 100000    |     6.1291 ns |  0.0077 ns |  0.0068 ns | 0.001 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArray               | 100000    |    28.7658 ns |  0.1238 ns |  0.1097 ns | 0.004 |    0.00 |   0.0024 |        - |        - |      40 B |       0.000 |
    | RentedArrayReadOnlyStruct | 100000    |     5.3272 ns |  0.0197 ns |  0.0184 ns | 0.001 |    0.00 |        - |        - |        - |         - |       0.000 |
    | RentedArrayMutableStruct  | 100000    |     5.4002 ns |  0.0085 ns |  0.0071 ns | 0.001 |    0.00 |        - |        - |        - |         - |       0.000 |

*/

namespace RCi.Toolbox.Benchmarks
{
    [MemoryDiagnoser]
    public unsafe class ArrayAllocationBenchmark
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
