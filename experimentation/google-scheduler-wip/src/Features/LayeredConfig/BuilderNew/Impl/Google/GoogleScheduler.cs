using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Google.Apis.Requests;
using Google.Apis.Services;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Helper;

namespace ScheduleLib.Application.Config.Google;

public sealed class ConcurrencyLimitOptions
{
    public required int ConcurrencyLimit { get; set; }
}

public sealed class Throttler : IDisposable
{
    public SemaphoreSlim Semaphore { get; }

    public Throttler(ConcurrencyLimitOptions opts)
    {
        Semaphore = new(
            initialCount: opts.ConcurrencyLimit,
            maxCount: opts.ConcurrencyLimit);
    }

    public void Dispose()
    {
        Semaphore.Dispose();
    }
}

public sealed class SchedulerOptions
{
    public required TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.MaxValue;
    public TimeSpan ProbeStartBackoff { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 1000;
    public int ConcurrencyLimit { get; set; } = 10;
}

public readonly record struct RequestId(string Value)
{
    public bool HasValue => Value != null;
}

internal enum ExecutionStatus
{
    Ok,
    RateLimited,
    IsComplete,
    LogicErrors,
}

internal readonly record struct ExecutionResult
{
    private ExecutionResult(
        int doneCount,
        EnumBitArray<ExecutionStatus> status)
    {
        DoneCount = doneCount;
        Status = status;
    }

    public int DoneCount { get; }
    public EnumBitArray<ExecutionStatus> Status { get; }

    public ExecutionStatus GetMostImportantStatus()
    {
        ReadOnlySpan<ExecutionStatus> importance =
        [
            ExecutionStatus.IsComplete,
            ExecutionStatus.LogicErrors,
            ExecutionStatus.RateLimited,
            ExecutionStatus.Ok,
        ];
        foreach (var x in importance)
        {
            if (Status.Contains(x))
            {
                return x;
            }
        }
        throw Unreachable();
    }

    public static ExecutionResult LogicErrors(
        int successCount,
        int logicErrorCount)
    {
        _ = logicErrorCount;
        Debug.Assert(successCount >= 0);
        Debug.Assert(logicErrorCount > 0);
        var arr = new EnumBitArray<ExecutionStatus>(ExecutionStatus.LogicErrors);
        if (successCount != 0)
        {
            arr.Set(ExecutionStatus.Ok);
        }
        return new(successCount, arr);
    }

    public static ExecutionResult RateLimited(
        int successCount,
        int logicErrorCount)
    {
        Debug.Assert(successCount >= 0);
        Debug.Assert(logicErrorCount >= 0);
        var arr = new EnumBitArray<ExecutionStatus>(ExecutionStatus.RateLimited);
        if (successCount != 0)
        {
            arr.Set(ExecutionStatus.Ok);
        }
        if (logicErrorCount != 0)
        {
            arr.Set(ExecutionStatus.LogicErrors);
        }
        return new(successCount, arr);
    }

    public static ExecutionResult Ok(
        int successCount,
        bool isComplete = false)
    {
        Debug.Assert(successCount > 0);

        var arr = new EnumBitArray<ExecutionStatus>(ExecutionStatus.Ok);
        if (isComplete)
        {
            arr.Set(ExecutionStatus.IsComplete);
        }
        return new(successCount, arr);
    }

    public static ExecutionResult Completed(
        int successCount)
    {
        return Ok(successCount, isComplete: true);
    }
}

internal readonly record struct ExecutionStartResult(
    Task<ExecutionResult> Task,
    int ExecutingCount);

internal delegate ExecutionStartResult ExecuteFunc(int maxCountToExecute);

internal sealed class ScheduledItem
{
    public ScheduledItem(
        ExecuteFunc execute,
        int totalCount)
    {
        _execute = execute;
        TotalCount = totalCount;
    }

    private ExecuteFunc _execute;
    public int TotalCount { get; set; }

    public bool IsDone => TotalCount == 0;

