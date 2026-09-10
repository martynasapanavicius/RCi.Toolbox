using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RCi.Toolbox.Tests
{
    [Parallelizable(ParallelScope.All)]
    public static class JobQueueTests
    {
        [Test]
        public static void Ctor_Default()
        {
            using var jobQueue = new JobQueue();
            Assert.That(jobQueue.Parameters, Is.EqualTo(JobQueueParameters.Default));
        }

        [Test]
        public static void Ctor()
        {
            var parameters = new JobQueueParameters
            {
                WorkerCount = 3,
                Name = "Some name",
                ThreadPriority = ThreadPriority.Lowest,
                UseBackgroundThreads = true,
            };
            using var jobQueue = new JobQueue(parameters);
            Assert.That(jobQueue.Parameters, Is.EqualTo(parameters));
        }

        [Test]
        public static void Ctor_Clamped_WorkerCount_Zero()
        {
            var parameters = new JobQueueParameters
            {
                WorkerCount = 0,
                Name = "Some name",
                ThreadPriority = ThreadPriority.Lowest,
                UseBackgroundThreads = true,
            };
            using var jobQueue = new JobQueue(parameters);
            Assert.That(jobQueue.Parameters, Is.EqualTo(parameters with { WorkerCount = 1 }));
        }

        [Test]
        public static void Ctor_Clamped_WorkerCount_Negative()
        {
            var parameters = new JobQueueParameters
            {
                WorkerCount = -1,
                Name = "Some name",
                ThreadPriority = ThreadPriority.Lowest,
                UseBackgroundThreads = true,
            };
            using var jobQueue = new JobQueue(parameters);
            Assert.That(jobQueue.Parameters, Is.EqualTo(parameters with { WorkerCount = 1 }));
        }

        [Test]
        public static void Dispose()
        {
            var jobQueue = new JobQueue();
            jobQueue.Dispose();

            var isCancelled = jobQueue.IsCancelled;
            Assert.That(isCancelled, Is.True);

            var activeWorkerCount = jobQueue.ActiveWorkerCount;
            Assert.That(activeWorkerCount, Is.Zero);
        }

        [Test]
        public static void Dispose_Multiple()
        {
            var jobQueue = new JobQueue();
            Assert.DoesNotThrow(jobQueue.Dispose);
            Assert.DoesNotThrow(jobQueue.Dispose);
            Assert.DoesNotThrow(jobQueue.Dispose);
        }

        [Test]
        public static void CatchJobException()
        {
            using var jobQueue = new JobQueue();

            var locker = new object();
            var jobStarted = false;
            var result = default(JobResult);
            jobQueue.Post(
                () =>
                {
                    lock (locker)
                    {
                        jobStarted = true;
                    }

                    throw new Exception("test");
                },
                r =>
                {
                    lock (locker)
                    {
                        result = r;
                    }
                }
            );
            jobQueue.WaitForIdle();

            Assert.That(jobStarted, Is.True);
            Assert.That(result.Cancelled, Is.False);
            Assert.That(result.Exception, Is.Not.Null);
            Assert.That(result.Exception.Message, Is.EqualTo("test"));
        }

        [Test]
        public static void Cancel()
        {
            var queue = new ConcurrentQueue<string>();

            using var jobQueue = new JobQueue();

            var isCancelledBefore = jobQueue.IsCancelled;
            Assert.That(isCancelledBefore, Is.False);

            using var jobsCanExecute = new ManualResetEvent(false);

            var jobShouldBePosted = jobQueue.Post(_ =>
            {
                // block job and wait for signal
                jobsCanExecute.WaitOne();
                queue.Enqueue("job 0 done");
            });
            Assert.That(jobShouldBePosted, Is.True);

            // unblock jobs (currently the only active job is job 0)
            jobsCanExecute.Set();

            var cancelled = jobQueue.Cancel();
            Assert.That(cancelled, Is.True);

            var cancelledAgain = jobQueue.Cancel();
            Assert.That(cancelledAgain, Is.False);

            var isCancelledAfter = jobQueue.IsCancelled;
            Assert.That(isCancelledAfter, Is.True);

            var jobShouldBeRejected = jobQueue.Post(() =>
            {
                // this job will never execute
                jobsCanExecute.WaitOne();
                queue.Enqueue("job 1 done");
            });
            Assert.That(jobShouldBeRejected, Is.False);

            jobQueue.WaitForIdle();
            Assert.That(queue.ToArray().SequenceEqual(["job 0 done"]));
        }

        [Test]
        public static void CancellationToken()
        {
            var cancellationTokenStates = new ConcurrentQueue<bool>();
            var results = new ConcurrentQueue<JobResult>();

            using var jobQueue = new JobQueue();

            using var jobStarted = new ManualResetEvent(false);
            using var waitForCancel = new ManualResetEvent(false);

            jobQueue.Post(
                ct =>
                {
                    cancellationTokenStates.Enqueue(ct.IsCancellationRequested); // should be false

                    // unblock outer thread
                    jobStarted.Set();

                    // block job and wait for signal
                    waitForCancel.WaitOne();

                    cancellationTokenStates.Enqueue(ct.IsCancellationRequested); // should be true
                },
                results.Enqueue
            );

            // wait for thread to start (so we capture cancellation before)
            jobStarted.WaitOne();

            // cancel job queue
            jobQueue.Cancel();

            // unblock job (so we can capture flipped cancellation token)
            waitForCancel.Set();

            jobQueue.WaitForIdle();
            Assert.That(cancellationTokenStates.ToArray().SequenceEqual([false, true]));
            Assert.That(results.ToArray().SequenceEqual([new JobResult(true, null)]));
        }

        [Test]
        public static void IsCancelled_ThroughDispose()
        {
            var jobQueue = new JobQueue();
            var before = jobQueue.IsCancelled;
            Assert.That(before, Is.False);
            jobQueue.Dispose();
            var after = jobQueue.IsCancelled;
            Assert.That(after, Is.True);
        }

        [Test]
        public static void IsIdle()
        {
            using var jobQueue = new JobQueue();

            var before = jobQueue.IsIdle;
            Assert.That(before, Is.True);

            using var jobsCanExecute = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                // block job and wait for signal
                jobsCanExecute.WaitOne();
            });

            var during = jobQueue.IsIdle;
            Assert.That(during, Is.False);

            // unblock job
            jobsCanExecute.Set();

            // wait for jobs to finish
            jobQueue.WaitForIdle();

            var after = jobQueue.IsIdle;
            Assert.That(after, Is.True);
        }

        [Test]
        public static void IsBusy()
        {
            using var jobQueue = new JobQueue();

            var before = jobQueue.IsBusy;
            Assert.That(before, Is.False);

            using var jobsCanExecute = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                // block job and wait for signal
                jobsCanExecute.WaitOne();
            });

            var during = jobQueue.IsBusy;
            Assert.That(during, Is.True);

            // unblock job
            jobsCanExecute.Set();

            // wait for jobs to finish
            jobQueue.WaitForIdle();

            var after = jobQueue.IsBusy;
            Assert.That(after, Is.False);
        }

        [Test]
        public static void ActiveWorkerCount()
        {
            using var jobQueue = new JobQueue(new JobQueueParameters { WorkerCount = 2 });

            var actual = jobQueue.ActiveWorkerCount;
            Assert.That(actual, Is.EqualTo(0));

            using var jobsCanFinish = new ManualResetEvent(false);

            // will activate first worker
            using var job0Started = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                job0Started.Set();
                jobsCanFinish.WaitOne();
            });
            job0Started.WaitOne();
            actual = jobQueue.ActiveWorkerCount;
            Assert.That(actual, Is.EqualTo(1));

            // will activate second worker
            using var job1Started = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                job1Started.Set();
                jobsCanFinish.WaitOne();
            });
            job1Started.WaitOne();
            actual = jobQueue.ActiveWorkerCount;
            Assert.That(actual, Is.EqualTo(2));

            // no more workers available, job will be enqueued
            using var job2Started = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                job2Started.Set();
                jobsCanFinish.WaitOne();
            });
            // should fail to wait job 2 start, because there are no workers available to execute it
            // (the other are still stuck waiting for signal to finish)
            var waitOneSuccess = job2Started.WaitOne(TimeSpan.FromMilliseconds(500));
            Assert.That(waitOneSuccess, Is.False);
            actual = jobQueue.ActiveWorkerCount;
            Assert.That(actual, Is.EqualTo(2));

            // allow all jobs to finish
            jobsCanFinish.Set();

            // wait for all jobs to finish
            jobQueue.WaitForIdle();

            actual = jobQueue.ActiveWorkerCount;
            Assert.That(actual, Is.EqualTo(0));
        }

        [Test]
        public static void Events()
        {
            var queue = new ConcurrentQueue<string>();

            using (var jobQueue = new JobQueue())
            {
                jobQueue.ActiveWorkerCountChanged += (_, count) =>
                    queue.Enqueue($"ActiveWorkerCountChanged = {count}");
                jobQueue.Cancelled += (_, _) => queue.Enqueue("Cancelled");
                jobQueue.Disposing += (_, _) => queue.Enqueue("Disposing");
                jobQueue.Disposed += (_, _) => queue.Enqueue("Disposed");

                using var jobsCanExecute = new ManualResetEvent(false);

                jobQueue.Post(() =>
                {
                    jobsCanExecute.WaitOne();
                    queue.Enqueue("job 0");
                });
                jobQueue.Post(() =>
                {
                    jobsCanExecute.WaitOne();
                    queue.Enqueue("job 1");
                });
                jobQueue.Post(() =>
                {
                    jobsCanExecute.WaitOne();
                    queue.Enqueue("job 2");
                });

                queue.Enqueue("WaitForIdle start");

                // allow jobs to execute
                jobsCanExecute.Set();

                // wait for jobs to finish
                jobQueue.WaitForIdle();
                queue.Enqueue("WaitForIdle end");
            }

            var actual = queue.ToArray();

            var expected = new[]
            {
                "ActiveWorkerCountChanged = 1",
                "WaitForIdle start",
                "job 0",
                "job 1",
                "job 2",
                "ActiveWorkerCountChanged = 0",
                "WaitForIdle end",
                "Cancelled",
                "Disposing",
                "Disposed",
            };
            Assert.That(actual.SequenceEqual(expected));
        }

        [Test]
        public static void WaitForIdle()
        {
            using var jobQueue = new JobQueue();

            // wait on empty (should return immediately)
            jobQueue.WaitForIdle();

            using var jobsCanExecute = new ManualResetEvent(false);

            jobQueue.Post(() =>
            {
                // wait for signal to unblock the job
                jobsCanExecute.WaitOne();
            });

            var waited = jobQueue.WaitForIdle(TimeSpan.FromMilliseconds(500));
            Assert.That(waited, Is.False);

            // unblock job
            jobsCanExecute.Set();
        }
    }

    [Parallelizable]
    public static class JobQueueExtensionsTests
    {
        [Test]
        public static void PostWithResults()
        {
            var results = new ConcurrentQueue<JobResult<int>>();
            using var jobQueue = new JobQueue();
            jobQueue.Post(
                ct =>
                {
                    ct.ThrowIfCancellationRequested(); // should not throw
                    return 42;
                },
                results.Enqueue
            );
            jobQueue.WaitForIdle();
            Assert.That(results.ToArray().SequenceEqual([new JobResult<int>(false, null, 42)]));
        }

        [Test]
        public static void Send()
        {
            using var jobQueue = new JobQueue();
            jobQueue.Send(() => 42, out var result);
            Assert.That(result.Exception, Is.Null);
            Assert.That(result.Result, Is.EqualTo(42));
        }

        [Test]
        public static void Send_CatchJobException()
        {
            using var jobQueue = new JobQueue();
            jobQueue.Send<int>(() => throw new Exception("test"), out var result);
            Assert.That(result.Exception, Is.Not.Null);
            Assert.That(result.Exception.Message, Is.EqualTo("test"));
            Assert.That(result.Result, Is.EqualTo(0));
        }

        [Test]
        public static void Send_WithTimeout_TimesOut()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);

            var success = jobQueue.Send(
                _ => blocker.WaitOne(),
                TimeSpan.FromMilliseconds(50),
                out var result
            );

            Assert.That(success, Is.False);
            Assert.That(result.Exception, Is.TypeOf<TimeoutException>());

            blocker.Set();
            jobQueue.WaitForIdle();
        }

        [Test]
        public static void Send_WithCancellation_Cancels()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);
            using var cts = new CancellationTokenSource();

            var task = Task.Run(() =>
            {
                return jobQueue.Send(_ => blocker.WaitOne(), cts.Token, out _);
            });

            Thread.Sleep(50);
            cts.Cancel();

            var success = task.Result;
            Assert.That(success, Is.False);

            blocker.Set();
            jobQueue.WaitForIdle();
        }

        [Test]
        public static async Task SendAsync_ReturnsResult()
        {
            using var jobQueue = new JobQueue();
            var result = await jobQueue.SendAsync(() => 1337);
            Assert.That(result.Cancelled, Is.False);
            Assert.That(result.Exception, Is.Null);
            Assert.That(result.Result, Is.EqualTo(1337));
        }

        [Test]
        public static async Task SendAsync_CatchJobException()
        {
            using var jobQueue = new JobQueue();
            var result = await jobQueue.SendAsync<int>(() =>
                throw new InvalidOperationException("async fail")
            );
            Assert.That(result.Cancelled, Is.False);
            Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(result.Exception.Message, Is.EqualTo("async fail"));
            Assert.That(result.Result, Is.EqualTo(0));
        }

        [Test]
        public static async Task SendAsync_Action()
        {
            using var jobQueue = new JobQueue();
            var executed = false;
            var result = await jobQueue.SendAsync(() => executed = true);
            Assert.That(result.Cancelled, Is.False);
            Assert.That(result.Exception, Is.Null);
            Assert.That(executed, Is.True);
        }

        [Test]
        public static async Task SendAsync_WithTimeout_TimesOut()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);

            try
            {
                var result = await jobQueue.SendAsync(
                    () =>
                    {
                        blocker.WaitOne();
                        return 42;
                    },
                    TimeSpan.FromMilliseconds(50)
                );

                Assert.That(result.Exception, Is.TypeOf<TimeoutException>());
                Assert.That(result.Result, Is.EqualTo(0));
            }
            finally
            {
                blocker.Set();
            }

            jobQueue.WaitForIdle();
        }

        [Test]
        public static async Task SendAsync_WithCancellation_Cancels()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);
            using var cts = new CancellationTokenSource();

            try
            {
                var task = jobQueue.SendAsync(
                    () =>
                    {
                        blocker.WaitOne();
                        return 42;
                    },
                    cts.Token
                );

                await Task.Delay(50);
                cts.Cancel();

                var result = await task;
                Assert.That(result.Cancelled, Is.True);
                Assert.That(result.Exception, Is.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                blocker.Set();
            }

            jobQueue.WaitForIdle();
        }

        [Test]
        public static async Task SendAsync_Action_WithTimeout_TimesOut()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);

            try
            {
                var result = await jobQueue.SendAsync(
                    () => blocker.WaitOne(),
                    TimeSpan.FromMilliseconds(50)
                );

                Assert.That(result.Exception, Is.TypeOf<TimeoutException>());
            }
            finally
            {
                blocker.Set();
            }

            jobQueue.WaitForIdle();
        }

        [Test]
        public static async Task SendAsync_Action_WithCancellation_Cancels()
        {
            using var jobQueue = new JobQueue();
            using var blocker = new ManualResetEvent(false);
            using var cts = new CancellationTokenSource();

            try
            {
                var task = jobQueue.SendAsync(() => blocker.WaitOne(), cts.Token);

                await Task.Delay(50);
                cts.Cancel();

                var result = await task;
                Assert.That(result.Cancelled, Is.True);
                Assert.That(result.Exception, Is.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                blocker.Set();
            }

            jobQueue.WaitForIdle();
        }

        [Test]
        public static void Dispose_FromWorkerThread_ThrowsInvalidOperationException()
        {
            var jobQueue = new JobQueue();

            try
            {
                using var done = new ManualResetEvent(false);
                Exception? caughtException = null;

                jobQueue.Post(() =>
                {
                    try
                    {
                        jobQueue.Dispose();
                    }
                    catch (Exception e)
                    {
                        caughtException = e;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                Assert.That(
                    done.WaitOne(TimeSpan.FromSeconds(2)),
                    Is.True,
                    "Worker thread deadlocked during Dispose"
                );
                Assert.That(caughtException, Is.TypeOf<InvalidOperationException>());
                Assert.That(
                    caughtException.Message,
                    Is.EqualTo("cannot dispose job queue from within a worker thread")
                );
            }
            finally
            {
                jobQueue.Dispose();
            }
        }

        [Test]
        public static async Task Send_WhenCancelled_ReturnsCancelledResult()
        {
            using var jobQueue = new JobQueue();
            jobQueue.Cancel();

            // Synchronous Action Send
            var success = jobQueue.Send(() => { }, out var syncResult);
            Assert.That(success, Is.False);
            Assert.That(syncResult.Cancelled, Is.True);
            Assert.That(syncResult.Exception, Is.Null);

            // Synchronous Func<T> Send
            var successGeneric = jobQueue.Send(() => 42, out var syncGenericResult);
            Assert.That(successGeneric, Is.False);
            Assert.That(syncGenericResult.Cancelled, Is.True);
            Assert.That(syncGenericResult.Exception, Is.Null);
            Assert.That(syncGenericResult.Result, Is.EqualTo(0));

            // Asynchronous Action SendAsync
            var asyncResult = await jobQueue.SendAsync(() => { });
            Assert.That(asyncResult.Cancelled, Is.True);
            Assert.That(asyncResult.Exception, Is.Null);

            // Asynchronous Func<T> SendAsync
            var asyncGenericResult = await jobQueue.SendAsync(() => 42);
            Assert.That(asyncGenericResult.Cancelled, Is.True);
            Assert.That(asyncGenericResult.Exception, Is.Null);
            Assert.That(asyncGenericResult.Result, Is.EqualTo(0));
        }

        [Test]
        public static void Ctor_NullParameters_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new JobQueue(null!));
        }

        [Test]
        public static void Post_NullValidation()
        {
            using var jobQueue = new JobQueue();

            // Post(Action<CancellationToken>, Action<JobResult>)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(_ => { }, null!));

            // Post(Action<CancellationToken>)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(null!));

            // Extension Post(Action, Action<JobResult>)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post((Action)null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(() => { }, null!));

            // Extension Post(Action)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post((Action)null!));

            // Extension Post<T>(Func<CancellationToken, T>, Action<JobResult<T>>)
            Assert.Throws<ArgumentNullException>(() =>
                jobQueue.Post((Func<CancellationToken, int>)null!, _ => { })
            );
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(_ => 42, null!));

            // Extension Post<T>(Func<T>, Action<JobResult<T>>)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post((Func<int>)null!, _ => { }));
            Assert.Throws<ArgumentNullException>(() => jobQueue.Post(() => 42, null!));
        }

        [Test]
        public static void Send_NullValidation()
        {
            using var jobQueue = new JobQueue();

            // Send(Action<CancellationToken>)
            Assert.Throws<ArgumentNullException>(() =>
                jobQueue.Send((Action<CancellationToken>)null!)
            );
            Assert.Throws<ArgumentNullException>(() =>
                jobQueue.Send((Action<CancellationToken>)null!, out _)
            );

            // Send(Action)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Send((Action)null!));
            Assert.Throws<ArgumentNullException>(() => jobQueue.Send((Action)null!, out _));

            // Send<T>(Func<CancellationToken, T>)
            Assert.Throws<ArgumentNullException>(() =>
                jobQueue.Send((Func<CancellationToken, int>)null!, out _)
            );

            // Send<T>(Func<T>)
            Assert.Throws<ArgumentNullException>(() => jobQueue.Send((Func<int>)null!, out _));

            // SendAsync
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await jobQueue.SendAsync((Action<CancellationToken>)null!)
            );
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await jobQueue.SendAsync((Action)null!)
            );
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await jobQueue.SendAsync((Func<CancellationToken, int>)null!)
            );
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await jobQueue.SendAsync((Func<int>)null!)
            );
        }
    }
}
