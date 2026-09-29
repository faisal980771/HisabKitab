using HisabKitab.Models;
using HisabKitab.Services;

namespace HisabKitab.Views;

public partial class LoginPage : ContentPage
{
    private readonly GoogleAuthService _googleAuthService;

    public LoginPage()
    {
        InitializeComponent();

        _googleAuthService =
            new GoogleAuthService();
    }

    private async void GoogleLoginClicked(
    object sender,
    EventArgs e)
    {
        try
        {
            var window =
                Application.Current?
                    .Windows
                    .FirstOrDefault();

            if (window == null)
                return;

            ContentPage? nextPage = null;

            await Loader.RunAsync(
                async () =>
                {
                    var result =
                        await _googleAuthService.SignInAsync();

                    if (!result)
                    {
                        throw new Exception(
                            "Google authentication failed.");
                    }

                    var googleId =
                        await SessionService
                            .GetGoogleIdAsync();

                    var email =
                        await SessionService
                            .GetEmailAsync();

                    var name =
                        await SessionService
                            .GetNameAsync();

                    if (string.IsNullOrWhiteSpace(googleId))
                    {
                        throw new Exception(
                            "Google ID was not received.");
                    }

                    await UserRepository
                        .SaveGoogleUserAsync(
                            googleId,
                            name ?? "User",
                            email ?? string.Empty);

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

                        nextPage = new MainPage();
                    }
                    else
                    {
                        nextPage =
                            new CompanySetupPage();
                    }
                },
                "Signing you in...");

            if (nextPage != null)
                window.Page = nextPage;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "Google Login",
                ex.Message,
                "OK");
        }
    }
}