    public ExecutionStartResult Execute(int maxCountToExecute) => _execute(maxCountToExecute);
}

public readonly record struct Request<TRequestData>(
    RequestId Id,
    TRequestData RequestData);

public readonly record struct RequestBatch<TRequestData>(
    ImmutableArray<Request<TRequestData>> Items);

public readonly record struct RequestSpec<TClient, TRequestData, TResponse>
{
    public required Func<TClient, Request<TRequestData>, ClientServiceRequest<TResponse>> RequestFactory { get; init; }
    public required Func<TResponse, RequestId> ResponseIdGetter { get; init; }
}

// Should be awaitable
public readonly record struct ResponseHandle<TResponse>(
    Task<TResponse> Task);

// Should be awaitable
public readonly record struct BatchResponseHandle<TResponse>(
    Task<ImmutableArray<TResponse>> Task)
{
}

public readonly struct GoogleSchedulerUser<TClient> : IAsyncDisposable
    where TClient : IClientService
{
    private readonly GoogleScheduler _scheduler;
    private readonly TClient _client;
    private readonly CancellationToken _cancellationToken;

    public GoogleSchedulerUser(
        GoogleScheduler scheduler,
        TClient client,
        CancellationToken cancellationToken)
    {
        _scheduler = scheduler;
        _client = client;
        _cancellationToken = cancellationToken;
    }

    public ValueTask DisposeAsync()
    {
    }
}



public sealed class GoogleScheduler
{
    private readonly Throttler _throttler;
    private readonly Channel<ScheduledItem> _items;
    private readonly SchedulerOptions _options;
    private RunState _runState;

    public enum RunState
    {
        NotRunning,
        Running,
    }

    public GoogleScheduler(
        Throttler throttler,
        SchedulerOptions options)
    {
        _throttler = throttler;
        _items = Channel.CreateUnbounded<ScheduledItem>(new()
        {
            SingleReader = true,
            // For now true, but will be false eventually when this is reusable by multiple clients.
            SingleWriter = true,
            // AllowSynchronousContinuations = true,
        });
        _options = options;
        _runState = RunState.NotRunning;
    }

    public ResponseHandle<TResponse> Enqueue<TClient, TRequestData, TResponse>(
        RequestSpec<TClient, TRequestData, TResponse> spec,
        TClient client,
        Request<TRequestData> request,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<TResponse>();
        cancellationToken.Register(() => tcs.TrySetCanceled());
        var scheduledItem = new ScheduledItem((maxCount) =>
        {
            return new(Start1(), 1);

            async Task<ExecutionResult> Start1()
            {
                var r = spec.RequestFactory(client, request);
                var response = await r.ExecuteAsync(cancellationToken);
                tcs.SetResult(response);

                _ = maxCount;

                return ExecutionResult.Completed(successCount: 1);
            }
        }, totalCount: 1);
        _items.Writer.TryWrite(scheduledItem);
        return new(tcs.Task);
    }

    public BatchResponseHandle<TResponse> EnqueueBatch<TClient, TRequestData, TResponse>(
        RequestSpec<TClient, TRequestData, TResponse> spec,
        TClient client,
        RequestBatch<TRequestData> batch,
        CancellationToken cancellationToken)
        where TResponse : class
        where TClient : IClientService
    {
        var tcs = new TaskCompletionSource<ImmutableArray<TResponse>>();
        cancellationToken.Register(() => tcs.TrySetCanceled());

        var builder = ImmutableArray.CreateBuilder<TResponse?>();
        builder.SetExactSize(batch.Items.Length);
        void SetResult(int i, TResponse response)
        {
            Debug.Assert(builder[i] == null);
            builder[i] = response;
        }

        int pendingItemsStartIndex = 0;
        bool IsDone()
        {
            return pendingItemsStartIndex == batch.Items.Length;
        }
        void UpdatePending()
        {
            for (; pendingItemsStartIndex < batch.Items.Length; pendingItemsStartIndex++)
            {
                if (builder[pendingItemsStartIndex] == null)
                {
                    break;
                }
            }

            if (IsDone())
            {
                Debug.Assert(builder.Count != 0);
                tcs.SetResult(builder.MoveToImmutable()!);
            }
        }

        var scheduledItem = new ScheduledItem(
            totalCount: batch.Items.Length,
            execute: count =>
            {
                switch (count)
                {
                    case 1:
                    {
                        Debug.Assert(pendingItemsStartIndex < batch.Items.Length);
                        Debug.Assert(builder[pendingItemsStartIndex] == null);
                        var r = spec.RequestFactory(client, batch.Items[pendingItemsStartIndex]);
                        return new(Start1(), 1);

                        async Task<ExecutionResult> Start1()
                        {
                            var response = await r.ExecuteAsync(cancellationToken);
                            SetResult(pendingItemsStartIndex, response);
                            UpdatePending();
                            return new();
                        }
                    }
                    case > 1:
                    {
                        var batchRequest = new BatchRequest(client);
                        int successCount = 0;

                        void Queue(ClientServiceRequest<TResponse> req, int i1)
                        {
                            batchRequest.Queue(req, (TResponse content, RequestError error, int index, HttpResponseMessage message) =>
                            {
                                // TODO: handle errors and stuff
                                _ = error;
                                _ = index;
                                _ = message;

                                // TODO: Count the actual successful items.
                                successCount += 1;
                                SetResult(i1, content);
                            });
                        }

                        for (int i = pendingItemsStartIndex; i < batch.Items.Length; i++)
                        {
                            var existingResponse = builder[i];
                            if (existingResponse != null)
                            {
                                continue;
                            }

                            var item = batch.Items[i];
                            var r = spec.RequestFactory(client, item);
                            Queue(r, i);

                            if (batchRequest.Count >= count)
                            {
                                break;
                            }
                        }

                        return new(
                            Task: Start1(),
                            ExecutingCount: batchRequest.Count);

                        async Task<ExecutionResult> Start1()
                        {
                            await batchRequest.ExecuteAsync(cancellationToken);
                            UpdatePending();

                            return ExecutionResult.Ok(
                                successCount,
                                isComplete: IsDone());
                        }
                    }
                    default:
                    {
                        throw Unreachable();
                    }
                }
            });
        _items.Writer.TryWrite(scheduledItem);
        return new(tcs.Task);
    }

