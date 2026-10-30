namespace FluentTube;

// Share extraction work between hover preparation and playback. Each caller can
// cancel independently; the worker stops when nobody needs its result anymore.
internal sealed class StreamCache<T>(Func<string, CancellationToken, Task<T>> factory,
    Func<T, DateTimeOffset> expires, int capacity = 12, Func<DateTimeOffset>? clock = null)
{
    private sealed class Pending
    {
        public readonly CancellationTokenSource Stop = new();
        public readonly TaskCompletionSource<T> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, (T Value, DateTimeOffset Expires, long Used)> values = [];
    private readonly Dictionary<string, Pending> pending = [];
    private long sequence;
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    public bool TryGet(string id, out T value)
    {
        lock (gate)
        {
            if (values.TryGetValue(id, out var hit) && hit.Expires > Now)
            {
                values[id] = (hit.Value, hit.Expires, ++sequence);
                value = hit.Value;
                return true;
            }
            values.Remove(id);
            value = default!;
            return false;
        }
    }
    public async Task<T> Get(string id, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Pending work;
        bool start = false;
        lock (gate)
        {
            if (values.TryGetValue(id, out var hit) && hit.Expires > Now)
            {
                values[id] = (hit.Value, hit.Expires, ++sequence);
                return hit.Value;
            }
            values.Remove(id);
            if (!pending.TryGetValue(id, out work!)) { work = new(); pending[id] = work; start = true; }
            work.Waiters++;
        }
        if (start) _ = Produce(id, work);
        try { return await work.Result.Task.WaitAsync(cancellation); }
        finally
        {
            lock (gate)
            {
                work.Waiters--;
                if (work.Waiters == 0 && !work.Result.Task.IsCompleted)
                {
                    if (pending.TryGetValue(id, out var existing) && ReferenceEquals(existing, work)) pending.Remove(id);
                    work.Stop.Cancel();
                }
            }
        }
    }
    private async Task Produce(string id, Pending work)
    {
        try
        {
            var value = await factory(id, work.Stop.Token);
            lock (gate)
            {
                work.Stop.Token.ThrowIfCancellationRequested();
                var until = expires(value);
                if (until > Now)
                {
                    values[id] = (value, until, ++sequence);
                    while (values.Count > capacity) values.Remove(values.MinBy(p => p.Value.Used).Key);
                }
                work.Result.TrySetResult(value);
            }
        }
        catch (OperationCanceledException) { work.Result.TrySetCanceled(); }
        catch (Exception e) { work.Result.TrySetException(e); }
        finally
        {
            lock (gate)
            {
                if (pending.TryGetValue(id, out var existing) && ReferenceEquals(existing, work)) pending.Remove(id);
                work.Stop.Dispose();
            }
        }
    }
    public void Invalidate(string id)
    {
        lock (gate)
        {
            values.Remove(id);
            if (pending.Remove(id, out var work)) work.Stop.Cancel();
        }
    }
}
