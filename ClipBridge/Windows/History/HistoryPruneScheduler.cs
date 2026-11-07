using System;
using System.Threading;

namespace ClipBridge.Windows.History;

internal sealed class HistoryPruneScheduler : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly Action _prune;
    private readonly Action<Exception> _reportFailure;
    private readonly IDisposable _scheduledTick;
    private int _running;

    internal HistoryPruneScheduler(
        Action prune,
        Action<Exception> reportFailure,
        Func<Action, TimeSpan, TimeSpan, IDisposable>? schedule = null)
    {
        _prune = prune ?? throw new ArgumentNullException(nameof(prune));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        Func<Action, TimeSpan, TimeSpan, IDisposable> scheduleTick = schedule ??
            ((tick, dueTime, interval) => new Timer(_ => tick(), null, dueTime, interval));
        _scheduledTick = scheduleTick(PruneOnce, TimeSpan.Zero, Interval);
    }

    private void PruneOnce()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try { _prune(); }
        catch (Exception exception)
        {
            try { _reportFailure(exception); }
            catch { }
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    public void Dispose() => _scheduledTick.Dispose();
}
