using Microsoft.Maui.Controls;

namespace HisabKitab.Controls;

public partial class ImagePreviewOverlay : ContentView
{
    public ImagePreviewOverlay()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(string imagePath, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        PreviewImage.Source = ImageSource.FromFile(imagePath);
        PreviewTitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Image Preview" : title;
        IsVisible = true;
        Opacity = 0;
        PreviewCard.Scale = 0.92;
        PreviewCard.TranslationY = 12;

        await Task.WhenAll(
            this.FadeToAsync(1, 150, Easing.CubicOut),
            PreviewCard.ScaleToAsync(1, 180, Easing.CubicOut),
            PreviewCard.TranslateToAsync(0, 0, 180, Easing.CubicOut));
    }

    public async Task HideAsync()
    {
        if (!IsVisible) return;
        await Task.WhenAll(
            this.FadeToAsync(0, 110, Easing.CubicIn),
            PreviewCard.ScaleToAsync(0.94, 120, Easing.CubicIn));
        IsVisible = false;
        Opacity = 1;
        PreviewCard.Scale = 1;
        PreviewCard.TranslationY = 0;
    }

    private async void CloseTapped(object? sender, TappedEventArgs e) => await HideAsync();
    private async void BackdropTapped(object? sender, TappedEventArgs e) => await HideAsync();
}
