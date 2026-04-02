using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RCi.Toolbox.Collections
{
    public static class RentedArray
    {
        public static RentedArray<T> UnsafeCreateFromExisting<T>(
            int length,
            T[] rented,
            ArrayPool<T> pool
        ) => RentedArray<T>.UnsafeCreateFromExisting(length, rented, pool);
    }

    public sealed class RentedArray<T> : IMemoryOwner<T>, IList<T>, IReadOnlyList<T>
    {
        public readonly int Length;
        private readonly ArrayPool<T> _pool;
        private T[] _array;

        private RentedArray(int length, ArrayPool<T> pool, T[] rented)
        {
            Length = length;
            _pool = pool;
            _array = rented;
        }

        public RentedArray(int length, ArrayPool<T> pool, bool clearArray = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
            _pool = pool;
            Length = length;
            if (length == 0)
            {
                _array = [];
            }
            else
            {
                _array = pool.Rent(length);
                if (clearArray)
                {
                    Array.Clear(_array, 0, length);
                }
            }
        }

        public RentedArray(int length, bool clearArray = true)
            : this(length, ArrayPool<T>.Shared, clearArray) { }

        public RentedArray(IEnumerable<T> items, ArrayPool<T> pool)
        {
            _pool = pool;
            switch (items)
            {
                case T[] array:
                    Length = array.Length;
                    if (array.Length == 0)
                    {
                        _array = [];
                    }
                    else
                    {
                        _array = pool.Rent(Length);
                        array.CopyTo(_array.AsSpan());
                    }
                    break;

                case ImmutableArray<T> immutableArray:
                    Length = immutableArray.Length;
                    if (immutableArray.Length == 0)
                    {
                        _array = [];
                    }
                    else
                    {
                        _array = pool.Rent(Length);
                        immutableArray.CopyTo(_array.AsSpan());
                    }
                    break;

                case ICollection<T> collection:
                    {
                        var length = collection.Count;
                        Length = length;
                        if (length == 0)
                        {
                            _array = [];
                        }
                        else
                        {
                            _array = pool.Rent(Length);
                            collection.CopyTo(_array, 0);
                        }
                    }
                    break;

                default:
                    {
                        using var rentedList = items.ToRentedList();
                        var length = rentedList.Count;
                        Length = length;
                        if (length == 0)
                        {
                            _array = [];
                        }
                        else
                        {
                            _array = pool.Rent(Length);
                            rentedList.CopyTo(_array, 0);
                        }
                    }
                    break;
            }
        }

        public RentedArray(IEnumerable<T> items)
            : this(items, ArrayPool<T>.Shared) { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RentedArray() => Dispose(false);

        private void Dispose(bool disposing)
        {
            if (_array is null)
            {
                return;
            }
            if (disposing && Length != 0)
            {
                var needsClearing = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
                _pool.Return(_array, needsClearing);
            }
            _array = null!;
        }

        public Memory<T> Memory => new(_array, 0, Length);

        public ReadOnlyMemory<T> ReadOnlyMemory => new(_array, 0, Length);

        public Span<T> Span => new(_array, 0, Length);

        public ReadOnlySpan<T> ReadOnlySpan => new(_array, 0, Length);

        #region // IEnumerable<T>

        public struct Enumerator : IEnumerator<T>
        {
            private readonly RentedArray<T> _array;
            private nint _index;

            internal Enumerator(RentedArray<T> array)
            {
                _array = array;
                _index = -1;
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                var index = _index + 1;
                var length = _array.Length;
                if ((nuint)index >= (nuint)length)
                {
                    _index = length;
                    return false;
                }
                _index = index;
                return true;
            }

            public T Current
            {
                get
                {
                    var index = _index;
                    var array = _array;
                    if ((nuint)index < (nuint)array.Length)
                    {
                        return array._array[index];
                    }
                    if (index < 0)
                    {
                        throw new InvalidOperationException("enumeration not started");
                    }
                    throw new InvalidOperationException("enumeration ended");
                }
            }

            object? IEnumerator.Current => Current;

            public void Reset() => _index = -1;
        }

        public Enumerator GetEnumerator() => new(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        #endregion

        #region // ICollection<T>

        public void Add(T item) => throw new InvalidOperationException("cannot add items to array");

        public void Clear()
        {
            var length = Length;
            if (length > 0)
            {
                Array.Clear(_array, 0, length);
            }
        }

        public bool Contains(T item) => Length != 0 && IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex) =>
            Array.Copy(_array, 0, array, arrayIndex, Length);

        public bool Remove(T item) =>
            throw new InvalidOperationException("cannot remove items from array");

        public int Count => Length;

        public bool IsReadOnly => false;

        #endregion

        #region // IList<T>

        public int IndexOf(T item) => Array.IndexOf(_array, item, 0, Length);

        public void Insert(int index, T item) =>
            throw new InvalidOperationException("cannot insert items to array");

        public void RemoveAt(int index) =>
            throw new InvalidOperationException("cannot remove items from array");

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return _array[index];
            }
            set
            {
                if ((uint)index >= (uint)Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                _array[index] = value;
            }
        }

        #endregion

        /// <summary>
        /// WARNING: DO NOT STORE EXPOSED UNDERLYING ARRAY REFERENCE!
        /// If the reference of the exposed underlying array will be stored somewhere,
        /// the <see cref="RentedArray{T}"/> will not know about this and will return rented array it to the pool.
        /// Note that underlying array's length might be larger.
        /// </summary>
        public void UnsafeAccessUnderlyingArray(Action<T[]> action) => action(_array);

        /// <summary>
        /// WARNING: use with caution!
        /// Creates <see cref="RentedArray{T}"/> using provided rented array and will start managing it
        /// (will return to provided <see cref="pool"/> on <see cref="Dispose"/>).
        /// </summary>
        public static RentedArray<T> UnsafeCreateFromExisting(
            int length,
            T[] rented,
            ArrayPool<T> pool
        )
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, rented.Length);
            return new RentedArray<T>(length, pool, rented);
        }

        /// <summary>
        /// WARNING: use with caution!
        /// This detaches rented array from the container (<see cref="RentedArray{T}"/>) and
        /// won't be returned to <see cref="ArrayPool{T}"/> on <see cref="Dispose"/>.
        /// User is now responsible to return the array to the pool.
        /// </summary>
        public (T[] Array, ArrayPool<T> Pool) UnsafeDetachUnderlyingArray()
        {
            var array = _array;
            _array = null!;
            return (array, _pool);
        }
    }

    public static class RentedArrayExtensions
    {
        extension<T>(IEnumerable<T> items)
        {
            public RentedArray<T> ToRentedArray(ArrayPool<T> pool) => new(items, pool);

            public RentedArray<T> ToRentedArray() => items.ToRentedArray(ArrayPool<T>.Shared);
        }
    }
}