    private struct Delay(SchedulerOptions _opts)
    {
        private int _retryCount = 0;

        public void Reset() => this = new(_opts);

        public Task GetNextDelay(CancellationToken cancellationToken)
        {
            var delay = (1 << _retryCount) * _opts.InitialRetryDelay.Ticks;
            delay = Math.Min(delay, _opts.MaxRetryDelay.Ticks);
            _retryCount++;
            var ret = Task.Delay(TimeSpan.FromTicks(delay), cancellationToken);
            return ret;
        }
    }

    private readonly record struct RunningAttempt(
        ScheduledItem Item,
        Task<ExecutionResult> ExecutionTask,
        int ExecutingCount)
    {
        public static RunningAttempt Null => default;
        public bool IsNull => Item == null;
    }

    private struct GlobalThrottlerWaitContext : IDisposable
    {
        private readonly Throttler _throttler;
        public Task? WaitTask { get; private set; }
        private CancellationTokenSource? _waitCts;
        private readonly CancellationToken _outerCancellationToken;

        public GlobalThrottlerWaitContext(
            Throttler throttler,
            CancellationToken outerCancellationToken)
        {
            _throttler = throttler;
            _outerCancellationToken = outerCancellationToken;
        }

        public Task StartWait()
        {
            Debug.Assert(WaitTask is null);
            _waitCts ??= CancellationTokenSource.CreateLinkedTokenSource(_outerCancellationToken);
            var x = _throttler.Semaphore.WaitAsync(_waitCts.Token);
            WaitTask = x;
            return x;
        }

        public void CancelWait()
        {
            if (_waitCts is null)
            {
                return;
            }
            Debug.Assert(WaitTask != null);

            _waitCts.Cancel();
            if (WaitTask.IsCompletedSuccessfully)
            {
                _throttler.Semaphore.Release();
            }

            _waitCts.Dispose();
            _waitCts = null;

            WaitTask.Dispose();
            WaitTask = null;
        }

        public bool TryUseWait()
        {
            if (WaitTask is not { } w)
            {
                return false;
            }

            bool ok = w.IsCompletedSuccessfully;
            w.Dispose();
            WaitTask = null;
            return false;
        }

        public void Dispose()
        {
            if (TryUseWait())
            {
                _throttler.Semaphore.Release();
            }
        }
    }

    private sealed class ExecutionContext : IDisposable
    {
        public ExecutionState State;
        public Delay DelayProvider;
        public readonly List<RunningAttempt> RunningAttempts;
        // List since we don't care about the order.
        public readonly List<ScheduledItem> QueueToStart;
        public readonly List<Task> TasksForAwaiting;
        public int CurrentWorkingCount;
        public int AvailableWorkingCapacity => Self._options.BatchSize - CurrentWorkingCount;
        public Task<bool>? WaitReadTask;
        public readonly GoogleScheduler Self;
        public Channel<ScheduledItem> Items => Self._items;
        public readonly CancellationToken CancellationToken;
        public readonly CancellationTokenSource Cts;

        public GlobalThrottlerWaitContext ThrottlerContext;

        public bool HasWorkingCapacityForAnotherTask()
        {
            if (AvailableWorkingCapacity == 0)
            {
                return false;
            }
            if (Self._options.ConcurrencyLimit <= RunningAttempts.Count)
            {
                return false;
            }
            return true;
        }

