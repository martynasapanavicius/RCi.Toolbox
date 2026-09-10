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
    public static class RentedListTests
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

        private static RentedList<int> CreateTestRentedList()
        {
            var list = new RentedList<int>(_originalArray.Length, false);
            foreach (var item in _originalArray)
            {
                list.Add(item);
            }
            return list;
        }

        [Test]
        public static void Dispose()
        {
            var actual = CreateTestRentedList();
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
        public static void Capacity()
        {
            using var actual = CreateTestRentedList();
            Assert.That(actual.Capacity, Is.GreaterThanOrEqualTo(_originalArray.Length));
        }

        [Test]
        public static void Grow()
        {
            using var actual = new RentedList<int>(false);
            var capacity = actual.Capacity;
            const int MAX_ADDITIONS = 1000;
            for (var i = 0; i < MAX_ADDITIONS; i++)
            {
                Assert.That(actual.Count, Is.EqualTo(i));
                actual.Add(i);
                Assert.That(actual.Count, Is.EqualTo(i + 1));
                if (actual.Capacity != capacity)
                {
                    break;
                }
            }
            Assert.That(actual.Count, Is.LessThan(MAX_ADDITIONS));
        }

        [Test]
        public static void GetEnumerator_Exhaustive()
        {
            using var actual = CreateTestRentedList();
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
            using var actual = new RentedList<object?>([null, null, null], true);
            using var enumerator = actual.GetEnumerator();
            while (enumerator.MoveNext())
            {
                Assert.That(enumerator.Current, Is.Null);
            }
        }

        [Test]
        public static void Clear()
        {
            using var actual = CreateTestRentedList();
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
            actual.Clear();
            Assert.That(actual.Count, Is.EqualTo(0));
        }

        [Test]
        public static void Contains()
        {
            using var actual = CreateTestRentedList();
            Assert.That(actual.Contains(_originalArray[0]), Is.True);
            Assert.That(actual.Contains(int.MaxValue), Is.False);
        }

        [Test]
        public static void CopyTo()
        {
            using var actual = CreateTestRentedList();

            var copy = new int[actual.Count];
            actual.CopyTo(copy, 0);
            Assert.That(copy.SequenceEqual(_originalArray), Is.True);

            copy = new int[actual.Count + 5];
            actual.CopyTo(copy, 2);
            Assert.That(copy.SequenceEqual([0, 0, .. _originalArray, 0, 0, 0]), Is.True);
        }

        [Test]
        public static void CountProperty()
        {
            using var actual = CreateTestRentedList();
            Assert.That(actual.Count, Is.EqualTo(_originalArray.Length));
        }

        [Test]
        public static void IsReadOnly()
        {
            using var actual = CreateTestRentedList();
            Assert.That(actual.IsReadOnly, Is.False);
        }

        [Test]
        public static void IndexOf()
        {
            using var actual = CreateTestRentedList();
            var indexOf = actual.IndexOf(_originalArray[3]);
            Assert.That(indexOf, Is.EqualTo(3));
        }

        [Test]
        public static void Indexers()
        {
            using var actual = CreateTestRentedList();
            for (var i = 0; i < actual.Count; i++)
            {
                Assert.That(actual[i], Is.EqualTo(_originalArray[i]));

                actual[i] = _originalArray[i] * -1;
                Assert.That(actual[i], Is.EqualTo(_originalArray[i] * -1));
            }
        }

        [Test]
        public static void Add()
        {
            using var actual = new RentedList<int>(false);

            actual.Add(420);
            Assert.That(actual.SequenceEqual([420]), Is.True);

            actual.Add(1337);
            Assert.That(actual.SequenceEqual([420, 1337]), Is.True);

            actual.Add(69);
            Assert.That(actual.SequenceEqual([420, 1337, 69]), Is.True);
        }

        [Test]
        public static void Remove()
        {
            using var actual = new RentedList<int>(false);

            actual.Add(420);
            actual.Add(1337);
            actual.Add(69);
            Assert.That(actual.SequenceEqual([420, 1337, 69]), Is.True);

            Assert.That(actual.Remove(1337), Is.True);
            Assert.That(actual.SequenceEqual([420, 69]), Is.True);

            Assert.That(actual.Remove(420), Is.True);
            Assert.That(actual.SequenceEqual([69]), Is.True);

            Assert.That(actual.Remove(-1), Is.False);
            Assert.That(actual.SequenceEqual([69]), Is.True);

            Assert.That(actual.Remove(69), Is.True);
            Assert.That(actual.SequenceEqual([]), Is.True);
        }

        [Test]
        public static void Insert()
        {
            using var actual = new RentedList<int>(false);

            actual.Add(420);
            actual.Add(1337);
            actual.Add(69);
            Assert.That(actual.SequenceEqual([420, 1337, 69]), Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() => actual.Insert(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => actual.Insert(111, 0));

            actual.Insert(0, 111);
            Assert.That(actual.SequenceEqual([111, 420, 1337, 69]), Is.True);

            actual.Insert(1, 222);
            Assert.That(actual.SequenceEqual([111, 222, 420, 1337, 69]), Is.True);

            actual.Insert(4, 444);
            Assert.That(actual.SequenceEqual([111, 222, 420, 1337, 444, 69]), Is.True);

            actual.Insert(6, 666);
            Assert.That(actual.SequenceEqual([111, 222, 420, 1337, 444, 69, 666]), Is.True);
        }

        [Test]
        public static void RemoveAt()
        {
            using var actual = new RentedList<int>(false);

            actual.Add(420);
            actual.Add(1337);
            actual.Add(69);
            Assert.That(actual.SequenceEqual([420, 1337, 69]), Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() => actual.RemoveAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => actual.RemoveAt(111));

            actual.RemoveAt(0);
            Assert.That(actual.SequenceEqual([1337, 69]), Is.True);

            actual.RemoveAt(1);
            Assert.That(actual.SequenceEqual([1337]), Is.True);

            actual.RemoveAt(0);
            Assert.That(actual.SequenceEqual([]), Is.True);
        }

        [Test]
        public static void AddRange_ICollection()
        {
            using var actual = new RentedList<int>(false);

            actual.AddRange([11, 22, 33]);
            Assert.That(actual.SequenceEqual([11, 22, 33]), Is.True);

            actual.AddRange([6, 7, 8, 9]);
            Assert.That(actual.SequenceEqual([11, 22, 33, 6, 7, 8, 9]), Is.True);
        }

        [Test]
        public static void AddRange_ICollection_Grow()
        {
            using var actual = new RentedList<int>(false);
            var array = Enumerable.Range(0, 100).ToArray();

            actual.AddRange(array);
            Assert.That(actual.SequenceEqual(array), Is.True);
        }

        [Test]
        public static void AddRange_IEnumerable()
        {
            using var actual = new RentedList<int>(false);
            var array = Enumerable.Range(0, 100).ToArray();

            actual.AddRange(array.Where(x => x > -1));
            Assert.That(actual.SequenceEqual(array), Is.True);
        }

        [Test]
        public static void AddRange_ReadOnlySpan()
        {
            using var actual = new RentedList<int>(false);
            ReadOnlySpan<int> span = [11, 22, 33];
            actual.AddRange(span);
            Assert.That(actual.SequenceEqual([11, 22, 33]), Is.True);
        }

        [Test]
        public static void AddRange_ReadOnlyMemory()
        {
            using var actual = new RentedList<int>(false);
            ReadOnlyMemory<int> memory = new[] { 11, 22, 33 };
            actual.AddRange(memory);
            Assert.That(actual.SequenceEqual([11, 22, 33]), Is.True);
        }

        [Test]
        public static void AddRange_ReadOnlySpan_Overlap_Throws()
        {
            using var list = new RentedList<int>(false);
            list.AddRange([1, 2, 3]);
            var span = list.AsReadOnlySpanUnsafe();
            try
            {
                list.AddRange(span);
                Assert.Fail("Expected ArgumentException when adding overlapping span");
            }
            catch (ArgumentException)
            {
                // expected
            }
        }

        [Test]
        public static void AddRange_ReadOnlyMemory_Overlap_Throws()
        {
            using var list = new RentedList<int>(false);
            list.AddRange([1, 2, 3]);
            var memory = list.AsReadOnlyMemoryUnsafe();
            Assert.Throws<ArgumentException>(() => list.AddRange(memory));
        }

        [Test]
        public static void AddRange_Self()
        {
            using var list = new RentedList<int>(initCapacity: 4, false);
            list.AddRange([1, 2, 3]);
            list.AddRange(list);
            Assert.That(list.SequenceEqual([1, 2, 3, 1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_List()
        {
            using var list = new RentedList<int>(false);
            list.AddRange(new List<int> { 1, 2, 3 });
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_RentedArray()
        {
            using var list = new RentedList<int>(false);
            using var rentedArray = new[] { 1, 2, 3 }.ToRentedArray(false);
            list.AddRange(rentedArray);
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_RentedList()
        {
            using var list = new RentedList<int>(false);
            using var other = new[] { 1, 2, 3 }.ToRentedList(false);
            list.AddRange(other);
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_IReadOnlyCollection()
        {
            using var list = new RentedList<int>(false);
            list.AddRange(new TestReadOnlyCollection<int>([1, 2, 3]));
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_IReadOnlyList()
        {
            using var list = new RentedList<int>(false);
            list.AddRange(new TestReadOnlyList<int>([1, 2, 3]));
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void AddRange_NonGenericICollection()
        {
            using var list = new RentedList<int>(false);
            list.AddRange(new TestNonGenericCollection<int>([1, 2, 3]));
            Assert.That(list.SequenceEqual([1, 2, 3]), Is.True);
        }

        [Test]
        public static void ToRentedList_Array()
        {
            var original = _originalArray.ToArray();
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Length; i++)
            {
                original[i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedList_ImmutableArray()
        {
            var original = _originalArray.ToImmutableArray();
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Length; i++)
            {
                ImmutableCollectionsMarshal.AsArray(original)![i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedList_IList()
        {
            var original = _originalArray.ToList();
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray));
            for (var i = 0; i < original.Count; i++)
            {
                original[i] = 123;
            }
            Assert.That(actual.SequenceEqual(_originalArray));
        }

        [Test]
        public static void ToRentedList_IEnumerable()
        {
            var original = GetStream(_originalArray);
            using var actual = original.ToRentedList(false);
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
        public static void ToRentedList_NonEnumeratedCount()
        {
            var range = Enumerable.Range(10, 20);
            Assert.That(range.TryGetNonEnumeratedCount(out var count) && count == 20, Is.True);
            using var actual = range.ToRentedList(false);
            Assert.That(actual.SequenceEqual(Enumerable.Range(10, 20)), Is.True);
        }

        [Test]
        public static void ToRentedList_List()
        {
            var original = new List<int>(_originalArray);
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_RentedArray()
        {
            using var source = _originalArray.ToRentedArray(false);
            using var actual = source.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_RentedList()
        {
            using var source = _originalArray.ToRentedList(false);
            using var actual = source.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_IReadOnlyCollection()
        {
            var original = new TestReadOnlyCollection<int>(_originalArray);
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_IReadOnlyList()
        {
            var original = new TestReadOnlyList<int>(_originalArray);
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_NonGenericICollection()
        {
            var original = new TestNonGenericCollection<int>(_originalArray);
            using var actual = original.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_ReadOnlySpan()
        {
            ReadOnlySpan<int> span = _originalArray.AsSpan();
            using var actual = span.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_Span()
        {
            Span<int> span = _originalArray.ToArray().AsSpan();
            using var actual = span.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_ReadOnlyMemory()
        {
            ReadOnlyMemory<int> memory = _originalArray.AsMemory();
            using var actual = memory.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void ToRentedList_Memory()
        {
            Memory<int> memory = _originalArray.ToArray().AsMemory();
            using var actual = memory.ToRentedList(false);
            Assert.That(actual.SequenceEqual(_originalArray), Is.True);
        }

        [Test]
        public static void Ctor_ReadOnlySpan()
        {
            ReadOnlySpan<int> span = _originalArray.AsSpan();
            using var actual1 = new RentedList<int>(span, false);
            Assert.That(actual1.SequenceEqual(_originalArray), Is.True);

            using var actual2 = new RentedList<int>(span, ArrayPool<int>.Shared, false);
            Assert.That(actual2.SequenceEqual(_originalArray), Is.True);

            using var empty = new RentedList<int>(ReadOnlySpan<int>.Empty, false);
            Assert.That(empty.Count, Is.EqualTo(0));
        }

        [Test]
        public static void Ctor_ReadOnlyMemory()
        {
            ReadOnlyMemory<int> memory = _originalArray.AsMemory();
            using var actual1 = new RentedList<int>(memory, false);
            Assert.That(actual1.SequenceEqual(_originalArray), Is.True);

            using var actual2 = new RentedList<int>(memory, ArrayPool<int>.Shared, false);
            Assert.That(actual2.SequenceEqual(_originalArray), Is.True);

            using var empty = new RentedList<int>(ReadOnlyMemory<int>.Empty, false);
            Assert.That(empty.Count, Is.EqualTo(0));
        }

        [Test]
        public static void NullValidation()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new RentedList<int>(10, null!, false));
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>((ArrayPool<int>)null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>((IEnumerable<int>)null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>((IEnumerable<int>)null!, ArrayPool<int>.Shared, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>([1, 2, 3], null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>(ReadOnlySpan<int>.Empty, null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                _ = new RentedList<int>(ReadOnlyMemory<int>.Empty, null!, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                new RentedList<int>(false).AddRange((IEnumerable<int>)null!)
            );

            IEnumerable<int> nullEnumerable = null!;
            Assert.Throws<ArgumentNullException>(() => nullEnumerable.ToRentedList(false));
            Assert.Throws<ArgumentNullException>(() =>
                nullEnumerable.ToRentedList(ArrayPool<int>.Shared, false)
            );
            Assert.Throws<ArgumentNullException>(() =>
                new[] { 1, 2, 3 }.ToRentedList(null!, false)
            );
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
                _ = new RentedList<int>(new ThrowingReadOnlyCollection(), pool, false)
            );
            Assert.That(pool.RentCount, Is.EqualTo(1));
            Assert.That(pool.ReturnCount, Is.EqualTo(1));
        }

        [Test]
        public static void Ctor_NegativeCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RentedList<int>(-1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new RentedList<int>(-1, ArrayPool<int>.Shared, false)
            );
        }
    }
}
