using HisabKitab.Services;
using HisabKitab.Views;

namespace HisabKitab;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        AutoSyncService.Start();
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        return new Window(
            new StartupPage());
    }
}