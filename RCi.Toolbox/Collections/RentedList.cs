using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RCi.Toolbox.Collections
{
    /// <summary>
    /// A high-performance, dynamically resizable collection backed by <see cref="ArrayPool{T}"/>.
    /// Dynamic Resizing: like <see cref="List{T}"/>, this collection grows automatically.
    /// However, resizing requires renting a new array and returning the old one to the pool.
    ///
    /// <br/><strong>WARNING:</strong> Turn on <strong>clearOnReturn</strong> when dealing with rented arrays which hold reference types!
    /// It zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
    /// If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
    /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
    /// </summary>
    public sealed class RentedList<T> : IDisposable, IList<T>, IReadOnlyList<T>
    {
        private const int DefaultCapacity = 4;

        private readonly ArrayPool<T> _pool;
        private readonly bool _clearOnReturn;
        private T[] _items;
        private int _size;
        private int _version;

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that is empty
        /// and has the specified initial capacity, using a specific array pool.
        /// </summary>
        /// <param name="initCapacity">The number of elements that the new list can initially store.</param>
        /// <param name="pool">The pool to rent the underlying array from.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(int initCapacity, ArrayPool<T> pool, bool clearOnReturn)
        {
            _pool = pool;
            _clearOnReturn = clearOnReturn;
            _items = pool.Rent(initCapacity);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that is empty
        /// and has the specified initial capacity, using <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="initCapacity">The number of elements that the new list can initially store.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(int initCapacity, bool clearOnReturn)
            : this(initCapacity, ArrayPool<T>.Shared, clearOnReturn) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that is empty
        /// and has the default initial capacity, using a specific array pool.
        /// </summary>
        /// <param name="pool">The pool to rent the underlying array from.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(ArrayPool<T> pool, bool clearOnReturn)
            : this(DefaultCapacity, pool, clearOnReturn) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that is empty
        /// and has the default initial capacity, using <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(bool clearOnReturn)
            : this(DefaultCapacity, ArrayPool<T>.Shared, clearOnReturn) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that contains elements
        /// copied from the specified collection, using a specific array pool.
        /// </summary>
        /// <param name="initItems">The collection whose elements are copied into the new list.</param>
        /// <param name="pool">The pool to rent the underlying array from.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(IEnumerable<T> initItems, ArrayPool<T> pool, bool clearOnReturn)
        {
            _pool = pool;
            _clearOnReturn = clearOnReturn;
            switch (initItems)
            {
                case T[] array:
                    _items = pool.Rent(array.Length);
                    array.CopyTo(_items.AsSpan());
                    _size = array.Length;
                    break;

                case ImmutableArray<T> immutableArray:
                    _items = pool.Rent(immutableArray.Length);
                    immutableArray.CopyTo(_items.AsSpan());
                    _size = immutableArray.Length;
                    break;

                case ICollection<T> collection:
                    var length = collection.Count;
                    _items = pool.Rent(collection.Count);
                    collection.CopyTo(_items, 0);
                    _size = length;
                    break;

                default:
                    _items = pool.Rent(DefaultCapacity);
                    foreach (var item in initItems)
                    {
                        Add(item);
                    }
                    break;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RentedList{T}"/> class that contains elements
        /// copied from the specified collection, using <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="initItems">The collection whose elements are copied into the new list.</param>
        /// <param name="clearOnReturn">
        /// If <c>true</c>, zeroes out rented memory when it is returned to the pool, this is useful when dealing with sensitive data.
        /// WARNING: If rented array holds reference types and is not cleared on return, the objects will be dangling in the array
        /// and GC won't be able to collect them (unless array is re-rented and references overriden, which is undeterministic).
        /// </param>
        public RentedList(IEnumerable<T> initItems, bool clearOnReturn)
            : this(initItems, ArrayPool<T>.Shared, clearOnReturn) { }

        /// <summary>
        /// Returns the underlying array to the pool and invalidates this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RentedList() => Dispose(false);

        private void Dispose(bool disposing)
        {
            if (_items is null)
            {
                return;
            }
            if (disposing && _items.Length != 0)
            {
                _pool.Return(_items, _clearOnReturn);
            }
            _items = null!;
        }

        /// <summary>
        /// Creates a <see cref="Memory{T}"/> view over the current underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING:</strong> Unlike <see cref="Span{T}"/>, <see cref="Memory{T}"/> can be stored on the heap and outlive the current scope.
        /// <br/>• <strong>Do not</strong> store this instance in a field or capture it in an async state machine.
        /// <br/>• <strong>Do not</strong> use this instance after modifying the list (e.g., calling Add or Insert), as a resize will return the underlying array to the array pool.
        /// <br/>• <strong>Do not</strong> use this instance after the list is disposed.
        /// <br/>Violating these rules will result in Use-After-Free bugs, silently corrupting memory rented by other parts of the application.
        /// </remarks>
        /// <returns>A <see cref="Memory{T}"/> pointing to the current active elements.</returns>
        public Memory<T> AsMemoryUnsafe() => new(_items, 0, _size);

        /// <summary>
        /// Creates a <see cref="ReadOnlyMemory{T}"/> view over the current underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING:</strong> <see cref="ReadOnlyMemory{T}"/> can escape the local stack.
        /// If the list is resized or disposed, this memory view will point to a pooled array that may be overwritten by completely unrelated code,
        /// causing your reads to return corrupted or unexpected data.
        /// Do not store this instance or use it after modifying or disposing the list.
        /// </remarks>
        /// <returns>A <see cref="ReadOnlyMemory{T}"/> pointing to the current active elements.</returns>
        public ReadOnlyMemory<T> AsReadOnlyMemoryUnsafe() => new(_items, 0, _size);

        /// <summary>
        /// Creates a <see cref="Span{T}"/> view over the current underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING:</strong> The returned span is only valid for the exact moment it is created.
        /// <br/>• <strong>Do not</strong> mutate the list (add, insert, or clear) while holding this span. Mutations can trigger a reallocation, returning the original array to the pool.
        /// <br/>• Writing to a span after a resize has occurred might overwrite memory belonging to another component, causing severe bugs.
        /// </remarks>
        /// <returns>A <see cref="Span{T}"/> pointing to the current active elements.</returns>
        public Span<T> AsSpanUnsafe() => new(_items, 0, _size);

        /// <summary>
        /// Creates a <see cref="ReadOnlySpan{T}"/> view over the current underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING:</strong> The returned span is only valid for the exact moment it is created.
        /// Do not mutate the list while iterating or reading from this span. If a resize occurs, this span will point to pooled memory
        /// that may suddenly change under the hood as other application components rent and write to it.
        /// </remarks>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> pointing to the current active elements.</returns>
        public ReadOnlySpan<T> AsReadOnlySpanUnsafe() => new(_items, 0, _size);

        /// <summary>
        /// Gets or sets the total number of elements the internal data structure can hold without resizing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is set to a value less than <see cref="Count"/>.</exception>
        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                if (value != _items.Length && value > 0)
                {
                    var newItems = _pool.Rent(value);
                    if (_size > 0)
                    {
                        Array.Copy(_items, newItems, _size);
                    }
                    _pool.Return(_items, _clearOnReturn);
                    _items = newItems;
                }
            }
        }

        private void Grow(int capacity)
        {
            Debug.Assert(_items.Length < capacity);
            var newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;
            if ((uint)newCapacity > Array.MaxLength)
            {
                newCapacity = Array.MaxLength;
            }
            if (newCapacity < capacity)
            {
                newCapacity = capacity;
            }
            Capacity = newCapacity;
        }

        #region // IEnumerable<T>

        /// <summary>
        /// Zero-allocation struct enumerator for <see cref="RentedList{T}"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly RentedList<T> _list;
            private int _index;
            private readonly int _version;
            private T? _current;

            internal Enumerator(RentedList<T> list)
            {
                _list = list;
                _index = 0;
                _version = list._version;
                _current = default;
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                var localList = _list;
                if (_version == localList._version && (uint)_index < (uint)localList._size)
                {
                    _current = localList._items[_index];
                    _index++;
                    return true;
                }
                return MoveNextRare();
            }

            private bool MoveNextRare()
            {
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("EnumFailedVersion");
                }
                _index = _list._size + 1;
                _current = default;
                return false;
            }

            public T Current => _current!;

            object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _list._size + 1)
                    {
                        throw new InvalidOperationException("EnumOpCantHappen");
                    }
                    return Current;
                }
            }

            public void Reset()
            {
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("EnumFailedVersion");
                }
                _index = 0;
                _current = default;
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection without allocating on the heap.
        /// </summary>
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region // ICollection<T>

        /// <summary>
        /// Adds an object to the end of the <see cref="RentedList{T}"/>.
        /// </summary>
        public void Add(T item)
        {
            _version++;
            var array = _items;
            var size = _size;
            if ((uint)size < (uint)array.Length)
            {
                _size = size + 1;
                array[size] = item;
            }
            else
            {
                AddWithResize(item);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            var size = _size;
            Grow(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        /// <summary>
        /// Removes all elements from the <see cref="RentedList{T}"/>.
        /// </summary>
        /// <remarks>
        /// This sets the <see cref="Count"/> to 0 and clears references to allow garbage collection,
        /// but it does <strong>not</strong> change the <see cref="Capacity"/> or return the underlying array to the pool.
        /// </remarks>
        public void Clear()
        {
            _version++;
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                var size = _size;
                if (size > 0)
                {
                    Array.Clear(_items, 0, size);
                }
            }
            _size = 0;
        }

        public bool Contains(T item) => _size != 0 && IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex) =>
            Array.Copy(_items, 0, array, arrayIndex, _size);

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the number of elements actually contained in the <see cref="RentedList{T}"/>.
        /// </summary>
        public int Count => _size;

        public bool IsReadOnly => false;

        #endregion

        #region // IList<T>

        public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _size);

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_size)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (_size == _items.Length)
            {
                Grow(_size + 1);
            }
            if (index < _size)
            {
                Array.Copy(_items, index, _items, index + 1, _size - index);
            }
            _items[index] = item;
            _size++;
            _version++;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            _size--;
            if (index < _size)
            {
                Array.Copy(_items, index + 1, _items, index, _size - index);
            }
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items[_size] = default!;
            }
            _version++;
        }

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the index is outside the logical <see cref="Count"/>.</exception>
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return _items[index];
            }
            set
            {
                if ((uint)index >= (uint)_size)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                _items[index] = value;
                _version++;
            }
        }

        #endregion

        /// <summary>
        /// Adds the elements of the specified collection to the end of the <see cref="RentedList{T}"/>.
        /// </summary>
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection is ICollection<T> c)
            {
                var count = c.Count;
                if (count > 0)
                {
                    if (_items.Length - _size < count)
                    {
                        Grow(checked(_size + count));
                    }
                    c.CopyTo(_items, _size);
                    _size += count;
                    _version++;
                }
            }
            else
            {
                using var en = collection.GetEnumerator();
                while (en.MoveNext())
                {
                    Add(en.Current);
                }
            }
        }

        /// <summary>
        /// Executes a delegate against the raw underlying array.
        /// </summary>
        /// <remarks>
        /// <strong>WARNING: DO NOT STORE EXPOSED UNDERLYING ARRAY REFERENCE!</strong>
        /// <br/>If the reference of the exposed underlying array is stored somewhere, the <see cref="RentedList{T}"/> will not know about this.
        /// If the list resizes, the array will be returned to the pool, and the stored reference will become a dangling pointer to pooled memory.
        /// <br/>Note that the underlying array's length is likely larger than the logical <see cref="Count"/> of this collection.
        /// </remarks>
        /// <param name="action">The action to execute against the raw array.</param>
        public void UnsafeAccessUnderlyingArray(Action<T[]> action) => action(_items);
    }

    public static class RentedListExtensions
    {
        extension<T>(IEnumerable<T> items)
        {
            /// <summary>
            /// Allocates a new <see cref="RentedList{T}"/> from the specified pool and populates it with elements from the sequence.
            /// </summary>
            /// <param name="pool">The pool to rent the underlying array from.</param>
            /// <param name="clearOnReturn">
            /// If <c>true</c>, zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
            /// <br/><strong>WARNING:</strong> If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
            /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
            /// </param>
            public RentedList<T> ToRentedList(ArrayPool<T> pool, bool clearOnReturn) =>
                new(items, pool, clearOnReturn);

            /// <summary>
            /// Allocates a new <see cref="RentedList{T}"/> from <see cref="ArrayPool{T}.Shared"/> and populates it with elements from the sequence.
            /// </summary>
            /// <param name="clearOnReturn">
            /// If <c>true</c>, zeroes out the rented memory when it is returned to the pool. This is useful when dealing with sensitive data.
            /// <br/><strong>WARNING:</strong> If the rented array holds reference types and is not cleared on return, the objects will be dangling in the pool.
            /// The GC won't be able to collect them unless the exact array slot is re-rented and the references are overwritten, which is unpredictable.
            /// </param>
            public RentedList<T> ToRentedList(bool clearOnReturn) =>
                items.ToRentedList(ArrayPool<T>.Shared, clearOnReturn);
        }
    }
}
