using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using HisabKitab.Models;
using HisabKitab.Services;

namespace HisabKitab.Views;

public partial class CompanySetupPage : ContentPage
{
    private string? _companyLogoPath;
    public CompanySetupPage()
    {
        InitializeComponent();

        Loaded += CompanySetupPage_Loaded;
    }

    private async void CompanySetupPage_Loaded(
        object? sender,
        EventArgs e)
    {
        try
        {
            var name = await SessionService.GetNameAsync();
            var email = await SessionService.GetEmailAsync();

            WelcomeNameLabel.Text =
                string.IsNullOrWhiteSpace(name)
                    ? "Welcome"
                    : $"Welcome, {name}";

            OwnerNameEntry.Text = name;
            EmailEntry.Text = email;
            _companyLogoPath = null;
            CompanyLogoImage.Source = ImageSource.FromFile("appicon.svg");
        }
        catch
        {
        }
    }

    private async void UploadCompanyLogoClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions { Title = "Select business logo" });
            if (result == null) return;

            var extension = Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var folder = Path.Combine(FileSystem.AppDataDirectory, "company-logos");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, $"company_{Guid.NewGuid():N}{extension}");
            await using var source = await result.OpenReadAsync();
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);

            _companyLogoPath = target;
            CompanyLogoImage.Source = ImageSource.FromFile(target);
            await ToastService.Success("Business logo selected.");
        }
        catch (FeatureNotSupportedException)
        {
            await ToastService.Warning("Photo selection is not supported on this device.");
        }
        catch (PermissionException)
        {
            await ToastService.Warning("Photo permission was not granted.");
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
    }

    private async void CreateCompanyClicked(object sender, EventArgs e)
    {
        try
        {
            var googleId = await SessionService.GetGoogleIdAsync();

            if (string.IsNullOrWhiteSpace(googleId))
            {
                await ToastService.Warning("Login session not found.");
                return;
            }

            var companyName = CompanyNameEntry.Text?.Trim();

            var mobile = MobileEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(companyName))
            {
                await ToastService.Warning("Please enter company name.");
                return;
            }

            if (string.IsNullOrWhiteSpace(mobile) || mobile.Length != 10)
            {
                await ToastService.Warning("Please enter valid mobile number.");
                return;
            }

            if (IndustryPicker.SelectedIndex < 0)
            {
                await ToastService.Warning("Please select industry type.");
                return;
            }

            await Loader.RunAsync(
                async () =>
                {
                    var company = new Company
                    {
                        OwnerGoogleId = googleId,

                        CompanyName =
                            companyName ?? string.Empty,

                        OwnerName =
                            OwnerNameEntry.Text?.Trim(),

                        Mobile =
                            mobile ?? string.Empty,

                        Email =
                            EmailEntry.Text?.Trim(),

                        IndustryType =
                            IndustryPicker.SelectedItem?
                                .ToString()
                            ?? string.Empty,

                        BusinessType =
                            BusinessTypePicker.SelectedItem?
                                .ToString(),

                        GSTIN =
                            GSTINEntry.Text?.Trim(),

                        PAN =
                            PANEntry.Text?.Trim(),

                        Address =
                            AddressEditor.Text?.Trim(),

                        City =
                            CityEntry.Text?.Trim(),

                        State =
                            StateEntry.Text?.Trim(),

                        Pincode =
                            PincodeEntry.Text?.Trim(),

                        Country = "India",
                        Currency = "INR",
                        FinancialYearStartMonth = 4,
                        LogoPath = _companyLogoPath,
                        IsActive = true
                    };

                    var companyId =
                        await CompanyRepository.SaveAsync(company);

                    await SessionService
                        .SetActiveCompanyIdAsync(companyId);
                },
                "Creating company...");

            var window =
                Application.Current?
                    .Windows
                    .FirstOrDefault();

            if (window != null)
                window.Page = new MainPage();
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
    }
}
