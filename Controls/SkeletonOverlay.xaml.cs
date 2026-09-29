namespace HisabKitab.Controls;

public enum SkeletonType
{
    Home,
    Stock,
    List,
    More,
    Detail,
    Form
}

public partial class SkeletonOverlay : Grid
{
    private CancellationTokenSource? _animationCts;

    public SkeletonOverlay()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(SkeletonType type)
    {
        _animationCts?.Cancel();
        _animationCts = new CancellationTokenSource();

        HomeSkeleton.IsVisible = type == SkeletonType.Home;
        StockSkeleton.IsVisible = type == SkeletonType.Stock;
        ListSkeleton.IsVisible = type == SkeletonType.List;
        MoreSkeleton.IsVisible = type == SkeletonType.More;
        DetailSkeleton.IsVisible = type == SkeletonType.Detail;
        FormSkeleton.IsVisible = type == SkeletonType.Form;

        SkeletonBody.Opacity = 1;
        IsVisible = true;
        IsEnabled = true;
        InputTransparent = false;

        _ = AnimateSkeletonAsync(_animationCts.Token);

        await Task.Yield();
    }

    public async Task HideAsync()
    {
        _animationCts?.Cancel();

        IsVisible = false;
        IsEnabled = false;
        InputTransparent = true;
        SkeletonBody.Opacity = 1;

        await Task.Yield();
    }

    public void HideImmediately()
    {
        _animationCts?.Cancel();

        IsVisible = false;
        IsEnabled = false;
        InputTransparent = true;
        SkeletonBody.Opacity = 1;
    }

    private async Task AnimateSkeletonAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && IsVisible)
            {
                SkeletonBody.Opacity = 0.64;
                await Task.Delay(420, token);

                SkeletonBody.Opacity = 1;
                await Task.Delay(420, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
