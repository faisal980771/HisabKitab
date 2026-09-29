namespace HisabKitab.Controls;

public partial class LoadingOverlay : Grid
{
    public LoadingOverlay() { InitializeComponent(); }

    public void Show(string message = "Processing...")
    {
        MessageLabel.Text = message;
        IsVisible = true;
        Spinner.IsRunning = true;
    }

    public void Hide()
    {
        Spinner.IsRunning = false;
        IsVisible = false;
    }

    public async Task RunAsync(Func<Task> action, string message = "Processing...")
    {
        Show(message);
        try
        {
            await Task.Yield();
            await action();
        }
        finally
        {
            Hide();
        }
    }
}