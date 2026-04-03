using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace RCi.Toolbox.Collections
{
    /// <summary>
    /// Provides static factory methods for <see cref="RentedArray{T}"/>.
    /// </summary>
    public static class RentedArray
    {
        /// <inheritdoc cref="RentedArray{T}.UnsafeCreateFromExisting"/>
        public static RentedArray<T> UnsafeCreateFromExisting<T>(
            int length,
            T[] rented,
            ArrayPool<T> pool,
            bool clearOnReturn
        ) => RentedArray<T>.UnsafeCreateFromExisting(length, rented, pool, clearOnReturn);
    }

    /// <summary>
    /// A high-performance, fixed-size collection backed by <see cref="ArrayPool{T}"/>.
    ///
    /// <br/><strong>WARNING:</strong> Turn on <strong>clearOnReturn</strong> when dealing with rented arrays which hold reference types!
    /// It zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
    /// If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
    /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
    /// </summary>
    public sealed class RentedArray<T> : IMemoryOwner<T>, IList<T>, IReadOnlyList<T>
    {
        /// <summary>
        /// Gets the logical number of elements contained in the <see cref="RentedArray{T}"/>.
        /// </summary>
        public readonly int Length;

        private readonly ArrayPool<T> _pool;
        private readonly bool _clearOnReturn;
        private T[] _array;

        private RentedArray(int length, ArrayPool<T> pool, T[] rented, bool clearOnReturn)
        {
            _pool = pool;
            _clearOnReturn = clearOnReturn;
            Length = length;
            _array = rented;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedArray{T}"/> class with the specified length, using a specific array pool.
        /// </summary>
        /// <param name="length">The exact number of elements the array will logically contain.</param>
        /// <param name="pool">The pool to rent the underlying array from.</param>
        /// <param name="clearOnInit">If <c>true</c>, zeroes out the rented memory to prevent reading previous pool state.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="length"/> is less than 0.</exception>
        public RentedArray(int length, ArrayPool<T> pool, bool clearOnInit, bool clearOnReturn)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
            _pool = pool;
            _clearOnReturn = clearOnReturn;
            Length = length;
            if (length == 0)
            {
                _array = [];
            }
            else
            {
                _array = pool.Rent(length);
                if (clearOnInit)
                {
                    Array.Clear(_array, 0, length);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedArray{T}"/> class with the specified length, using <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="length">The exact number of elements the array will logically contain.</param>
        /// <param name="clearOnInit">If <c>true</c>, zeroes out the rented memory. Defaults to <c>true</c>.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedArray(int length, bool clearOnInit, bool clearOnReturn)
            : this(length, ArrayPool<T>.Shared, clearOnInit, clearOnReturn) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedArray{T}"/> class by copying elements from an existing collection, using a specific array pool.
        /// </summary>
        /// <param name="items">The collection whose elements are copied into the new array.</param>
        /// <param name="pool">The pool to rent the underlying array from.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedArray(IEnumerable<T> items, ArrayPool<T> pool, bool clearOnReturn)
        {
            _pool = pool;
            _clearOnReturn = clearOnReturn;
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
                        // ALWAYS clear intermediate arrays, the user cannot access them to do it manually
                        using var rentedList = items.ToRentedList(true);
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

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedArray{T}"/> class by copying elements
        /// from an existing collection, using <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="items">The collection whose elements are copied into the new array.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedArray(IEnumerable<T> items, bool clearOnReturn)
            : this(items, ArrayPool<T>.Shared, clearOnReturn) { }

        /// <summary>
        /// Returns the underlying array to the pool and invalidates this instance.
        /// </summary>
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
            if (disposing && _array.Length != 0)
            {
                _pool.Return(_array, _clearOnReturn);
            }
            _array = null!;
        }

        /// <summary>
        /// Gets a <see cref="Memory{T}"/> view sliced to the exact logical length of the rented array.
        /// </summary>
        public Memory<T> Memory => new(_array, 0, Length);

        /// <summary>
        /// Gets a <see cref="ReadOnlyMemory{T}"/> view sliced to the exact logical length of the rented array.
        /// </summary>
        public ReadOnlyMemory<T> ReadOnlyMemory => new(_array, 0, Length);

        /// <summary>
        /// Gets a <see cref="Span{T}"/> view sliced to the exact logical length of the rented array.
        /// </summary>
        public Span<T> Span => new(_array, 0, Length);

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> view sliced to the exact logical length of the rented array.
        /// </summary>
        public ReadOnlySpan<T> ReadOnlySpan => new(_array, 0, Length);

        #region // IEnumerable<T>

        /// <summary>
        /// Zero-allocation struct enumerator for <see cref="RentedArray{T}"/>.
        /// </summary>
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

        /// <summary>
        /// Returns an enumerator that iterates through the collection without allocating on the heap.
        /// </summary>
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        #endregion

        #region // ICollection<T>

        public void Add(T item) => throw new InvalidOperationException("cannot add items to array");

        /// <summary>
        /// Clears the logical contents of the array by resetting elements to their default value.
        /// This does not shrink the array or return it to the pool.
        /// </summary>
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

        /// <summary>
        /// Gets the number of logical elements contained in the array.
        /// </summary>
        public int Count => Length;

        public bool IsReadOnly => false;

        #endregion

        #region // IList<T>

        public int IndexOf(T item) => Array.IndexOf(_array, item, 0, Length);

        public void Insert(int index, T item) =>
            throw new InvalidOperationException("cannot insert items to array");

        public void RemoveAt(int index) =>
            throw new InvalidOperationException("cannot remove items from array");

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is outside the logical <see cref="Length"/>.</exception>
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
        /// Executes a delegate against the raw underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING: DO NOT STORE EXPOSED UNDERLYING ARRAY REFERENCE!</strong>
        /// <br/>If the reference of the exposed underlying array is stored somewhere, the <see cref="RentedArray{T}"/>
        /// will not know about this and will eventually return it to the pool, causing memory corruption.
        /// <br/>Note that the underlying array's length is likely larger than the logical <see cref="Length"/> of this collection.
        /// </remarks>
        /// <param name="action">The action to execute against the raw array.</param>
        public void UnsafeAccessUnderlyingArray(Action<T[]> action) => action(_array);

        /// <summary>
        /// Creates a <see cref="RentedArray{T}"/> using a provided array and assumes ownership of it.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING: Use with caution!</strong> The <see cref="RentedArray{T}"/> will assume management
        /// of the array and will return it to the provided <paramref name="pool"/> on <see cref="Dispose"/>.
        /// Do not return the array to the pool manually after calling this method.
        /// </remarks>
        /// <param name="length">The logical length of the array data.</param>
        /// <param name="rented">The existing array to take ownership of.</param>
        /// <param name="pool">The pool the array belongs to.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        /// <returns>A managed <see cref="RentedArray{T}"/> instance.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if <paramref name="length"/> exceeds the actual length of the <paramref name="rented"/> array.
        /// </exception>
        public static RentedArray<T> UnsafeCreateFromExisting(
            int length,
            T[] rented,
            ArrayPool<T> pool,
            bool clearOnReturn
        )
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, rented.Length);
            return new RentedArray<T>(length, pool, rented, clearOnReturn);
        }

        /// <summary>
        /// Detaches the underlying array from this collection, transferring ownership to the caller.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING: Use with caution!</strong> This operation permanently detaches the array.
        /// The <see cref="RentedArray{T}"/> instance is effectively disposed and can no longer be used.
        /// The caller assumes full responsibility for returning the array to the provided <see cref="ArrayPool{T}"/>.
        /// </remarks>
        /// <returns>A tuple containing the raw array and the exact pool it must be returned to.</returns>
        public (T[] Array, ArrayPool<T> Pool) UnsafeDetachUnderlyingArray()
        {
            var array = _array;
            _array = null!;
            return (array, _pool);
        }
    }

    /// <summary>
    /// Provides extension methods for creating <see cref="RentedArray{T}"/> instances.
    /// </summary>
    public static class RentedArrayExtensions
    {
        extension<T>(IEnumerable<T> items)
        {
            /// <summary>
            /// Allocates a new <see cref="RentedArray{T}"/> from the specified pool and populates it with elements from the sequence.
            /// </summary>
            /// <param name="pool">The pool to rent the underlying array from.</param>
            /// <param name="clearOnReturn">
            /// If <c>true</c>, zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
            /// <br/><strong>WARNING:</strong> If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
            /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
            /// </param>
            public RentedArray<T> ToRentedArray(ArrayPool<T> pool, bool clearOnReturn) =>
                new(items, pool, clearOnReturn);

            /// <summary>
            /// Allocates a new <see cref="RentedArray{T}"/> from <see cref="ArrayPool{T}.Shared"/> and populates it with elements from the sequence.
            /// </summary>
            /// <param name="clearOnReturn">
            /// If <c>true</c>, zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
            /// <br/><strong>WARNING:</strong> If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
            /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
            /// </param>
            public RentedArray<T> ToRentedArray(bool clearOnReturn) =>
                items.ToRentedArray(ArrayPool<T>.Shared, clearOnReturn);
        }
    }
}
