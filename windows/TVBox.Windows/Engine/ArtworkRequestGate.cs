namespace TVBoxForWindows.Engine;

/// <summary>Foreground artwork goes next without increasing the shared network concurrency.</summary>
public sealed class ArtworkRequestGate(int capacity)
{
    readonly object _sync = new();
    readonly LinkedList<Waiter> _queue = new();
    int _active;
    sealed class Waiter(object owner, bool priority)
    {
        public object Owner = owner;
        public bool Priority = priority;
        public LinkedListNode<Waiter> Node;
        public TaskCompletionSource<bool> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task EnterAsync(object owner, bool priority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Waiter waiter;
        lock (_sync) {
            if (_active < capacity) { _active++; return; }
            waiter = new(owner, priority); waiter.Node = _queue.AddLast(waiter);
        }
        using var registration = ct.Register(() => {
            lock (_sync) {
                if (waiter.Node?.List == null) return;
                _queue.Remove(waiter.Node); waiter.Node = null; waiter.Ready.TrySetCanceled(ct);
            }
        });
        await waiter.Ready.Task.ConfigureAwait(false);
    }
    public void Promote(object owner)
    { lock (_sync) { var item = _queue.FirstOrDefault(w => ReferenceEquals(w.Owner, owner)); if (item != null) item.Priority = true; } }
    public void Exit()
    {
        lock (_sync) {
            if (_queue.Count == 0) { _active--; return; }
            var next = _queue.FirstOrDefault(w => w.Priority) ?? _queue.First.Value;
            _queue.Remove(next.Node); next.Node = null; next.Ready.TrySetResult(true);
        }
    }
}
