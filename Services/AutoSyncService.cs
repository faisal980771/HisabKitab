using Microsoft.Maui.Networking;

namespace HisabKitab.Services;

public static class AutoSyncService
{
    private static CancellationTokenSource? _cts;
    private static bool _started;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _started = false;
    }

    private static async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    continue;

                await TrySyncAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AUTO SYNC ERROR: {ex}");
            }
        }
    }

    public static async Task TrySyncAsync()
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        var package = await BackupPackageService.BuildPendingPackageAsync(companyId.Value);
        if (package.Length == 0) return;

        // Drive OAuth access-token flow will be connected here.
        // Keep the package pending until Drive upload succeeds.
        var fileName = $"HisabKitab_Changes_{DateTime.Now:yyyyMM}.gz";
        System.Diagnostics.Debug.WriteLine($"AUTO SYNC READY: {fileName}, {package.Length} bytes");
    }
}
