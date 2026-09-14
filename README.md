# RCi.Toolbox

[![NuGet Version](https://img.shields.io/nuget/v/RCi.Toolbox.svg)](https://www.nuget.org/packages/RCi.Toolbox/)
[![Target Framework](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A focused .NET 10 library providing high-performance concurrency primitives, pooled memory collections, reactive state synchronization, and low-overhead timing utilities.

---

## Highlights

- **Pooled Collections** (`RentedArray<T>`, `RentedList<T>`): High-performance collections backed by `ArrayPool<T>` to eliminate GC allocations, featuring zero-allocation struct enumerators, span/memory slicing, and leak prevention.
- **Concurrency & Work Distribution** (`JobQueue`, `CoalescingWorker`): Dedicated thread pools for sequential or parallel workloads, and debounce-style coalescing workers to merge redundant requests.
- **Lock-Free Synchronization** (`AtomicGate`): A lightweight, thread-safe gate (`Ready` &rarr; `Executing` &rarr; `Sealed`) guaranteeing single-execution semantics with fast-fail.
- **Synchronized State Containers** (`SyncBox<T>`, `SyncBoxDeferred<T>`, `Box<T>`): Thread-safe observable values with transactional access (`AccessLocked`), condition waiting (`WaitForAsync`), or deferred in-order event dispatch.
- **Timing & Diagnostics** (`ValueStopwatch`, `SleepExtensions`): Zero-allocation struct stopwatch with `TimeProvider` support, and fluent, non-throwing delay extensions for `TimeSpan`.

---

## Installation

```shell
dotnet add package RCi.Toolbox
```

Supported frameworks: `.NET 10.0`.

---

## Features & Usage

### 1. Pooled Collections (`RCi.Toolbox.Collections`)

Avoid heap allocations in hot paths by renting memory from `ArrayPool<T>`.

#### `RentedArray<T>`
A fixed-size collection backed by pooled memory. Implements `IMemoryOwner<T>`, `IList<T>`, and `IReadOnlyList<T>`, with direct access to `Span<T>` and `Memory<T>`.

```csharp
using RCi.Toolbox.Collections;

// Rent a fixed-size buffer from ArrayPool<T>.Shared
using var buffer = new RentedArray<byte>(1024, clearOnInit: false, clearOnReturn: false);

Span<byte> span = buffer.Span;
span[0] = 0x42;

// Convert sequences or spans into rented arrays:
ReadOnlySpan<int> data = [1, 2, 3, 4, 5];
using var rented = data.ToRentedArray(clearOnReturn: false);
```

> **Note on `clearOnReturn`:** Always enable `clearOnReturn: true` when storing reference types or sensitive data. This prevents objects from dangling in the pool and blocking garbage collection.

#### `RentedList<T>`
A dynamically resizable list backed by `ArrayPool<T>`. It defaults to an initial capacity of 256 to avoid repeated reallocation cycles (4 &rarr; 8 &rarr; 16 &rarr; ... &rarr; 256) without starving pool buckets.

```csharp
using RCi.Toolbox.Collections;

using var list = new RentedList<int>(clearOnReturn: false);

list.Add(10);
list.AddRange([20, 30, 40]);

// Zero-allocation struct enumeration
foreach (var item in list)
{
    Console.WriteLine(item);
}

// Inspect active elements without heap copying
ReadOnlySpan<int> activeElements = list.AsReadOnlySpanUnsafe();
```

---

### 2. Work Execution & Debouncing

#### `CoalescingWorker`
If multiple execution requests arrive while a job is already in progress, `CoalescingWorker` collapses them into at most **one** pending follow-up run. Ideal for file watchers, cache invalidation, search re-indexing, or UI redraws.

```csharp
using RCi.Toolbox;

using var worker = new CoalescingWorker(
    new CoalescingWorkerParameters { Name = "SearchIndexer" },
    () => RebuildSearchIndex()
);

// Rapid burst triggers:
worker.Schedule();
worker.Schedule(); // Coalesced into a single pending execution
worker.Schedule(); // Coalesced

// Await completion of active and queued executions
await worker.WaitForIdleAsync();
```

#### `JobQueue`
A dedicated worker thread queue supporting sequential FIFO execution (1 worker) or parallel processing (N workers). Avoids thread-pool starvation for long-running or blocking workloads.

```csharp
using RCi.Toolbox;

// Queue with 4 dedicated worker threads
using var queue = new JobQueue(new JobQueueParameters
{
    WorkerCount = 4,
    Name = "TaskProcessor",
    ThreadPriority = ThreadPriority.BelowNormal,
});

// Asynchronously enqueue work (fire-and-forget or with completion callback)
queue.Post(ct => ProcessBatch(ct));

// Or enqueue and await the result with timeout and cancellation
JobResult<int> result = await queue.SendAsync(
    ct => ComputeChecksum(ct),
    timeout: TimeSpan.FromSeconds(5),
    cancellationToken
);

if (result.Exception is null && !result.Cancelled)
{
    Console.WriteLine($"Computed: {result.Result}");
}
```

---

### 3. Concurrency Primitives

#### `AtomicGate`
A lock-free gate providing single-execution semantics. Once executed, the gate permanently transitions to `Sealed`; all subsequent attempts immediately return `false`.

```csharp
using RCi.Toolbox;

var gate = new AtomicGate();

// Exactly one thread executes the action; other threads fast-fail
if (gate.TryExecute(() => InitializeSubsystem()))
{
    Console.WriteLine("Subsystem initialized by this thread.");
}

// Asynchronous overload:
bool executed = await gate.TryExecuteAsync(async () =>
{
    await StartServiceAsync();
});
```

---

### 4. Observable State Containers (`RCi.Toolbox.Boxes`)

#### `SyncBox<T>`
A thread-safe value wrapper protected by an internal lock. Events are fired synchronously within the lock, ensuring subscribers always observe the exact value at the moment of notification.

```csharp
using RCi.Toolbox.Boxes;

var box = new SyncBox<int>(0);

// Atomic read-modify-write
box.AccessLocked((get, set) =>
{
    var current = get();
    set(current + 1);
});

// Asynchronously wait until a predicate is satisfied
bool reached = await box.WaitForAsync(val => val >= 10, TimeSpan.FromSeconds(5));
```

#### `SyncBoxDeferred<T>`
Similar to `SyncBox<T>`, but dispatches value change events asynchronously via an internal unbounded channel. This ensures that slow subscribers or UI event handlers cannot block write operations or cause cross-thread deadlocks.

```csharp
using RCi.Toolbox.Boxes;

using var deferredBox = new SyncBoxDeferred<string>("idle");

deferredBox.ValueChanged += (sender, state) =>
{
    // Dispatched asynchronously outside the lock
    Console.WriteLine($"State transitioned: {state}");
};

deferredBox.Value = "running";
```

---

### 5. Timing & Utilities

#### `ValueStopwatch`
An allocation-free `readonly struct` alternative to `System.Diagnostics.Stopwatch`. It supports .NET's `TimeProvider`, making elapsed-time logic fully testable.

```csharp
using RCi.Toolbox;

var sw = ValueStopwatch.StartNew();
ExecuteOperation();
Console.WriteLine($"Completed in {sw.Elapsed.TotalMilliseconds:F2} ms");

// With custom or mock TimeProvider:
var testSw = ValueStopwatch.StartNew(fakeTimeProvider);
```

#### `SleepExtensions`
Fluent extension methods on `TimeSpan` for synchronous blocking and asynchronous delays. Cancellation overloads return a `bool` rather than throwing `OperationCanceledException`, avoiding exception overhead during expected cancellations.

```csharp
using RCi.Toolbox;

// Asynchronous delay returning true if elapsed, false if cancelled
bool completed = await TimeSpan.FromSeconds(2).SleepAsync(cancellationToken);

// Synchronous sleep with TimeProvider
TimeSpan.FromMilliseconds(500).Sleep(TimeProvider.System, cancellationToken);
```

---

## License

This project is licensed under the [MIT License](LICENSE).
