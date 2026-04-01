using System;
using System.Collections.Generic;

namespace RCi.Toolbox.Boxes
{
    /// <summary>
    /// Wraps and places value on a heap.
    /// </summary>
    public sealed class Box<T> : IBox<T>
    {
        private readonly Func<T, T, bool> _funcEquals;

        private T _value;
        public T Value
        {
            get => _value;
            set
            {
                if (_funcEquals(_value, value))
                {
                    return;
                }
                _value = value;
                ValueChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<T>? ValueChanged;

        public Box(T initValue, Func<T, T, bool> funcEquals)
        {
            _value = initValue;
            _funcEquals = funcEquals;
        }

        public Box(T initValue)
            : this(initValue, EqualityComparer<T>.Default.Equals) { }

        public override string ToString() => $"{Value}";

        public static implicit operator T(Box<T> box) => box.Value;
    }
}
