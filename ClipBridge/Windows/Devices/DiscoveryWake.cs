using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal sealed class DiscoveryWake : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public DiscoveryWake() { }
    public void Signal()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }
    public Task<bool> WaitAsync(int milliseconds, CancellationToken token) => _signal.WaitAsync(milliseconds, token);
    public void Dispose() => _signal.Dispose();
}