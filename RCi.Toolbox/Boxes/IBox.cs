using System;

namespace RCi.Toolbox.Boxes
{
    public interface IBoxReadOnly<out T>
    {
        T Value { get; }

        event EventHandler<T> ValueChanged;
    }

    public interface IBox<T> : IBoxReadOnly<T>
    {
        new T Value { get; set; }
    }
}
