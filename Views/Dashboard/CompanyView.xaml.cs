using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
namespace HisabKitab.Views.Dashboard;
public partial class CompanyView : ContentView
{
    private List<CompanyOption> _options = new();
    public event EventHandler? AddCompanyClicked;
    public event EventHandler? BackClicked;
    public CompanyView() { InitializeComponent(); Loaded += CompanyView_Loaded; }
    private async void CompanyView_Loaded(object? sender, EventArgs e) => await RefreshAsync();
    public async Task RefreshAsync()
    {
        try
        {
            var googleId = await SessionService.GetGoogleIdAsync();
            if (string.IsNullOrWhiteSpace(googleId)) return;
            var companies = await CompanyRepository.GetAllForUserAsync(googleId);
            var activeId = await SessionService.GetActiveCompanyIdAsync();
            if (!activeId.HasValue && companies.Count > 0) { activeId = companies[0].Id; await SessionService.SetActiveCompanyIdAsync(activeId.Value); }
            _options = companies.Select(c => new CompanyOption(c, c.Id == activeId)).ToList();
            CompanyList.ItemsSource = _options;
            CompanyCountLabel.Text = companies.Count.ToString();
            DefaultCompanyLabel.Text = _options.FirstOrDefault(x => x.IsDefault)?.Company.CompanyName ?? "No company selected";
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"COMPANY VIEW ERROR: {ex}"); }
    }
    private async void CompanyList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CompanyOption selected) return;
        await SessionService.SetActiveCompanyIdAsync(selected.Company.Id);
        await RefreshAsync();
        CompanyList.SelectedItem = null;
    }

    private async void CompanyLogoButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not Button button || button.CommandParameter == null) return;
            if (!int.TryParse(button.CommandParameter.ToString(), out var companyId) || companyId <= 0) return;

            var company = await CompanyRepository.GetByIdAsync(companyId);
            if (company == null) return;

            var photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Select business logo"
            });
            if (photo == null) return;

            var folder = Path.Combine(FileSystem.AppDataDirectory, "company-logos");
            Directory.CreateDirectory(folder);
            var extension = Path.GetExtension(photo.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            var target = Path.Combine(folder, $"company_{company.Id}{extension}");

            await using (var source = await photo.OpenReadAsync())
            await using (var destination = File.Create(target))
            {
                await source.CopyToAsync(destination);
            }

            company.LogoPath = target;
            await CompanyRepository.SaveAsync(company);
            await RefreshAsync();
            await ToastService.Success("Business logo updated.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"COMPANY LOGO ERROR: {ex}");
            await ToastService.Error("Unable to update business logo.");
        }
    }

    private void AddCompanyButtonClicked(object? sender, EventArgs e) => AddCompanyClicked?.Invoke(this, EventArgs.Empty);
    private void BackButtonTapped(object? sender, TappedEventArgs e) => BackClicked?.Invoke(this, EventArgs.Empty);
    private sealed class CompanyOption
    {
        public Company Company { get; }
        public bool IsDefault { get; }
        public string? LogoPath => !string.IsNullOrWhiteSpace(Company.LogoPath) && File.Exists(Company.LogoPath) ? Company.LogoPath : null;
        public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);
        public bool HasNoLogo => !HasLogo;
        public string Initials
        {
            get
            {
                var name = Company.CompanyName ?? string.Empty;
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 0 ? "C" : parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
            }
        }
        public CompanyOption(Company company, bool isDefault) { Company = company; IsDefault = isDefault; }
    }
}
