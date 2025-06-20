using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Forkleans.Concurrency;
using Forkleans.Providers;
using Forkleans.Timers;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    [Serializable]
    [GenerateSerializer]
    public class SimpleGenericGrainState<T>
    {
        [Id(0)]
        public T A { get; set; }
        [Id(1)]
        public T B { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class SimpleGenericGrain1<T> : Grain<SimpleGenericGrainState<T>>, ISimpleGenericGrain1<T>
    {
        public Task<T> GetA()
        {
            return Task.FromResult(State.A);
        }

        public Task SetA(T a)
        {
            State.A = a;
            return Task.CompletedTask;
        }

        public Task SetB(T b)
        {
            State.B = b;
            return Task.CompletedTask;
        }

        public Task<string> GetAxB()
        {
            string retValue = string.Format("{0}x{1}", State.A, State.B);
            return Task.FromResult(retValue);
        }

        public Task<string> GetAxB(T a, T b)
        {
            string retValue = string.Format("{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }
    }

    [StorageProvider(ProviderName = "AzureStore")]
    public class SimpleGenericGrainUsingAzureStorageAndLongGrainName<T> : Grain<SimpleGenericGrainState<T>>, ISimpleGenericGrainUsingAzureStorageAndLongGrainName<T>
    {
        public async Task<T> EchoAsync(T entity)
        {
            State.A = entity;
            await WriteStateAsync();
            return entity;
        }

        public async Task ClearState()
        {
            await ClearStateAsync();
        }
    }

    [StorageProvider(ProviderName = "AzureStore")]
    public class TinyNameGrain<T> : Grain<SimpleGenericGrainState<T>>, ITinyNameGrain<T>
    {
        public async Task<T> EchoAsync(T entity)
        {
            State.A = entity;
            await WriteStateAsync();
            return entity;
        }

        public async Task ClearState()
        {
            await ClearStateAsync();
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class SimpleGenericGrainUState<U>
    {
        [Id(0)]
        public U A { get; set; }
        [Id(1)]
        public U B { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class SimpleGenericGrainU<U> : Grain<SimpleGenericGrainUState<U>>, ISimpleGenericGrainU<U>
    {
        public Task<U> GetA()
        {
            return Task.FromResult(State.A);
        }

        public Task SetA(U a)
        {
            State.A = a;
            return Task.CompletedTask;
        }

        public Task SetB(U b)
        {
            State.B = b;
            return Task.CompletedTask;
        }

        public Task<string> GetAxB()
        {
            string retValue = string.Format("{0}x{1}", State.A, State.B);
            return Task.FromResult(retValue);
        }

        public Task<string> GetAxB(U a, U b)
        {
            string retValue = string.Format("{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class SimpleGenericGrain2State<T, U>
    {
        [Id(0)]
        public T A { get; set; }
        [Id(1)]
        public U B { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class SimpleGenericGrain2<T, U> : Grain<SimpleGenericGrain2State<T, U>>, ISimpleGenericGrain2<T, U>
    {
        public Task<T> GetA()
        {
            return Task.FromResult(State.A);
        }

        public Task SetA(T a)
        {
            State.A = a;
            return Task.CompletedTask;
        }

        public Task SetB(U b)
        {
            State.B = b;
            return Task.CompletedTask;
        }

        public Task<string> GetAxB()
        {
            string retValue = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", State.A, State.B);
            return Task.FromResult(retValue);
        }

        public Task<string> GetAxB(T a, U b)
        {
            string retValue = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }
    }

    public class GenericGrainWithNoProperties<T> : Grain, IGenericGrainWithNoProperties<T>
    {
        public Task<string> GetAxB(T a, T b)
        {
            string retValue = string.Format("{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }
    }
    public class GrainWithNoProperties : Grain, IGrainWithNoProperties
    {
        public Task<string> GetAxB(int a, int b)
        {
            string retValue = string.Format("{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class IGrainWithListFieldsState
    {
        [Id(0)]
        public IList<string> Items { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class GrainWithListFields : Grain<IGrainWithListFieldsState>, IGrainWithListFields
    {
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (State.Items == null)
                State.Items = new List<string>();
            return base.OnActivateAsync(cancellationToken);
        }

        public Task AddItem(string item)
        {
            State.Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<IList<string>> GetItems()
        {
            return Task.FromResult((State.Items));
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericGrainWithListFieldsState<T>
    {
        [Id(0)]
        public IList<T> Items { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class GenericGrainWithListFields<T> : Grain<GenericGrainWithListFieldsState<T>>, IGenericGrainWithListFields<T>
    {
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (State.Items == null)
                State.Items = new List<T>();

            return base.OnActivateAsync(cancellationToken);
        }

        public Task AddItem(T item)
        {
            State.Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<IList<T>> GetItems()
        {
            return Task.FromResult(State.Items);
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericReaderWriterState<T>
    {
        [Id(0)]
        public T Value { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericReader2State<TOne, TTwo>
    {
        [Id(0)]
        public TOne Value1 { get; set; }
        [Id(1)]
        public TTwo Value2 { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericReaderWriterGrain2State<TOne, TTwo>
    {
        [Id(0)]
        public TOne Value1 { get; set; }
        [Id(1)]
        public TTwo Value2 { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class GenericReader3State<TOne, TTwo, TThree>
    {
        [Id(0)]
        public TOne Value1 { get; set; }
        [Id(1)]
        public TTwo Value2 { get; set; }
        [Id(2)]
        public TThree Value3 { get; set; }
    }


    [StorageProvider(ProviderName = "MemoryStore")]
    public class GenericReaderWriterGrain1<T> : Grain<GenericReaderWriterState<T>>, IGenericReaderWriterGrain1<T>
    {
        public Task SetValue(T value)
        {
            State.Value = value;
            return Task.CompletedTask;
        }

        public Task<T> GetValue()
        {
            return Task.FromResult(State.Value);
        }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class GenericReaderWriterGrain2<TOne, TTwo> : Grain<GenericReaderWriterGrain2State<TOne, TTwo>>, IGenericReaderWriterGrain2<TOne, TTwo>
    {
        public Task SetValue1(TOne value)
        {
            State.Value1 = value;
            return Task.CompletedTask;
        }
        public Task SetValue2(TTwo value)
        {
            State.Value2 = value;
            return Task.CompletedTask;
        }

        public Task<TOne> GetValue1()
        {
            return Task.FromResult(State.Value1);
        }

        public Task<TTwo> GetValue2()
        {
            return Task.FromResult(State.Value2);
        }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public class GenericReaderWriterGrain3<TOne, TTwo, TThree> : Grain<GenericReader3State<TOne, TTwo, TThree>>, IGenericReaderWriterGrain3<TOne, TTwo, TThree>
    {
        public Task SetValue1(TOne value)
        {
            State.Value1 = value;
            return Task.CompletedTask;
        }
        public Task SetValue2(TTwo value)
        {
            State.Value2 = value;
            return Task.CompletedTask;
        }
        public Task SetValue3(TThree value)
        {
            State.Value3 = value;
            return Task.CompletedTask;
        }

        public Task<TThree> GetValue3()
        {
            return Task.FromResult(State.Value3);
        }

        public Task<TOne> GetValue1()
        {
            return Task.FromResult(State.Value1);
        }

        public Task<TTwo> GetValue2()
        {
            return Task.FromResult(State.Value2);
        }
    }

    public class BasicGenericGrain<T, U> : Grain, IBasicGenericGrain<T, U>
    {
        private T _a;
        private U _b;

        public Task<T> GetA()
        {
            return Task.FromResult(_a);
        }

        public Task<string> GetAxB()
        {
            string retValue = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", _a, _b);
            return Task.FromResult(retValue);
        }

        public Task<string> GetAxB(T a, U b)
        {
            string retValue = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", a, b);
            return Task.FromResult(retValue);
        }

        public Task SetA(T a)
        {
            this._a = a;
            return Task.CompletedTask;
        }

        public Task SetB(U b)
        {
            this._b = b;
            return Task.CompletedTask;
        }
    }

    public class HubGrain<TKey, T1, T2> : Grain, IHubGrain<TKey, T1, T2>
    {
        public virtual Task Bar(TKey key, T1 message1, T2 message2)
        {
            throw new System.NotImplementedException();
        }
    }

    public class EchoHubGrain<TKey, TMessage> : HubGrain<TKey, TMessage, TMessage>, IEchoHubGrain<TKey, TMessage>
    {
        private int _x;

        public Task Foo(TKey key, TMessage message, int x)
        {
            _x = x;
            return Task.CompletedTask;
        }

        public override Task Bar(TKey key, TMessage message1, TMessage message2)
        {
            return Task.CompletedTask;
        }

        public Task<int> GetX()
        {
            return Task.FromResult(_x);
        }
    }

    public class EchoGenericChainGrain<T> : Grain, IEchoGenericChainGrain<T>
    {
        public async Task<T> Echo(T item)
        {
            long pk = this.GetPrimaryKeyLong();
            var otherGrain = GrainFactory.GetGrain<ISimpleGenericGrain1<T>>(pk);
            await otherGrain.SetA(item);
            return await otherGrain.GetA();
        }

        public async Task<T> Echo2(T item)
        {
            long pk = this.GetPrimaryKeyLong() + 1;
            var otherGrain = GrainFactory.GetGrain<IEchoGenericChainGrain<T>>(pk);
            return await otherGrain.Echo(item);
        }

        public async Task<T> Echo3(T item)
        {
            long pk = this.GetPrimaryKeyLong() + 1;
            var otherGrain = GrainFactory.GetGrain<IEchoGenericChainGrain<T>>(pk);
            return await otherGrain.Echo2(item);
        }

        public async Task<T> Echo4(T item)
        {
            long pk = this.GetPrimaryKeyLong() + 1;
            var otherGrain = GrainFactory.GetGrain<ISimpleGenericGrain1<T>>(pk);
            await otherGrain.SetA(item);
            return await otherGrain.GetA();
        }

        public async Task<T> Echo5(T item)
        {
            long pk = this.GetPrimaryKeyLong() + 1;
            var otherGrain = GrainFactory.GetGrain<IEchoGenericChainGrain<T>>(pk);
            return await otherGrain.Echo4(item);
        }

        public async Task<T> Echo6(T item)
        {
            long pk = this.GetPrimaryKeyLong() + 1;
            var otherGrain = GrainFactory.GetGrain<IEchoGenericChainGrain<T>>(pk);
            return await otherGrain.Echo5(item);
        }
    }

    public class NonGenericBaseGrain : Grain, INonGenericBase
    {
        public Task Ping()
        {
            return Task.CompletedTask;
        }
    }

    public class Generic1ArgumentGrain<T> : NonGenericBaseGrain, IGeneric1Argument<T>
    {
        public Task<T> Ping(T t)
        {
            return Task.FromResult(t);
        }
    }

    public class Generic1ArgumentDerivedGrain<T> : NonGenericBaseGrain, IGeneric1Argument<T>
    {
        public Task<T> Ping(T t)
        {
            return Task.FromResult(t);
        }
    }

    public class Generic2ArgumentGrain<T, U> : Grain, IGeneric2Arguments<T, U>
    {
        public Task<Tuple<T, U>> Ping(T t, U u)
        {
            return Task.FromResult(new Tuple<T, U>(t, u));
        }

        public Task Ping()
        {
            return Task.CompletedTask;
        }
    }

    public class Generic2ArgumentsDerivedGrain<T, U> : NonGenericBaseGrain, IGeneric2Arguments<T, U>
    {
        public Task<Tuple<T, U>> Ping(T t, U u)
        {
            return Task.FromResult(new Tuple<T, U>(t, u));
        }
    }

    public class DbGrain<T> : Grain, IDbGrain<T>
    {
        private T _value;

        public Task SetValue(T value)
        {
            _value = value;
            return Task.CompletedTask;
        }

        public Task<T> GetValue()
        {
            return Task.FromResult(_value);
        }
    }

    [Reentrant]
    public class PingSelfGrain<T> : IGrainBase, IGenericPingSelf<T>
    {
        private readonly ILogger logger;
        private T _lastValue;
        private readonly ITimerRegistry _timerRegistry;

        public PingSelfGrain(ILogger<PingSelfGrain<T>> logger, IGrainContext context, ITimerRegistry timerRegistry)
        {
            this.logger = logger;
            this.GrainContext = context;
            _timerRegistry = timerRegistry;
        }

        public IGrainContext GrainContext { get; set; }

        public Task<T> Ping(T t)
        {
            _lastValue = t;
            return Task.FromResult(t);
        }

        public Task<T> PingOther(IGenericPingSelf<T> target, T t)
        {
            return target.Ping(t);
        }


        public Task<T> PingSelf(T t)
        {
            return PingOther(this, t);
        }


        public Task<T> PingSelfThroughOther(IGenericPingSelf<T> target, T t)
        {
            return target.PingOther(this, t);
        }

        public Task ScheduleDelayedPing(IGenericPingSelf<T> target, T t, TimeSpan delay)
        {
            _timerRegistry.RegisterGrainTimer<object>(
                GrainContext,
                (_, cancellationToken) =>
                {
                    this.logger.LogDebug("***Timer fired for pinging {0}***", target.GetPrimaryKey());
                    return target.Ping(t);
                },
                null,
                new() { DueTime = delay, Period = Timeout.InfiniteTimeSpan });
            return Task.CompletedTask;
        }

        public Task<T> GetLastValue()
        {
            return Task.FromResult(_lastValue);
        }

        public async Task ScheduleDelayedPingToSelfAndDeactivate(IGenericPingSelf<T> target, T t, TimeSpan delay)
        {
            await target.ScheduleDelayedPing(this, t, delay);
            this.DeactivateOnIdle();
        }

        public Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("***Activating*** {0}", this.GetPrimaryKey());
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(DeactivationReason deactivationReason, CancellationToken cancellationToken)
        {
            this.logger.LogDebug("***Deactivating*** {0}", this.GetPrimaryKey());
            return Task.CompletedTask;
        }
    }

    public class LongRunningTaskGrain<T> : Grain, ILongRunningTaskGrain<T>
    {
        private readonly Channel<(Guid CallId, Exception Error)> _cancelledCalls = Channel.CreateUnbounded<(Guid, Exception)>();
        private T lastValue;

        public async Task GrainCancellationTokenCallbackThrow(GrainCancellationToken ct, Guid callId)
        {
            ct.CancellationToken.Register(() =>
            {
                _cancelledCalls.Writer.TryWrite((callId, null));
                throw new InvalidOperationException("From cancellation token callback");
            });

            await Task.Delay(TimeSpan.FromSeconds(10), ct.CancellationToken);
        }

        public async Task CancellationTokenCallbackThrow(CancellationToken ct, Guid callId)
        {
            ct.Register(() =>
            {
                _cancelledCalls.Writer.TryWrite((callId, null));
                throw new InvalidOperationException("From cancellation token callback");
            });

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        public Task<T> GetLastValue()
        {
            return Task.FromResult(lastValue);
        }

        public async Task<bool> CallOtherCancellationTokenCallbackResolve(ILongRunningTaskGrain<T> target, Guid callId)
        {
            using var cts = new CancellationTokenSource();
            var grainTask = target.CancellationTokenCallbackResolve(cts.Token, callId);
            cts.CancelAfter(300);
            return await grainTask;
        }

        public async Task<bool> CallOtherGrainCancellationTokenCallbackResolve(ILongRunningTaskGrain<T> target, Guid callId)
        {
            using var cts = new GrainCancellationTokenSource();
            var grainTask = target.GrainCancellationTokenCallbackResolve(cts.Token, callId);
            await Task.Delay(300);
            await cts.Cancel();
            return await grainTask;
        }

        public Task<bool> GrainCancellationTokenCallbackResolve(GrainCancellationToken tc, Guid callId)
        {
            var tcs = new TaskCompletionSource<bool>();
            var orleansTs = TaskScheduler.Current;
            tc.CancellationToken.Register(() =>
            {
                if (TaskScheduler.Current != orleansTs)
                {
                    var exception = new Exception("Callback executed on wrong thread");
                    _cancelledCalls.Writer.TryWrite((callId, exception));
                    tcs.SetException(exception);
                }
                else
                {
                    _cancelledCalls.Writer.TryWrite((callId, null));
                    tcs.SetResult(true);
                }
            });

            return tcs.Task;
        }

        public Task<bool> CancellationTokenCallbackResolve(CancellationToken tc, Guid callId)
        {
            var tcs = new TaskCompletionSource<bool>();
            var orleansTs = TaskScheduler.Current;
            tc.Register(() =>
            {
                if (TaskScheduler.Current != orleansTs)
                {
                    var exception = new Exception("Callback executed on wrong thread");
                    _cancelledCalls.Writer.TryWrite((callId, exception));
                    tcs.SetException(exception);
                }
                else
                {
                    _cancelledCalls.Writer.TryWrite((callId, null));
                    tcs.SetResult(true);
                }
            });

            return tcs.Task;
        }

        public async Task<T> CallOtherLongRunningTask(ILongRunningTaskGrain<T> target, T t, TimeSpan delay)
        {
            return await target.LongRunningTask(t, delay);
        }

        public async Task<T> FanOutOtherLongRunningTask(ILongRunningTaskGrain<T> target, T t, TimeSpan delay, int degreeOfParallelism)
        {
            var promises = Enumerable
                .Range(0, degreeOfParallelism)
                .Select(_ => target.LongRunningTask(t, delay))
                .ToList();

            await Task.WhenAll(promises);
            return t;
        }

        public async Task CallOtherLongRunningTaskGrainCancellation(ILongRunningTaskGrain<T> target, GrainCancellationToken tc, TimeSpan delay, Guid callId)
        {
            await target.LongWaitGrainCancellation(tc, delay, callId);
        }

        public async Task CallOtherLongRunningTask(ILongRunningTaskGrain<T> target, CancellationToken tc, TimeSpan delay, Guid callId)
        {
            await target.LongWait(tc, delay, callId);
        }

        public async Task CallOtherLongRunningTaskWithLocalCancellation(ILongRunningTaskGrain<T> target, TimeSpan delay, TimeSpan delayBeforeCancel, Guid callId)
        {
            using var cts = new CancellationTokenSource();
            var task = target.LongWait(cts.Token, delay, callId);
            cts.CancelAfter(delayBeforeCancel);
            await task;
        }

        public async Task CallOtherLongRunningTaskWithLocalGrainCancellationToken(ILongRunningTaskGrain<T> target, TimeSpan delay, TimeSpan delayBeforeCancel, Guid callId)
        {
            using var cts = new GrainCancellationTokenSource();
            var task = target.LongWaitGrainCancellation(cts.Token, delay, callId);
            await Task.Delay(delayBeforeCancel);
            await cts.Cancel();
            await task;
        }

        public Task LongWaitGrainCancellationInterleaving(GrainCancellationToken tc, TimeSpan delay, Guid callId) => LongWaitGrainCancellation(tc, delay, callId);

        public async Task LongWaitGrainCancellation(GrainCancellationToken ct, TimeSpan delay, Guid callId)
        {
            try
            {
                await Task.Delay(delay, ct.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelledCalls.Writer.TryWrite((callId, null));
                throw;
            }
        }

        public Task LongWaitInterleaving(CancellationToken ct, TimeSpan delay, Guid callId) => LongWait(ct, delay, callId);
        public async Task LongWait(CancellationToken ct, TimeSpan delay, Guid callId)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                _cancelledCalls.Writer.TryWrite((callId, null));
                throw;
            }
        }

        public async Task<T> LongRunningTask(T t, TimeSpan delay)
        {
            await Task.Delay(delay);
            this.lastValue = t;
            return await Task.FromResult(t);
        }

        public Task<string> GetRuntimeInstanceId()
        {
            return Task.FromResult(RuntimeIdentity);
        }

        public async Task<string> GetRuntimeInstanceIdWithDelay(TimeSpan delay)
        {
            await Task.Delay(delay);
            return RuntimeIdentity;
        }

        public async IAsyncEnumerable<(Guid CallId, Exception Error)> WatchCancellations([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in _cancelledCalls.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
    }

    public class GenericGrainWithContraints<A, B, C>: Grain, IGenericGrainWithConstraints<A, B, C>
        where A : ICollection<B>, new()
        where B : struct
        where C : class
    {
        private A collection;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            collection = new A();
            return Task.CompletedTask;
        }

        [Alias("GenericGrainWithConstraints.GetCount")]
        public Task<int> GetCount() { return Task.FromResult(collection.Count); }

        public Task Add(B item)
        {
            collection.Add(item);
            return Task.CompletedTask;
        }

        public Task<C> RoundTrip(C value)
        {
            return Task.FromResult(value);
        }
    }

    public class NonGenericCastableGrain : Grain, INonGenericCastableGrain, ISomeGenericGrain<string>, IIndependentlyConcretizedGenericGrain<string>, IIndependentlyConcretizedGrain
    {
        public Task DoSomething() {
            return Task.CompletedTask;
        }

        public Task<string> Hello() {
            return Task.FromResult("Hello!");
        }
    }

    public class GenericCastableGrain<T> : Grain, IGenericCastableGrain<T>, INonGenericCastGrain
    {
        public Task<string> Hello() {
            return Task.FromResult("Hello!");
        }
    }

    public class GenericArrayRegisterGrain<T> : Grain, IGenericArrayRegisterGrain<T>
    {
        private T[] _value;
        public Task<T[]> Get() => Task.FromResult(_value);
        public Task Set(T[] value)
        {
            _value = value;
            return Task.CompletedTask;
        }
    }

    public class IndependentlyConcretizedGenericGrain : Grain, IIndependentlyConcretizedGenericGrain<string>, IIndependentlyConcretizedGrain
    {
        public Task<string> Hello() => Task.FromResult("I have been independently concretized!");
    }

    public interface IReducer<TState, TAction>
    {
        Task<TState> Handle(TState prevState, TAction act);
    }

    [Serializable]
    [GenerateSerializer]
    public class Reducer1Action { }

    [Serializable]
    [GenerateSerializer]
    public class Reducer2Action { }

    public class Reducer1 : IReducer<string, Reducer1Action>
    {
        public Task<string> Handle(string prevState, Reducer1Action act) => Task.FromResult(prevState + act);
    }

    public class Reducer2 : IReducer<int, Reducer2Action>
    {
        public Task<int> Handle(int prevState, Reducer2Action act) => Task.FromResult(prevState + act.ToString().Length);
    }

    public interface IUnmanagedArgGrain<T> : IGrainWithGuidKey where T : unmanaged
    {
        ValueTask<T> Echo(T value);
        ValueTask<U> EchoNonNullable<U>(U value) where U : notnull;
        ValueTask<U> EchoReference<U>(U value) where U : class;
        ValueTask<U> EchoValue<U>(U value) where U : struct;
    }

    public class UnmanagedArgGrain<T> : IUnmanagedArgGrain<T> where T : unmanaged
    {
        public ValueTask<T> Echo(T value) => new(value);
        public ValueTask<U> EchoNonNullable<U>(U value)  where U : notnull => new(value);
        public ValueTask<U> EchoReference<U>(U value) where U : class => new(value);
        public ValueTask<U> EchoValue<U>(U value) where U : struct => new(value);
    }

    public interface IReducerGameGrain<TState, TAction> : IGrainWithStringKey
    {
        Task<TState> Go(TState prevState, TAction act);
    }

    public class ReducerGameGrain<TState, TAction> : Grain, IReducerGameGrain<TState, TAction>
    {
        private readonly IReducer<TState, TAction> reducer;

        public ReducerGameGrain(IReducer<TState, TAction> reducer)
        {
            this.reducer = reducer;
        }

        public Task<TState> Go(TState prevState, TAction act) => this.reducer.Handle(prevState, act);
    }

    namespace Generic.EdgeCases
    {
        using System.Linq;
        using UnitTests.GrainInterfaces.Generic.EdgeCases;

        public abstract class BasicGrain : Grain
        {
            public Task<string> Hello()
            {
                return Task.FromResult("Hello!");
            }

            public Task<string[]> ConcreteGenArgTypeNames()
            {
                var grainType = GetImmediateSubclass(this.GetType());
                return Task.FromResult(grainType.GetGenericArguments().Select(t => t.FullName).ToArray());
            }

            private Type GetImmediateSubclass(Type subject)
            {
                if(subject.BaseType == typeof(BasicGrain))
                {
                    return subject;
                }

                return GetImmediateSubclass(subject.BaseType);
            }
        }

        public class PartiallySpecifyingGrain<T> : BasicGrain, IGrainWithTwoGenArgs<string, T>
        { }

        public class GrainWithPartiallySpecifyingInterface<T> : BasicGrain, IPartiallySpecifyingInterface<T>
        { }

        public class GrainSpecifyingSameGenArgTwice<T> : BasicGrain, IGrainReceivingRepeatedGenArgs<T, T>
        { }

        public class SpecifyingRepeatedGenArgsAmongstOthers<T1, T2> : BasicGrain, IReceivingRepeatedGenArgsAmongstOthers<T2, T1, T2>
        { }

        public class GrainForTestingCastingBetweenInterfacesWithReusedGenArgs : BasicGrain, ISpecifyingGenArgsRepeatedlyToParentInterface<bool>
        { }

        public class SpecifyingSameGenArgsButRearranged<T1, T2> : BasicGrain, IReceivingRearrangedGenArgs<T2, T1>
        { }

        public class GrainForTestingCastingWithRearrangedGenArgs<T1, T2> : BasicGrain, ISpecifyingRearrangedGenArgsToParentInterface<T1, T2>
        { }

        public class GrainWithGenArgsUnrelatedToFullySpecifiedGenericInterface<T1, T2> : BasicGrain, IArbitraryInterface<T1, T2>, IInterfaceUnrelatedToConcreteGenArgs<float>
        { }

        public class GrainSupplyingFurtherSpecializedGenArg<T> : BasicGrain, IInterfaceTakingFurtherSpecializedGenArg<List<T>>
        { }

        public class GrainSupplyingGenArgSpecializedIntoArray<T> : BasicGrain, IInterfaceTakingFurtherSpecializedGenArg<T[]>
        { }

        public class GrainForCastingBetweenInterfacesOfFurtherSpecializedGenArgs<T>
            : BasicGrain, IAnotherReceivingFurtherSpecializedGenArg<List<T>>, IYetOneMoreReceivingFurtherSpecializedGenArg<T[]>
        { }
    }
}
