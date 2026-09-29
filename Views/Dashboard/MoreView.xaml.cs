namespace HisabKitab.Views.Dashboard;

public partial class MoreView : ContentView
{
    public event EventHandler? UnitMasterClicked;

    public MoreView()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        await Task.CompletedTask;
    }

    private async void MoreRefreshView_Refreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        MoreRefreshView.IsRefreshing = false;
    }

    private void UnitMasterTapped(object? sender, TappedEventArgs e) =>
        UnitMasterClicked?.Invoke(this, EventArgs.Empty);
}
