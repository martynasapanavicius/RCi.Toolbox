using System;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox
{
    /// <summary>
    /// Represents the lifecycle state of an <see cref="AtomicGate"/>.
    /// </summary>
    public enum AtomicGateState
    {
        /// <summary>
        /// The gate is open and ready to accept the first thread.
        /// </summary>
        Ready = 0,

        /// <summary>
        /// A thread has entered the gate and is currently executing its payload.
        /// </summary>
        Executing = 1,

        /// <summary>
        /// The gate has been permanently closed. No further executions are possible.
        /// </summary>
        Sealed = 2,
    }

    /// <summary>
    /// A lightweight, thread-safe, and lock-free synchronization gate.
    /// Ensures that an operation is attempted exactly once. Once the gate is triggered,
    /// it becomes permanently sealed, and all subsequent attempts will fast-fail.
    /// </summary>
    public sealed class AtomicGate
    {
        private const long READY = 0;
        private const long EXECUTING = 1;
        private const long SEALED = 2;

        private long _state;

        /// <summary>
        /// Gets the current state of the gate.
        /// </summary>
        public AtomicGateState State => (AtomicGateState)Interlocked.Read(ref _state);

        /// <summary>
        /// Tries to acquire the lock and enter the gate.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - Lock acquired and gate entered.<br/>
        ///     <see langword="false"/> - Failed to acquire lock; the gate is currently executing or already sealed.
        /// </returns>
        public bool TryEnter() =>
            Interlocked.CompareExchange(ref _state, EXECUTING, READY) == READY;

        /// <inheritdoc cref="TryEnter()" />
        /// <param name="currentState">Returns the state of the gate after the attempt.</param>
        public bool TryEnter(out AtomicGateState currentState)
        {
            var originalState = Interlocked.CompareExchange(ref _state, EXECUTING, READY);
            if (originalState == READY)
            {
                // all good, we switched from READY to EXECUTING
                currentState = AtomicGateState.Executing;
                return true;
            }

            // failed, the state was not READY
            currentState = (AtomicGateState)originalState;
            return false;
        }

        /// <summary>
        /// Tries to release the lock and seal the gate.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> - Lock released and gate sealed.<br/>
        ///     <see langword="false"/> - Failed to release lock; the gate was not in an executing state.
        /// </returns>
        public bool TryExit() =>
            Interlocked.CompareExchange(ref _state, SEALED, EXECUTING) == EXECUTING;

        /// <inheritdoc cref="TryExit()" />
        /// <param name="currentState">Returns the state of the gate after the attempt.</param>
        public bool TryExit(out AtomicGateState currentState)
        {
            var originalState = Interlocked.CompareExchange(ref _state, SEALED, EXECUTING);
            if (originalState == EXECUTING)
            {
                // all good, we switched from EXECUTING to SEALED
                currentState = AtomicGateState.Sealed;
                return true;
            }

            // failed, the state was not RUNNING
            currentState = (AtomicGateState)originalState;
            return false;
        }

        /// <inheritdoc cref="TryExecute(Action, out AtomicGateState)" />
        public bool TryExecute(Action job) => TryExecute(job, out _);

        /// <summary>
        /// Attempts to execute the provided job if the gate is in a <see cref="AtomicGateState.Ready"/> state.
        /// Regardless of whether the job succeeds or throws an exception, the gate will be permanently sealed afterward.
        /// </summary>
        /// <param name="job">The user's synchronous code to execute.</param>
        /// <param name="currentState">The state of the gate after leaving this method.</param>
        /// <returns><see langword="true"/> if this thread successfully acquired the gate and executed the job; otherwise <see langword="false"/>.</returns>
        public bool TryExecute(Action job, out AtomicGateState currentState)
        {
            var success = false;
            try
            {
                // try to acquire lock and enter the scope
                if (!TryEnter(out currentState))
                {
                    return false;
                }

                success = true;

                // execute user's code
                // NOTE: we don't care if user's code throws exceptions,
                // finally block will handle resources properly and seal the gate
                job();

                return true;
            }
            finally
            {
                // release the lock
                if (success)
                {
                    if (!TryExit(out currentState))
                    {
                        // this shouldn't be possible
                        throw new InvalidOperationException(
                            "failed to exit scope, current state: " + currentState
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously attempts to execute the provided job if the gate is in a <see cref="AtomicGateState.Ready"/> state.
        /// Regardless of whether the job succeeds or throws an exception, the gate will be permanently sealed afterward.
        /// </summary>
        /// <param name="job">The user's asynchronous code to execute.</param>
        /// <returns><see langword="true"/> if this thread successfully acquired the gate and executed the job; otherwise <see langword="false"/>.</returns>
        public async ValueTask<bool> TryExecuteAsync(Func<ValueTask> job)
        {
            var success = false;
            try
            {
                // try to acquire lock and enter the scope
                if (!TryEnter())
                {
                    return false;
                }

                success = true;

                // execute user's code
                // NOTE: we don't care if user's code throws exceptions,
                // finally block will handle resources properly and seal the gate
                await job();

                return true;
            }
            finally
            {
                // release the lock
                if (success)
                {
                    if (!TryExit(out var currentState))
                    {
                        // this shouldn't be possible
                        throw new InvalidOperationException(
                            "failed to exit scope, current state: " + currentState
                        );
                    }
                }
            }
        }
    }
}
