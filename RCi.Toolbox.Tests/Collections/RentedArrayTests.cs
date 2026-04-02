using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using RCi.Toolbox.Collections;

namespace RCi.Toolbox.Tests.Collections
{
    [Parallelizable(ParallelScope.All)]
    public static class RentedArrayTests
    {
        private static readonly int[] _originalArray = new Func<int[]>(() =>
        {
            var random = new Random(42);
            var array = new int[10];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = random.Next(-99, 100);
            }
            return array;
        })();

        private static RentedArray<int> CreateTestRentedArray()
        {
            var array = new RentedArray<int>(_originalArray.Length, false);
            for (var i = 0; i < _originalArray.Length; i++)
            {
                array[i] = _originalArray[i];
            }
            return array;
        }

        [Test]
        public static void CtorEmpty()
        {
            using var empty = new RentedArray<int>(0);
            Assert.That(empty.Length, Is.EqualTo(0));
        }

        [Test]
        public static void Dispose()
        {
            var actual = CreateTestRentedArray();
            try
            {
                Assert.DoesNotThrow(() => actual[0] = 222);
                Assert.That(actual[0], Is.EqualTo(222));
            }
            finally
            {
                actual.Dispose();
            }
            Assert.Throws<NullReferenceException>(() => actual[0] = 333);

            Assert.DoesNotThrow(() => actual.Dispose());
        }

        [Test]
        public static void Length()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.Length, Is.EqualTo(_originalArray.Length));
        }

        [Test]
        public static void Memory()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.Memory.Span.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ReadOnlyMemory()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.ReadOnlyMemory.Span.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void Span()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.Span.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ReadOnlySpan()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.ReadOnlySpan.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void GetEnumerator()
        {
            using var actual = CreateTestRentedArray();
            using var enumerator = actual.GetEnumerator();
            var c = 0;
            while (enumerator.MoveNext())
            {
                Assert.That(enumerator.Current, Is.EqualTo(_originalArray[c]));
                c++;
            }
            Assert.That(c, Is.EqualTo(_originalArray.Length));
        }

        [Test]
        public static void GetEnumerator_CanHandleNulls()
        {
            using var actual = new RentedArray<object?>([null, null, null]);
            using var enumerator = actual.GetEnumerator();
            while (enumerator.MoveNext())
            {
                Assert.That(enumerator.Current, Is.Null);
            }
        }

        [Test]
        public static void Clear()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
            actual.Clear();
            Assert.That(actual.All(x => x == 0), Is.True);
        }

        [Test]
        public static void Contains()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.Contains(_originalArray[0]), Is.True);
            Assert.That(actual.Contains(int.MaxValue), Is.False);
        }

        [Test]
        public static void CopyTo()
        {
            using var actual = CreateTestRentedArray();

            var copy = new int[actual.Length];
            actual.CopyTo(copy, 0);
            Assert.That(copy.SequenceEqual(_originalArray), Is.True);

            copy = new int[actual.Length + 5];
            actual.CopyTo(copy, 2);
            Assert.That(copy.SequenceEqual([0, 0, .. _originalArray, 0, 0, 0]), Is.True);
        }

        [Test]
        public static void Count()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.Count, Is.EqualTo(_originalArray.Length));
        }

        [Test]
        public static void IsReadOnly()
        {
            using var actual = CreateTestRentedArray();
            Assert.That(actual.IsReadOnly, Is.False);
        }

        [Test]
        public static void IndexOf()
        {
            using var actual = CreateTestRentedArray();
            var indexOf = actual.IndexOf(_originalArray[3]);
            Assert.That(indexOf, Is.EqualTo(3));
        }

        [Test]
        public static void Indexers()
        {
            using var actual = CreateTestRentedArray();
            for (var i = 0; i < actual.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(_originalArray[i]));

                actual[i] = _originalArray[i] * -1;
                Assert.That(actual[i], Is.EqualTo(_originalArray[i] * -1));
            }
        }

        [Test]
        public static void ToRentedArray_Array()
        {
            var original = _originalArray.ToArray();
            using var actual = original.ToRentedArray();
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Length; i++)
            {
                original[i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedArray_ImmutableArray()
        {
            var original = _originalArray.ToImmutableArray();
            using var actual = original.ToRentedArray();
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Length; i++)
            {
                ImmutableCollectionsMarshal.AsArray(original)![i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedArray_IList()
        {
            var original = _originalArray.ToList();
            using var actual = original.ToRentedArray();
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Count; i++)
            {
                original[i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedArray_IEnumerable()
        {
            var original = GetStream(_originalArray);
            using var actual = original.ToRentedArray();
            Assert.That(actual.SequenceEqual(_originalArray));
            return;

            static IEnumerable<T> GetStream<T>(IEnumerable<T> items)
            {
                foreach (var item in items)
                {
                    yield return item;
                }
            }
        }

        [Test]
        public static void UnsafeAccessUnderlyingArray()
        {
            using var actual = new RentedArray<int>(_originalArray.Length, false);
            actual.UnsafeAccessUnderlyingArray(x => _originalArray.CopyTo(x, 0));
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void UnsafeCreateFromExisting()
        {
            const int length = 1337;
            var manuallyRentedArray = ArrayPool<int>.Shared.Rent(length);

            using var actual = RentedArray.UnsafeCreateFromExisting(
                length,
                manuallyRentedArray,
                ArrayPool<int>.Shared
            );
            Assert.That(
                manuallyRentedArray.AsSpan()[..length].ToArray(),
                Is.EqualTo(actual.Span.ToArray())
            );
            actual.UnsafeAccessUnderlyingArray(x =>
            {
                Assert.That(ReferenceEquals(manuallyRentedArray, x), Is.True);
            });
        }

        [Test]
        public static void UnsafeDetachUnderlyingArray()
        {
            int[]? underlying = null;
            ArrayPool<int>? pool = null;
            try
            {
                using (var actual = new RentedArray<int>(_originalArray.Length))
                {
                    // extract underlying array
                    (underlying, pool) = actual.UnsafeDetachUnderlyingArray();

                    // rented array no longer has underlying array reference and should throw
                    Assert.Throws<NullReferenceException>(() => actual[0] = 1);
                }

                // all good, we extracted and now own physical array
                underlying[0] = 1;
            }
            finally
            {
                if (underlying is not null && pool is not null)
                {
                    pool.Return(underlying);
                }
            }
        }
    }
}
