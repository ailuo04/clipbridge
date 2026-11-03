using ClipBridge.Windows;

internal static class DiscoveryWakeTests
{
    public static async Task RunAsync()
    {
        using var wake = new DiscoveryWake();
        for (int n = 0; n < 5; n++) wake.Signal();
        Program.Check(await wake.WaitAsync(50, CancellationToken.None), "discovery refresh wakes immediately");
        Program.Check(!await wake.WaitAsync(20, CancellationToken.None), "duplicate discovery refreshes are coalesced");
        var later = Task.Run(async () => { await Task.Delay(20); wake.Signal(); });
        Program.Check(await wake.WaitAsync(1000, CancellationToken.None), "a later network change wakes discovery");
        await later;
    }
}