        public bool CanStartAnotherTask()
        {
            if (!HasWorkingCapacityForAnotherTask())
            {
                return false;
            }
            if (Self._throttler.Semaphore.CurrentCount == 0)
            {
                return false;
            }
            return true;
        }

        public bool TryStartTask(ScheduledItem it)
        {
            if (CanStartAnotherTask())
            {
                if (!ThrottlerContext.TryUseWait())
                {
                    try
                    {
                        Self._throttler.Semaphore.Wait(TimeSpan.Zero);
                    }
                    catch
                    {
                        return false;
                    }
                }

                var startResult = it.Execute(this.AvailableWorkingCapacity);
                this.CurrentWorkingCount -= startResult.ExecutingCount;
                this.RunningAttempts.Add(
                    new(
                        it,
                        startResult.Task,
                        startResult.ExecutingCount));
                return true;
            }
            return false;
        }

        public ExecutionResult DoRunningCompletion(RunningAttempt running)
        {
            Debug.Assert(running.ExecutionTask.IsCompleted);
            this.CurrentWorkingCount -= running.ExecutingCount;

            // TODO: handle result
            var result = running.ExecutionTask.Result;
            running.ExecutionTask.Dispose();
            if (!result.Status.Contains(ExecutionStatus.IsComplete))
            {
                this.QueueToStart.Add(running.Item);
            }

            return result;
        }

