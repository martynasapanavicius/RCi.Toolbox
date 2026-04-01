using System;
using System.Threading.Tasks;
using RCi.Toolbox.Boxes;

namespace RCi.Toolbox.Tests.Boxes
{
    [Parallelizable(ParallelScope.All)]
    public static class SyncBoxTests
    {
        [Test]
        public static void Ctor_InitValue_FuncEquals() => AssertCtor(true, true);

        [Test]
        public static void Ctor_InitValue() => AssertCtor(true, false);

        [Test]
        public static void Ctor_FuncEquals() => AssertCtor(false, true);

        [Test]
        public static void Ctor() => AssertCtor(false, false);

        private static void AssertCtor(bool useInitValue, bool useFuncEquals)
        {
            var initValue = useInitValue ? 123 : 0;

            // provide funcEquals
            var funcEqualsCounter = 0;
            var funcEqualsArgsLeft = 0;
            var funcEqualsArgsRight = 0;
            var funcEquals = new Func<int, int, bool>(
                (left, right) =>
                {
                    funcEqualsCounter++;
                    funcEqualsArgsLeft = left;
                    funcEqualsArgsRight = right;
                    return left == right;
                }
            );

            // call ctor
            SyncBox<int> actual;
            if (useInitValue)
            {
                actual = useFuncEquals ? new SyncBox<int>(123, funcEquals) : new SyncBox<int>(123);
            }
            else
            {
                actual = useFuncEquals ? new SyncBox<int>(0, funcEquals) : new SyncBox<int>(0);
            }

            // hook ValueChanged
            var valueChangedCounter = 0;
            var valueChangedLastSender = default(object);
            var valueChangedLastValue = 0;
            actual.ValueChanged += (sender, newValue) =>
            {
                valueChangedCounter++;
                valueChangedLastSender = sender;
                valueChangedLastValue = newValue;
            };

            // check if seeding initial value works
            Assert.That(actual.Value, Is.EqualTo(initValue));

            if (useFuncEquals)
            {
                // make sure funcEquals wasn't invoked on ctor
                Assert.That(funcEqualsCounter, Is.EqualTo(0));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(0));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(0));
            }

            // make sure ValueChanged wasn't invoked on ctor
            Assert.That(valueChangedCounter, Is.EqualTo(0));
            Assert.That(valueChangedLastSender, Is.Null);
            Assert.That(valueChangedLastValue, Is.EqualTo(0));

            // set new value
            actual.Value = 456;

            // ensure value is set
            Assert.That(actual.Value, Is.EqualTo(456));

            if (useFuncEquals)
            {
                // ensure equality check works
                Assert.That(funcEqualsCounter, Is.EqualTo(1));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(initValue));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(456));
            }

            // ensure ValueChanged fired
            Assert.That(valueChangedCounter, Is.EqualTo(1));
            Assert.That(ReferenceEquals(actual, valueChangedLastSender));
            Assert.That(valueChangedLastValue, Is.EqualTo(456));

            // set to the same value
            actual.Value = 456;

            if (useFuncEquals)
            {
                // ensure equality check was invoked
                Assert.That(funcEqualsCounter, Is.EqualTo(2));
                Assert.That(funcEqualsArgsLeft, Is.EqualTo(456));
                Assert.That(funcEqualsArgsRight, Is.EqualTo(456));
            }

            // ensure ValueChanged wasn't fired
            Assert.That(valueChangedCounter, Is.EqualTo(1));
            Assert.That(ReferenceEquals(actual, valueChangedLastSender));
            Assert.That(valueChangedLastValue, Is.EqualTo(456));
        }

        [Test]
        public static void GenericParameters()
        {
            var valueType = new SyncBox<int>(0);
            valueType.Value = 1;
            //valueType.Value = null; // <--- compile error

            var valueTypeNullable = new SyncBox<int?>(null);
            valueTypeNullable.Value = 1;
            valueTypeNullable.Value = null;

            var referenceType = new SyncBox<object>(null!);
            referenceType.Value = new object();
            //referenceType.Value = null;  // <--- compile warning

            var referenceTypeNullable = new SyncBox<object?>(null);
            referenceTypeNullable.Value = new object();
            referenceTypeNullable.Value = null;
        }

        [Test]
        public static void ImplicitOperator()
        {
            var actual = new SyncBox<int>(123);
            int value = actual;
            Assert.That(value, Is.EqualTo(123));
        }

        [Test]
        public static void ToString_()
        {
            var actual = new SyncBox<int>(123);
            Assert.That(actual.ToString(), Is.EqualTo("123"));
        }

        [Test]
        public static void AccessLocked_ReadWriteAccessLockedDelegate()
        {
            AssertAccessLocked(sync =>
            {
                sync.AccessLocked(
                    (getter, setter) =>
                    {
                        var valueSet = Random.Shared.Next();
                        setter(valueSet);
                        var valueGet = getter();
                        Assert.That(valueGet, Is.EqualTo(valueSet));
                    }
                );
            });
        }

        [Test]
        public static void AccessLocked_ReadWriteAccessLockedDelegate_TResult()
        {
            AssertAccessLocked(sync =>
            {
                var valueSet = Random.Shared.Next();
                var valueGet = sync.AccessLocked(
                    (getter, setter) =>
                    {
                        setter(valueSet);
                        return getter();
                    }
                );
                Assert.That(valueGet, Is.EqualTo(valueSet));
            });
        }

        [Test]
        public static void AccessLocked_ReadOnlyAccessLockedDelegate()
        {
            AssertAccessLocked(sync =>
            {
                sync.AccessLocked(getter =>
                {
                    var valueSet = Random.Shared.Next();
                    sync.Value = valueSet;
                    var valueGet = getter();
                    Assert.That(valueGet, Is.EqualTo(valueSet));
                });
            });
        }

        [Test]
        public static void AccessLocked_ReadOnlyAccessLockedDelegate_TResult()
        {
            AssertAccessLocked(sync =>
            {
                var valueSet = Random.Shared.Next();
                var valueGet = sync.AccessLocked(getter =>
                {
                    sync.Value = valueSet;
                    return getter();
                });
                Assert.That(valueGet, Is.EqualTo(valueSet));
            });
        }

        private static void AssertAccessLocked(Action<SyncBox<long>> actionAssert)
        {
            var sw = ValueStopwatch.StartNew();
            var threadsTimeout = TimeSpan.FromSeconds(1);
            var box = new SyncBox<long>(0);
            var tasks = new Task[10];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    while (true)
                    {
                        if (sw.Elapsed > threadsTimeout)
                        {
                            return;
                        }
                        actionAssert(box);
                    }
                });
            }
            Task.WaitAll(tasks);
        }
    }
}
