using Microsoft.Maui.Media;
using Microsoft.Maui.ApplicationModel;
using System.Globalization;
using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Controls.Shapes;

namespace HisabKitab.Views.Dashboard;

public partial class PartyEditorView : ContentView
{
    private readonly List<AddressEditorState> _billing = new();
    private readonly List<AddressEditorState> _shipping = new();
    private Party? _party;
    private string _partyType = "Customer";
    private bool _isEdit;
    private string? _imagePath;

    public event EventHandler? BackClicked;
    public event EventHandler? PartySaved;

    public PartyEditorView() => InitializeComponent();

    public void StartNew(string partyType)
    {
        _isEdit = false;
        _party = null;
        _partyType = string.Equals(partyType, "Supplier", StringComparison.OrdinalIgnoreCase) ? "Supplier" : "Customer";
        TitleLabel.Text = $"Add {_partyType}";
        SubtitleLabel.Text = $"Create a new {_partyType.ToLowerInvariant()} account";
        TypeLabel.Text = _partyType;
        PartyCodeEntry.Text = string.Empty;
        _imagePath = null;
        ClearForm();
    }

    public async Task LoadForEditAsync(int partyId)
    {
        _party = await PartyRepository.GetByIdAsync(partyId) ?? throw new InvalidOperationException("Party not found.");
        _isEdit = true;
        _partyType = _party.PartyType;
        TitleLabel.Text = $"Edit {_partyType}";
        SubtitleLabel.Text = "Update party account";
        TypeLabel.Text = _partyType;
        NameEntry.Text = _party.Name;
        ContactPersonEntry.Text = _party.ContactPerson;
        PartyCodeEntry.Text = _party.PartyCode;
        MobileEntry.Text = _party.Mobile;
        Mobile2Entry.Text = _party.Mobile2;
        EmailEntry.Text = _party.Email;
        GSTINEntry.Text = _party.GSTIN;
        PANEntry.Text = _party.PAN;
        OpeningBalanceEntry.Text = _party.OpeningBalance.ToString("0.##", CultureInfo.InvariantCulture);
        CreditLimitEntry.Text = _party.CreditLimit.ToString("0.##", CultureInfo.InvariantCulture);
        PaymentTermsEntry.Text = _party.PaymentTermsDays == 0 ? string.Empty : _party.PaymentTermsDays.ToString();
        NotesEditor.Text = _party.Notes;
        _imagePath = _party.ImagePath;
        PartyImage.Source = !string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath) ? ImageSource.FromFile(_imagePath) : ImageSource.FromFile("customer.svg");
        await LoadAddressesAsync();
    }

    private void ClearForm()
    {
        NameEntry.Text = ContactPersonEntry.Text = PartyCodeEntry.Text = string.Empty;
        MobileEntry.Text = Mobile2Entry.Text = EmailEntry.Text = string.Empty;
        GSTINEntry.Text = PANEntry.Text = OpeningBalanceEntry.Text = CreditLimitEntry.Text = PaymentTermsEntry.Text = string.Empty;
        NotesEditor.Text = string.Empty;
        _imagePath = null;
        PartyImage.Source = ImageSource.FromFile("customer.svg");
        SameAsBillingCheckBox.IsChecked = false;
        _billing.Clear(); _shipping.Clear();
        BillingAddressContainer.Children.Clear(); ShippingAddressContainer.Children.Clear();
        AddBillingState();
        AddShippingState();
    }

    private async Task LoadAddressesAsync()
    {
        var addresses = await PartyAddressRepository.GetAllAsync(_party!.Id);
        _billing.Clear(); _shipping.Clear();
        BillingAddressContainer.Children.Clear(); ShippingAddressContainer.Children.Clear();
        foreach (var address in addresses.Where(x => x.AddressType == "Billing")) AddStateToContainer(address, _billing, BillingAddressContainer);
        foreach (var address in addresses.Where(x => x.AddressType == "Shipping")) AddStateToContainer(address, _shipping, ShippingAddressContainer);
        if (_billing.Count == 0) AddBillingState();
        if (_shipping.Count == 0) AddShippingState();
    }

    private void AddBillingClicked(object? sender, EventArgs e) => AddBillingState();
    private void AddShippingClicked(object? sender, EventArgs e) => AddShippingState();
    private void AddBillingState() => AddStateToContainer(new PartyAddress { AddressType = "Billing", Label = _billing.Count == 0 ? "Main Billing" : $"Billing {_billing.Count + 1}", IsDefault = _billing.Count == 0 }, _billing, BillingAddressContainer);
    private void AddShippingState() => AddStateToContainer(new PartyAddress { AddressType = "Shipping", Label = _shipping.Count == 0 ? "Main Shipping" : $"Shipping {_shipping.Count + 1}", IsDefault = _shipping.Count == 0 }, _shipping, ShippingAddressContainer);

    private void AddStateToContainer(PartyAddress address, List<AddressEditorState> states, VerticalStackLayout container)
    {
        var state = new AddressEditorState(address);
        states.Add(state);

        var card = new Border { Padding = 12, BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#222A1D") : Color.FromArgb("#F8FAF4"), Stroke = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) } };
        var titleGrid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var label = new Entry { Placeholder = "Address label", Text = address.Label, FontSize = 12 };
        label.TextChanged += (_, e) => state.Address.Label = e.NewTextValue;
        titleGrid.Add(label);
        var remove =
            new Button
            {
                Text = "✕",
                HeightRequest = 34,
                WidthRequest = 38,
                CornerRadius = 10,
                Padding = 0,
                BackgroundColor =
                    Application.Current?.RequestedTheme == AppTheme.Dark
                        ? Color.FromArgb("#332322")
                        : Color.FromArgb("#FBEDED"),
                TextColor =
                    Color.FromArgb("#C95D5D"),
                FontAttributes =
                    FontAttributes.Bold,
                FontSize = 13
            };

        remove.Clicked += (_, _) =>
        {
            states.Remove(state);
            container.Children.Remove(card);
        };
        titleGrid.Add(remove, 1, 0);

        var line1 = AddressEntry("Address line 1", address.AddressLine1, v => state.Address.AddressLine1 = v);
        var line2 = AddressEntry("Address line 2", address.AddressLine2, v => state.Address.AddressLine2 = v);
        var area = AddressEntry("Area / locality", address.Area, v => state.Address.Area = v);
        var city = AddressEntry("City", address.City, v => state.Address.City = v);
        var stateEntry = AddressEntry("State", address.State, v => state.Address.State = v);
        var pin = AddressEntry("Pincode", address.Pincode, v => state.Address.Pincode = v);

        var row1 = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        row1.Add(city); row1.Add(stateEntry, 1, 0);
        var row2 = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        row2.Add(pin); row2.Add(new Label { Text = "India", FontSize = 12, TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B"), VerticalOptions = LayoutOptions.Center }, 1, 0);

        var defaultBox = new CheckBox { Color = Color.FromArgb("#718238"), IsChecked = address.IsDefault };
        defaultBox.CheckedChanged += (_, e) => { state.Address.IsDefault = e.Value; if (e.Value) SetDefault(states, state); };
        var defaultText = new Label { Text = "Default address", FontSize = 11, VerticalTextAlignment = TextAlignment.Center, TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B") };
        var defaultRow = new HorizontalStackLayout { Spacing = 8 }; defaultRow.Add(defaultBox); defaultRow.Add(defaultText);

        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(titleGrid); stack.Add(line1); stack.Add(line2); stack.Add(area); stack.Add(row1); stack.Add(row2); stack.Add(defaultRow);
        card.Content = stack;
        container.Children.Add(card);
    }

    private static Entry AddressEntry(string placeholder, string? value, Action<string?> setValue)
    {
        var entry = new Entry { Placeholder = placeholder, Text = value, FontSize = 12 };
        entry.TextChanged += (_, e) => setValue(e.NewTextValue);
        return entry;
    }

    private static void SetDefault(List<AddressEditorState> states, AddressEditorState selected)
    {
        foreach (var item in states.Where(x => x != selected)) item.Address.IsDefault = false;
    }

    private void SameAsBillingChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!e.Value || _billing.Count == 0) return;
        var source = _billing.FirstOrDefault(x => x.Address.IsDefault) ?? _billing[0];
        var copy = new PartyAddress
        {
            AddressType = "Shipping", Label = "Shipping (same as billing)", AddressLine1 = source.Address.AddressLine1,
            AddressLine2 = source.Address.AddressLine2, Area = source.Address.Area, City = source.Address.City,
            State = source.Address.State, Pincode = source.Address.Pincode, Country = source.Address.Country, IsDefault = true
        };
        _shipping.Clear(); ShippingAddressContainer.Children.Clear(); AddStateToContainer(copy, _shipping, ShippingAddressContainer);
    }

    private async void UploadPartyImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions { Title = "Select party image" });
            if (result == null) return;

            var extension = System.IO.Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            var folder = System.IO.Path.Combine(FileSystem.AppDataDirectory, "party-images");
            Directory.CreateDirectory(folder);
            var target = System.IO.Path.Combine(folder, $"party_{Guid.NewGuid():N}{extension}");
            await using var source = await result.OpenReadAsync();
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);

            _imagePath = target;
            PartyImage.Source = ImageSource.FromFile(target);
            await ToastService.Success("Party image selected.");
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

    private void RemovePartyImageClicked(object? sender, EventArgs e)
    {
        _imagePath = null;
        PartyImage.Source = ImageSource.FromFile("customer.svg");
    }

    private void BackTapped(object? sender, TappedEventArgs e) => BackClicked?.Invoke(this, EventArgs.Empty);

    private async void SaveClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            await ToastService.Warning("Please enter party name.");
            NameEntry.Focus();
            return;
        }

        var companyId = await SessionService.GetActiveCompanyIdAsync();

        if (!companyId.HasValue)
        {
            await ToastService.Error("No active company selected.");
            return;
        }

        if (await PartyRepository.ExistsByNameAsync(
            companyId.Value,
            name,
            _partyType,
            _isEdit ? _party!.Id : 0))
        {
            await ToastService.Warning("This party already exists.");
            return;
        }

        decimal.TryParse(
            OpeningBalanceEntry.Text?.Replace(",", ""),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var opening);

        decimal.TryParse(
            CreditLimitEntry.Text?.Replace(",", ""),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var limit);

        int.TryParse(
            PaymentTermsEntry.Text,
            out var terms);

        var successMessage =
            _isEdit
                ? "Party updated successfully."
                : $"{_partyType} created successfully.";

        try
        {
            SaveButton.IsEnabled = false;

            await SaveLoader.RunAsync(
                async () =>
                {
                    var party =
                        _party ??
                        new Party
                        {
                            CompanyId = companyId.Value,
                            PartyType = _partyType,
                            IsActive = true
                        };

                    party.CompanyId = companyId.Value;
                    party.PartyType = _partyType;
                    party.Name = name;
                    party.ContactPerson = ContactPersonEntry.Text?.Trim();
                    party.Mobile = MobileEntry.Text?.Trim();
                    party.Mobile2 = Mobile2Entry.Text?.Trim();
                    party.Email = EmailEntry.Text?.Trim();
                    party.GSTIN = GSTINEntry.Text?.Trim();
                    party.PAN = PANEntry.Text?.Trim();
                    party.OpeningBalance = opening;
                    party.CreditLimit = limit;
                    party.PaymentTermsDays = terms;
                    party.Notes = NotesEditor.Text?.Trim();
                    party.ImagePath = _imagePath;

                    var id = await PartyRepository.SaveAsync(party);

                    var addresses =
                        _billing
                            .Select(x => x.Address)
                            .Concat(_shipping.Select(x => x.Address))
                            .ToList();

                    await PartyAddressRepository.ReplaceAsync(
                        companyId.Value,
                        id,
                        addresses);
                },
                _isEdit ? "Updating party..." : "Creating party...");

            // Ensure the modal is definitely removed before navigation/toast.
            SaveLoader.Hide();
            await Task.Delay(80);

            // Navigate first; the success toast is now shown on PartyView.
            PartySaved?.Invoke(this, EventArgs.Empty);
            await ToastService.Success(successMessage);
        }
        catch (Exception ex)
        {
            SaveLoader.Hide();
            await ToastService.Error(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private sealed class AddressEditorState
    {
        public PartyAddress Address { get; }
        public AddressEditorState(PartyAddress address) => Address = address;
    }
}
