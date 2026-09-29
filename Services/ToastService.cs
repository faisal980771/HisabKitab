using HisabKitab.Controls;
using Microsoft.Maui.ApplicationModel;

namespace HisabKitab.Services;

public static class ToastService
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static AppToast? _mainHost;

    public static void RegisterMainHost(AppToast host)
    {
        _mainHost = host;
        _mainHost.HideImmediately();
    }

    public static void UnregisterMainHost(AppToast host)
    {
        if (ReferenceEquals(_mainHost, host))
            _mainHost = null;
    }

    public static Task Success(string message, int duration = 2500) =>
        ShowAsync(message, ToastType.Success, duration);

    public static Task Error(string message, int duration = 3200) =>
        ShowAsync(message, ToastType.Error, duration);

    public static Task Warning(string message, int duration = 2800) =>
        ShowAsync(message, ToastType.Warning, duration);

    public static Task Info(string message, int duration = 2500) =>
        ShowAsync(message, ToastType.Info, duration);

    public static async Task ShowAsync(
        string message,
        ToastType type = ToastType.Info,
        int duration = 2500)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Lock.WaitAsync();

            try
            {
                if (_mainHost == null)
                    return;

                await _mainHost.ShowAsync(
                    message,
                    type,
                    duration);
            }
            finally
            {
                Lock.Release();
            }
        });
    }
}
