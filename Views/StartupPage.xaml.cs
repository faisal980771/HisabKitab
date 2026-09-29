using HisabKitab.Services;

namespace HisabKitab.Views;

public partial class StartupPage : ContentPage
{
    private bool _started;

    public StartupPage()
    {
        InitializeComponent();

        Loaded += StartupPage_Loaded;
    }

    private async void StartupPage_Loaded(
        object? sender,
        EventArgs e)
    {
        if (_started)
            return;

        _started = true;

        try
        {
            await DatabaseService.InitializeAsync();

            bool isLoggedIn =
                await SessionService.IsLoggedInAsync();

            var window =
                Application.Current?
                    .Windows
                    .FirstOrDefault();

            if (window == null)
                return;

            if (!isLoggedIn)
            {
                window.Page = new LoginPage();
                return;
            }

            var googleId =
                await SessionService.GetGoogleIdAsync();

            if (string.IsNullOrWhiteSpace(googleId))
            {
                SessionService.Logout();

                window.Page = new LoginPage();
                return;
            }

            var hasCompany =
                await CompanyRepository
                    .ExistsForUserAsync(googleId);

            if (hasCompany)
            {
                var companies =
                    await CompanyRepository
                        .GetAllForUserAsync(googleId);

                var activeCompanyId =
                    await SessionService
                        .GetActiveCompanyIdAsync();

                if (!activeCompanyId.HasValue &&
                    companies.Count > 0)
                {
                    await SessionService
                        .SetActiveCompanyIdAsync(
                            companies[0].Id);
                }

                window.Page = new MainPage();
            }
            else
            {
                window.Page =
                    new CompanySetupPage();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"STARTUP ERROR: {ex}");

            var window =
                Application.Current?
                    .Windows
                    .FirstOrDefault();

            if (window != null)
            {
                window.Page =
                    new LoginPage();
            }
        }
    }
}