        public ExecutionContext(GoogleScheduler self, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Self = self;
            var defaultSize = Math.Min(self._options.ConcurrencyLimit, 10);
            RunningAttempts = new(defaultSize);
            TasksForAwaiting = new(defaultSize + 1);
            State = ExecutionState.Start;
            DelayProvider = new Delay();
            CurrentWorkingCount = 0;
            WaitReadTask = null;
            QueueToStart = new();
            ThrottlerContext = new(self._throttler, cancellationToken);
            Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public void Cancel()
        {
            Cts.Cancel();
        }

        public ScheduledItem? PopOneOffStartQueue()
        {
            if (QueueToStart.Count == 0)
            {
                return null;
            }

            var next = QueueToStart[0];
            QueueToStart[0] = QueueToStart[^1];
            CollectionsMarshal.SetCount(QueueToStart, QueueToStart.Count - 1);
            return next;
        }

        public ValueTask<bool> WaitToRead()
        {
            if (WaitReadTask is { } waitTask)
            {
                if (!waitTask.IsCompleted)
                {
                    return new(waitTask);
                }
            }

            WaitReadTask = null;
            return Items.Writer.WaitToWriteAsync(CancellationToken);
        }

        public void Dispose()
        {
            WaitReadTask?.Dispose();
            ThrottlerContext.Dispose();
        }

        public bool IsDone()
        {
            if (this.Cts.IsCancellationRequested)
            {
                return true;
            }
            if (this.QueueToStart.Count != 0)
            {
                return false;
            }
            if (this.RunningAttempts.Count != 0)
            {
                return false;
            }
            if (this.Items.Reader.Completion.IsCompleted)
            {
                return true;
            }
            return false;
        }

        public async Task Execute()
        {
            while (true)
            {
                if (IsDone())
                {
                    return;
                }
                switch (this.State)
                {
                    case ExecutionState.Probe:
                    {
                        ScheduledItem? item = null;

                        Debug.Assert(this.RunningAttempts.Count == 0);

                        {
                            if (this.PopOneOffStartQueue() is { } item2)
                            {
                                item = item2;
                            }
                        }

                        if (item == null)
                        {
                            if (await this.WaitToRead())
                            {
                                return;
                            }

                            bool readNextItem = !this.Items.Reader.TryRead(out item);
                            Debug.Assert(readNextItem, "Someone else read the item, not allowed.");
                            Debug.Assert(item != null);
                            Debug.Assert(!item.IsDone, "Finished item should have been removed from the queue.");
                        }

                        while (true)
                        {
                            // Probe
                            ExecutionStatus importantStatus;

                            try
                            {
                                await this.Self._throttler.Semaphore.WaitAsync(this.CancellationToken);

                                // Minor issue:
                                // could potentially go over the configured concurrency
                                // if it's used by multiple clients higher than the global concurrency limit.
                                var result = await item.Execute(1).Task;
                                importantStatus = result.GetMostImportantStatus();
                            }
                            finally
                            {
                                this.Self._throttler.Semaphore.Release();
                            }

                            switch (importantStatus)
                            {
                                case ExecutionStatus.IsComplete:
                                {
                                    this.QueueToStart.Add(item);
                                    goto case ExecutionStatus.Ok;
                                }
                                case ExecutionStatus.Ok:
                                {
                                    // probe succeeded
                                    TransitionToFiringAtCap();
                                    break;
                                }
                                case ExecutionStatus.LogicErrors:
                                {
                                    this.Cancel();
                                    return;
                                }
                                case ExecutionStatus.RateLimited:
                                {
                                    await this.DelayProvider.GetNextDelay(this.CancellationToken);
                                    continue;
                                }
                            }
                            break;
                        }
                        break;
                    }
                    case ExecutionState.FireAtCap:
                    {
                        // start from existing queue
                        while (true)
                        {
                            Debug.Assert(this.CurrentWorkingCount <= this.Self._options.BatchSize);

                            var c = new RunningCompletionContext(this);
                            this.RunningAttempts.Rewrite(
                                ref c,
                                static (ref c, ref running) =>
                                {
                                    if (!running.ExecutionTask.IsCompleted)
                                    {
                                        return RewriteAction.Keep;
                                    }

                                    var result = c.Context.DoRunningCompletion(running);
                                    c.Update(result.Status);
                                    return RewriteAction.Remove;
                                });
                            if (c.RecordedStatuses.IsSet(ExecutionStatus.LogicErrors))
                            {
                                this.Cancel();
                                break;
                                // TODO: the rest.
                            }
                            else if (c.RecordedStatuses.IsSet(ExecutionStatus.RateLimited))
                            {
                                this.ThrottlerContext.CancelWait();
                                await TransitionToProbing();
                                continue;
                            }

                            // Start incomplete but once already started items
                            this.QueueToStart.Rewrite(
                                this,
                                static (context, ref it) =>
                                {
                                    if (context.TryStartTask(it))
                                    {
                                        return RewriteAction.Remove;
                                    }

                                    return RewriteAction.KeepThisAndAllOther;
                                });

                            // Start items off the queue
                            while (true)
                            {
                                if (!this.Items.Reader.TryRead(out var item))
                                {
                                    break;
                                }

                                if (!this.TryStartTask(item))
                                {
                                    this.QueueToStart.Add(item);
                                    break;
                                }
                            }

                            this.TasksForAwaiting.Clear();
                            foreach (var running in this.RunningAttempts)
                            {
                                this.TasksForAwaiting.Add(running.ExecutionTask);
                            }

                            if (this.HasWorkingCapacityForAnotherTask())
                            {
                                // If the issue is that there are no items in queue, just wait for the queue.
                                bool areItemsInQueue = this.Items.Reader.CanPeek;
                                if (!areItemsInQueue)
                                {
                                    this.WaitReadTask = this.WaitToRead().AsTask();
                                    this.TasksForAwaiting.Add(this.WaitReadTask);
                                }
                                // The issue is in the global throttler, try to wait for a slot there.
                                else
                                {
                                    var wait = this.ThrottlerContext.StartWait();
                                    this.TasksForAwaiting.Add(wait);
                                }
                            }

                            var completedTask = await Task.WhenAny(this.TasksForAwaiting);
                            _ = completedTask;
                        }
                        break;
                    }
                    default:
                    {
                        throw Unreachable();
                    }
                }
            }

            async Task TransitionToProbing()
            {
                // Probably wait for all running tasks to finish?
                // Put failed ones them into the start queue.
                // Change state.
                this.TasksForAwaiting.Clear();

                while (true)
                {
                    var countBefore = this.RunningAttempts.Count;
                    if (countBefore == 0)
                    {
                        break;
                    }

                    this.TasksForAwaiting.Clear();
                    try
                    {
                        this.RunningAttempts.Rewrite(
                            this,
                            static (context, ref running) =>
                            {
                                if (!running.ExecutionTask.IsCompleted)
                                {
                                    context.TasksForAwaiting.Add(running.ExecutionTask);
                                    return RewriteAction.Keep;
                                }

                                context.DoRunningCompletion(running);
                                return RewriteAction.Remove;
                            });
                    }
                    catch
                    {
                        throw Unreachable();
                    }

                    var countAfter = this.RunningAttempts.Count;
                    var countRemoved = countBefore - countAfter;
                    this.Self._throttler.Semaphore.Release(countRemoved);

                    await Task.WhenAny(this.TasksForAwaiting);
                }

                Debug.Assert(this.CurrentWorkingCount == 0);
                this.TasksForAwaiting.Clear();
                this.RunningAttempts.Clear();
                this.State = ExecutionState.Probe;

                await Task.Delay(
                    this.Self._options.ProbeStartBackoff,
                    this.CancellationToken);
            }

            void TransitionToFiringAtCap()
            {
                Debug.Assert(this.RunningAttempts.Count == 0);
                this.CurrentWorkingCount = 0;
                this.DelayProvider.Reset();
                this.State = ExecutionState.FireAtCap;
            }
        }
    }

