using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class AtomicGateTests
    {
        [Test]
        public static void Integration()
        {
            var gate = new AtomicGate();
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Ready));

            // try to enter
            var success = gate.TryEnter(out var state);
            Assert.That(success, Is.True);
            Assert.That(state, Is.EqualTo(AtomicGateState.Executing));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Executing));

            // try to enter again
            success = gate.TryEnter(out state);
            Assert.That(success, Is.False);
            Assert.That(state, Is.EqualTo(AtomicGateState.Executing));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Executing));

            // try to exit
            success = gate.TryExit(out state);
            Assert.That(success, Is.True);
            Assert.That(state, Is.EqualTo(AtomicGateState.Sealed));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Sealed));

            // try to exit again
            success = gate.TryExit(out state);
            Assert.That(success, Is.False);
            Assert.That(state, Is.EqualTo(AtomicGateState.Sealed));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Sealed));

            // entering after exit should also be rejected
            success = gate.TryEnter(out state);
            Assert.That(success, Is.False);
            Assert.That(state, Is.EqualTo(AtomicGateState.Sealed));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Sealed));
        }

        [Test]
        public static void TryExecute()
        {
            var gate = new AtomicGate();
            using var waiter = new ManualResetEventSlim(false);
            var counter = 0;

            var tasks = new Task[10];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    // block and wait for signal to continue
                    waiter.Wait();

                    // execute user's code
                    gate.TryExecute(() => Interlocked.Increment(ref counter));
                });
            }

            // allow all tasks to start waiting
            Thread.Sleep(500);

            // launch all at the same time (as close as possible)
            waiter.Set();

            // allow all tasks to finish
            Task.WaitAll(tasks);

            // only one task had to be allowed to enter and exit the scope
            Assert.That(counter, Is.EqualTo(1));
        }

        [Test]
        public static async Task TryExecuteAsync()
        {
            var gate = new AtomicGate();
            using var waiter = new ManualResetEventSlim(false);
            var counter = 0;

            var tasks = new Task[10];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    // block and wait for signal to continue
                    waiter.Wait();

                    // execute user's code
                    await gate.TryExecuteAsync(async () =>
                    {
                        // await something to force thread yielding,
                        // this proves the lock holds its EXECUTING state correctly
                        // across async state machine continuations without thread-affinity bugs
                        await Task.Yield();

                        Interlocked.Increment(ref counter);
                    });
                });
            }

            // allow all tasks to start waiting
            await Task.Delay(500);

            // launch all at the same time (as close as possible)
            waiter.Set();

            // allow all tasks to finish
            await Task.WhenAll(tasks);

            // only one task had to be allowed to enter and exit the scope
            Assert.That(counter, Is.EqualTo(1));
            Assert.That(gate.State, Is.EqualTo(AtomicGateState.Sealed));
        }
    }
}
