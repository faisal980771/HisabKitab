using HisabKitab.Services;

namespace HisabKitab.Views.Dashboard;

public partial class HomeView : ContentView
{
    private bool _loaded;
    public event EventHandler? CompanyClicked;

    public HomeView() { InitializeComponent(); }

    private async void HomeView_Loaded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var googleId = await SessionService.GetGoogleIdAsync();
            if (string.IsNullOrWhiteSpace(googleId)) return;

            var user = await UserRepository.GetByGoogleIdAsync(googleId);
            var companyId = await SessionService.GetActiveCompanyIdAsync();
            var companies = await CompanyRepository.GetAllForUserAsync(googleId);

            if (!companyId.HasValue && companies.Count > 0)
            {
                companyId = companies[0].Id;
                await SessionService.SetActiveCompanyIdAsync(companyId.Value);
            }

            var company = companyId.HasValue ? await CompanyRepository.GetByIdAsync(companyId.Value) : null;
            var personName = !string.IsNullOrWhiteSpace(user?.Name) ? user.Name.Trim() : company?.OwnerName?.Trim();
            var companyName = company?.CompanyName?.Trim();

            GreetingLabel.Text = GetGreeting();
            UserNameLabel.Text = string.IsNullOrWhiteSpace(personName) ? "User" : personName;
            CompanyInitialLabel.Text = GetInitial(companyName ?? personName);
            CompanyNameLabel.Text = string.IsNullOrWhiteSpace(companyName) ? "My Business" : companyName;
            FinancialYearLabel.Text = company == null ? "Financial Year" : $"Financial Year {GetFinancialYear(company.FinancialYearStartMonth)}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HOME LOAD ERROR: {ex}");
        }
        finally
        {
            SkeletonContainer.IsVisible = false;
            DashboardContainer.IsVisible = true;
            DashboardRefreshView.IsRefreshing = false;
        }
    }

    private async void DashboardRefreshView_Refreshing(object? sender, EventArgs e) => await RefreshAsync();

    private void CompanyCardTapped(object? sender, TappedEventArgs e) => CompanyClicked?.Invoke(this, EventArgs.Empty);

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good Morning ☀️" : hour < 17 ? "Good Afternoon 👋" : "Good Evening 👋";
    }

    private static string GetInitial(string? value) => string.IsNullOrWhiteSpace(value) ? "H" : value.Trim()[0].ToString().ToUpperInvariant();

    private static string GetFinancialYear(int startMonth)
    {
        var now = DateTime.Now;
        var startYear = now.Month >= startMonth ? now.Year : now.Year - 1;
        return $"{startYear}-{(startYear + 1) % 100:00}";
    }
}