    private struct RunningCompletionContext(ExecutionContext _context)
    {
        public readonly ExecutionContext Context = _context;
        public EnumBitArray<ExecutionStatus> RecordedStatuses = new();

        public void Update(EnumBitArray<ExecutionStatus> status)
        {
            RecordedStatuses = RecordedStatuses.UnionWith(status);
        }
    }

    public Task Start(CancellationToken cancellationToken)
    {
        {
            var prevState = Interlocked.CompareExchange(
                ref _runState,
                value: RunState.Running,
                comparand: RunState.NotRunning);
            if (prevState != RunState.NotRunning)
            {
                throw new InvalidOperationException("Scheduler cannot be started twice");
            }
        }

        // probe
        // probe succeeds -> launch at cap
        // cap rate limits -> probe
        // probe rate limits -> exp backoff for the probe
        using var context = new ExecutionContext(this, cancellationToken);
        var pump = Task.Run(context.Execute, cancellationToken);
        return pump;
    }

    private enum ExecutionState
    {
        Probe,
        FireAtCap,
        WaitingForItems,
        Start = WaitingForItems,
    }

    public void Dispose()
    {
    }
}

public enum RewriteAction
{
    Keep,
    Remove,
    KeepThisAndAllOther,
}

public delegate RewriteAction RefRewriteDelegate<TContext, T>(ref TContext context, ref T item)
    where TContext : allows ref struct;
public delegate RewriteAction RewriteDelegate<TContext, T>(TContext context, ref T item)
    where TContext : allows ref struct;

public static class RewriteHelper
{
    private readonly ref struct RefWrapper<T>
    {
        public readonly ref T Value;

        public RefWrapper(ref T value)
        {
            Value = ref value;
        }
    }

    private static class RefWrapper
    {
        public static RefWrapper<T> Create<T>(ref T item)
        {
            return new(ref item);
        }
    }

    private readonly ref struct RewriteRefHelper<TContext, T>
    {
        private readonly ref TContext _context;
        private readonly RefRewriteDelegate<TContext, T> _delegate;

        public RewriteRefHelper(
            ref TContext context,
            RefRewriteDelegate<TContext, T> @delegate)
        {
            _delegate = @delegate;
            _context = ref context;
        }

        public RewriteAction Action(ref T item) => _delegate(ref _context, ref item);
    }

    public static void Rewrite<TContext, T>(
        this List<T> items,
        ref TContext context,
        RefRewriteDelegate<TContext, T> action)
    {
        Rewrite(
            items,
            new RewriteRefHelper<TContext, T>(ref context, action),
            static (context, ref item) => context.Action(ref item));
    }

    public static void Rewrite<TContext, T>(
        this List<T> items,
        TContext context,
        RewriteDelegate<TContext, T> action)
        where TContext : allows ref struct
    {
        int readIndex = 0;
        int writeIndex = 0;
        var span = CollectionsMarshal.AsSpan(items);
        while (true)
        {
            if (readIndex >= items.Count)
            {
                break;
            }

            ref var current = ref span[readIndex];
            var a = action(context, ref current);
            switch (a)
            {
                case RewriteAction.KeepThisAndAllOther:
                {
                    if (readIndex == writeIndex)
                    {
                        // Array did not change.
                        return;
                    }

                    // Copy other items.
                    var remainingCount = items.Count - readIndex;
                    for (int i = 0; i < remainingCount; i++)
                    {
                        span[writeIndex] = span[readIndex];
                        writeIndex++;
                        readIndex++;
                    }
                    break;
                }
                case RewriteAction.Remove:
                {
                    readIndex++;
                    continue;
                }
                case RewriteAction.Keep:
                {
                    if (readIndex != writeIndex)
                    {
                        ref var other = ref span[writeIndex];
                        other = current;
                    }

                    readIndex++;
                    writeIndex++;
                    continue;
                }
                default:
                {
                    throw Unreachable();
                }
            }
        }

        CollectionsMarshal.SetCount(items, writeIndex);
    }
}
