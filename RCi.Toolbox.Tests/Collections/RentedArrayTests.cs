using System;
using System.Buffers;
using System.Collections;
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
            var array = new RentedArray<int>(_originalArray.Length, false, false);
            for (var i = 0; i < _originalArray.Length; i++)
            {
                array[i] = _originalArray[i];
            }
            return array;
        }

        [Test]
        public static void CtorEmpty()
        {
            using var empty = new RentedArray<int>(0, false, false);
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

            Assert.DoesNotThrow(actual.Dispose);
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
        public static void GetEnumerator_Exhaustive()
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
            using var actual = new RentedArray<object?>([null, null, null], true);
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
        public static void CountProperty()
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
            using var actual = original.ToRentedArray(false);
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
            using var actual = original.ToRentedArray(false);
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
            using var actual = original.ToRentedArray(false);
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
            using var actual = original.ToRentedArray(false);
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
        public static void ToRentedArray_NonEnumeratedCount()
        {
            var range = Enumerable.Range(10, 20);
            Assert.That(range.TryGetNonEnumeratedCount(out var count) && count == 20, Is.True);
            using var actual = range.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(Enumerable.Range(10, 20)), Is.True);
        }

        [Test]
        public static void ToRentedArray_List()
        {
            var original = new List<int>(_originalArray);
            using var actual = original.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_RentedArray()
        {
            using var source = _originalArray.ToRentedArray(false);
            using var actual = source.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_RentedList()
        {
            using var source = _originalArray.ToRentedList(false);
            using var actual = source.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_IReadOnlyCollection()
        {
            var original = new TestReadOnlyCollection<int>(_originalArray);
            using var actual = original.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_IReadOnlyList()
        {
            var original = new TestReadOnlyList<int>(_originalArray);
            using var actual = original.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_NonGenericICollection()
        {
            var original = new TestNonGenericCollection<int>(_originalArray);
            using var actual = original.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_ReadOnlySpan()
        {
            ReadOnlySpan<int> span = _originalArray.AsSpan();
            using var actual = span.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_Span()
        {
            Span<int> span = _originalArray.ToArray().AsSpan();
            using var actual = span.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_ReadOnlyMemory()
        {
            ReadOnlyMemory<int> memory = _originalArray.AsMemory();
            using var actual = memory.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedArray_Memory()
        {
            Memory<int> memory = _originalArray.ToArray().AsMemory();
            using var actual = memory.ToRentedArray(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void Ctor_ReadOnlySpan()
        {
            ReadOnlySpan<int> span = _originalArray.AsSpan();
            using var actual1 = new RentedArray<int>(span, false);
            Assert.That(actual1.SequenceEqual(_originalArray), Is.True);

            using var actual2 = new RentedArray<int>(span, ArrayPool<int>.Shared, false);
            Assert.That(actual2.SequenceEqual(_originalArray), Is.True);

            using var empty = new RentedArray<int>(ReadOnlySpan<int>.Empty, false);
            Assert.That(empty.Length, Is.EqualTo(0));
        }

        [Test]
        public static void Ctor_ReadOnlyMemory()
        {
            ReadOnlyMemory<int> memory = _originalArray.AsMemory();
            using var actual1 = new RentedArray<int>(memory, false);
            Assert.That(actual1.SequenceEqual(_originalArray), Is.True);

            using var actual2 = new RentedArray<int>(memory, ArrayPool<int>.Shared, false);
            Assert.That(actual2.SequenceEqual(_originalArray), Is.True);

            using var empty = new RentedArray<int>(ReadOnlyMemory<int>.Empty, false);
            Assert.That(empty.Length, Is.EqualTo(0));
        }

        private sealed class TestReadOnlyCollection<T>(IEnumerable<T> items)
            : IReadOnlyCollection<T>
        {
            private readonly List<T> _items = items.ToList();
            public int Count => _items.Count;

            public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TestReadOnlyList<T>(IEnumerable<T> items) : IReadOnlyList<T>
        {
            private readonly List<T> _items = items.ToList();
            public int Count => _items.Count;
            public T this[int index] => _items[index];

            public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TestNonGenericCollection<T>(IEnumerable<T> items)
            : ICollection,
                IEnumerable<T>
        {
            private readonly List<T> _items = items.ToList();
            public int Count => _items.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => this;

            public void CopyTo(Array array, int index) =>
                ((ICollection)_items).CopyTo(array, index);

            public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class ThrowingReadOnlyCollection : IReadOnlyCollection<int>
        {
            public int Count => 10;

            public IEnumerator<int> GetEnumerator()
            {
                yield return 1;
                throw new InvalidOperationException("enumeration error");
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class TrackingArrayPool<T> : ArrayPool<T>
        {
            public int RentCount { get; private set; }
            public int ReturnCount { get; private set; }

            public override T[] Rent(int minimumLength)
            {
                RentCount++;
                return new T[minimumLength];
            }

            public override void Return(T[] array, bool clearArray = false)
            {
                ReturnCount++;
            }
        }

        [Test]
        public static void Ctor_ExceptionDuringEnumeration_ReturnsToPool()
        {
            var pool = new TrackingArrayPool<int>();
            Assert.Throws<InvalidOperationException>(() =>
                _ = new RentedArray<int>(new ThrowingReadOnlyCollection(), pool, false)
            );
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
        }

        [Test]
        public static void UnsafeAccessUnderlyingArray()
        {
            using var actual = new RentedArray<int>(_originalArray.Length, false, false);
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
                ArrayPool<int>.Shared,
                false
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
                using (var actual = new RentedArray<int>(_originalArray.Length, false, false))
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

        [Test]
        public static void NullValidation()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>(10, null!, false, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>((IEnumerable<int>)null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>((IEnumerable<int>)null!, ArrayPool<int>.Shared, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>([1, 2, 3], null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>(ReadOnlySpan<int>.Empty, null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedArray<int>(ReadOnlyMemory<int>.Empty, null!, false)
            );

            IEnumerable<int> nullEnumerable = null!;
            Assert.Throws<ArgumentNullException>(() => nullEnumerable.ToRentedArray(false));
            Assert.Throws<ArgumentNullException>(() =>
                nullEnumerable.ToRentedArray(ArrayPool<int>.Shared, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                new[] { 1, 2, 3 }.ToRentedArray(null!, false)
            );
        }

        [Test]
        public static void Ctor_NegativeLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new RentedArray<int>(-1, false, false)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new RentedArray<int>(-1, ArrayPool<int>.Shared, false, false)
            );
        }
    }
}
