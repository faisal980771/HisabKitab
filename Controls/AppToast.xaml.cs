using Microsoft.Maui.Devices;

namespace HisabKitab.Controls;

public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

public partial class AppToast : Grid
{
    private bool _closing;
    private double _startX;

    public AppToast()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(
        string message,
        ToastType type,
        int duration)
    {
        if (_closing)
            return;

        ApplyType(type);

        MessageLabel.Text = message;

        IsVisible = true;
        IsEnabled = true;
        InputTransparent = false;

        ToastCard.Opacity = 0;
        ToastCard.TranslationX = 0;
        ToastCard.TranslationY = 55;
        ToastCard.Scale = 0.98;

        await Task.WhenAll(
            ToastCard.FadeToAsync(
                1,
                170,
                Easing.CubicOut),

            ToastCard.TranslateToAsync(
                0,
                0,
                330,
                Easing.CubicOut),

            ToastCard.ScaleToAsync(
                1,
                220,
                Easing.CubicOut));

        await Task.Delay(duration);

        await CloseAsync();
    }

    public void HideImmediately()
    {
        _closing = false;

        IsVisible = false;
        IsEnabled = false;
        InputTransparent = true;

        ToastCard.Opacity = 0;
        ToastCard.TranslationX = 0;
        ToastCard.TranslationY = 55;
        ToastCard.Scale = 0.98;
    }

    private async Task CloseAsync()
    {
        if (_closing)
            return;

        _closing = true;

        try
        {
            var exitX = Math.Max(
                Width + 50,
                380);

            await Task.WhenAll(
                ToastCard.FadeToAsync(
                    0,
                    160,
                    Easing.CubicIn),

                ToastCard.TranslateToAsync(
                    exitX,
                    0,
                    260,
                    Easing.CubicIn),

                ToastCard.ScaleToAsync(
                    0.97,
                    160,
                    Easing.CubicIn));

            IsVisible = false;
            IsEnabled = false;
            InputTransparent = true;
        }
        finally
        {
            ToastCard.TranslationX = 0;
            ToastCard.TranslationY = 55;
            ToastCard.Scale = 0.98;
            ToastCard.Opacity = 0;
            _closing = false;
        }
    }

    private async void ToastPanUpdated(
        object? sender,
        PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _startX = ToastCard.TranslationX;
                break;

            case GestureStatus.Running:
                if (e.TotalX > 0)
                    ToastCard.TranslationX =
                        _startX + e.TotalX;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:

                if (ToastCard.TranslationX > 65)
                    await CloseAsync();
                else
                    await ToastCard.TranslateToAsync(
                        0,
                        0,
                        160,
                        Easing.CubicOut);

                break;
        }
    }

    private void ApplyType(ToastType type)
    {
        var accent = type switch
        {
            ToastType.Success => Color.FromArgb("#4F9D5F"),
            ToastType.Error => Color.FromArgb("#C95D5D"),
            ToastType.Warning => Color.FromArgb("#D9943B"),
            _ => Color.FromArgb("#718238")
        };

        AccentBar.BackgroundColor = accent;
        TypeLabel.Text = type.ToString().ToUpperInvariant();
        TypeLabel.TextColor = accent;

        if (type == ToastType.Error ||
            type == ToastType.Warning)
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                    HapticFeedback.Default.Perform(
                        HapticFeedbackType.Click);
            }
            catch
            {
            }
        }
    }
}
