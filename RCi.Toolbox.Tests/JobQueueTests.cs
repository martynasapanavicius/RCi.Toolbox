using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace RCi.Toolbox.Tests
{
    [Parallelizable]
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
        public static void Ctor_Throws_WorkerCount_Zero()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new JobQueue(new JobQueueParameters
                {
                    WorkerCount = 0,
                });
            });
        }

        [Test]
        public static void Ctor_Throws_WorkerCount_Negative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new JobQueue(new JobQueueParameters
                {
                    WorkerCount = -1,
                });
            });
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
            jobQueue.Post(() =>
            {
                lock (locker)
                {
                    jobStarted = true;
                }

                throw new Exception("test");
            }, r =>
            {
                lock (locker)
                {
                    result = r;
                }
            });
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

            jobQueue.Post(ct =>
            {
                cancellationTokenStates.Enqueue(ct.IsCancellationRequested); // should be false

                // unblock outer thread
                jobStarted.Set();

                // block job and wait for signal
                waitForCancel.WaitOne();

                cancellationTokenStates.Enqueue(ct.IsCancellationRequested); // should be true
            }, results.Enqueue);

            // wait for thread to start (so we capture cancellation before)
            jobStarted.WaitOne();

            // cancel job queue
            jobQueue.Cancel();

            // unblock job (so we can capture flipped cancellation token)
            waitForCancel.Set();

            jobQueue.WaitForIdle();
            Assert.That(cancellationTokenStates.ToArray().SequenceEqual([false, true]));
            Assert.That(results.ToArray().SequenceEqual([new JobResult(true, default)]));
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
        public static void IsWorking()
        {
            using var jobQueue = new JobQueue();

            var before = jobQueue.IsWorking;
            Assert.That(before, Is.False);

            using var jobsCanExecute = new ManualResetEvent(false);
            jobQueue.Post(() =>
            {
                // block job and wait for signal
                jobsCanExecute.WaitOne();
            });

            var during = jobQueue.IsWorking;
            Assert.That(during, Is.True);

            // unblock job
            jobsCanExecute.Set();

            // wait for jobs to finish
            jobQueue.WaitForIdle();

            var after = jobQueue.IsWorking;
            Assert.That(after, Is.False);
        }

        [Test]
        public static void ActiveWorkerCount()
        {
            using var jobQueue = new JobQueue(new JobQueueParameters
            {
                WorkerCount = 2,
            });

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
                jobQueue.ActiveWorkerCountChanged += (_, count) => queue.Enqueue($"ActiveWorkerCountChanged = {count}");
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
            jobQueue.Post(ct =>
            {
                ct.ThrowIfCancellationRequested(); // should not throw
                return 42;
            }, results.Enqueue);
            jobQueue.WaitForIdle();
            Assert.That(results.ToArray().SequenceEqual([new JobResult<int>(false, default, 42)]));
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
            Assert.That(result.Result, Is.EqualTo(default(int)));
        }
    }
}
