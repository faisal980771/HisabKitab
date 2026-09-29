using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.ApplicationModel;

namespace HisabKitab.Views.Dashboard;

public partial class PartyDetailView : ContentView
{
    private Party? _party;
    private List<PartyAddress> _addresses = new();
    private List<PartyActivity> _activities = new();
    private string _tab = "Overview";
    private decimal _currentBalance;

    public event EventHandler? BackClicked;
    public event EventHandler<int>? EditClicked;
    public event EventHandler? Deleted;

    public PartyDetailView() => InitializeComponent();

    public async Task LoadAsync(int partyId)
    {
        _party = await PartyRepository.GetByIdAsync(partyId) ?? throw new InvalidOperationException("Party not found.");
        _addresses = await PartyAddressRepository.GetAllAsync(partyId);
        _activities = await PartyActivityRepository.GetAllAsync(partyId);
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        _currentBalance = companyId.HasValue ? await BillRepository.GetPartyBalanceAsync(companyId.Value, partyId) : _party.OpeningBalance;
        TitleLabel.Text = _party.Name;
        PartyNameLabel.Text = _party.Name;
        PartyTypeLabel.Text = _party.PartyType;
        PartyContactLabel.Text = string.Join(" • ", new[] { _party.Mobile, _party.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        InitialsLabel.Text = GetInitials(_party.Name);
        PartyBalanceHeaderLabel.Text = $"Balance  {InvoiceFormatService.Money(_currentBalance)}";
        if (!string.IsNullOrWhiteSpace(_party.ImagePath) && File.Exists(_party.ImagePath))
        {
            PartyImage.Source = ImageSource.FromFile(_party.ImagePath);
            PartyImage.IsVisible = true;
            InitialsLabel.IsVisible = false;
        }
        else
        {
            PartyImage.IsVisible = false;
            InitialsLabel.IsVisible = true;
        }
        RenderTab();
    }

    private void RenderTab()
    {
        DetailContent.Children.Clear();
        SetTabVisuals();
        if (_tab == "Billing") RenderAddresses("Billing");
        else if (_tab == "Shipping") RenderAddresses("Shipping");
        else if (_tab == "Timeline") RenderTimeline();
        else RenderOverview();
    }

    private void RenderOverview()
    {
        AddContactSection();

        AddSection("Tax & credit", new[]
        {
            ("GSTIN", string.IsNullOrWhiteSpace(_party?.GSTIN) ? "Not provided" : _party!.GSTIN!),
            ("PAN", string.IsNullOrWhiteSpace(_party?.PAN) ? "Not provided" : _party!.PAN!),
            ("Opening balance", InvoiceFormatService.Money(_party?.OpeningBalance ?? 0)),
            ("Current balance", InvoiceFormatService.Money(_currentBalance)),
            ("Credit limit", InvoiceFormatService.Money(_party?.CreditLimit ?? 0)),
            ("Payment terms", $"{_party?.PaymentTermsDays ?? 0} days")
        });

        var note =
            string.IsNullOrWhiteSpace(_party?.Notes)
                ? "No notes"
                : _party!.Notes!;

        AddSection("Notes", new[] { ("", note) });

        var defaultBilling =
            _addresses.FirstOrDefault(
                x => x.AddressType == "Billing" && x.IsDefault)
            ?? _addresses.FirstOrDefault(
                x => x.AddressType == "Billing");

        if (defaultBilling != null)
            AddAddressCard(defaultBilling);
    }

    private void AddContactSection()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 10
        };

        stack.Add(new Label
        {
            Text = "Contact details",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = GetPrimaryText()
        });

        AddContactRow(stack, "Mobile 1", _party?.Mobile, "☎");
        AddContactRow(stack, "Mobile 2", _party?.Mobile2, "☎");
        AddContactRow(stack, "Email", _party?.Email, "✉");
        AddContactRow(stack, "Contact person", _party?.ContactPerson, null);

        DetailContent.Add(new Border
        {
            Padding = 15,
            BackgroundColor = GetCardBg(),
            Stroke = new SolidColorBrush(GetBorder()),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(18)
            },
            Content = stack
        });
    }

    private void AddContactRow(
        VerticalStackLayout stack,
        string label,
        string? value,
        string? icon)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(112)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };

        row.Add(new Label
        {
            Text = label,
            FontSize = 10,
            TextColor = GetSecondaryText(),
            VerticalTextAlignment = TextAlignment.Center
        });

        if (string.IsNullOrWhiteSpace(value))
        {
            row.Add(new Label
            {
                Text = "Not provided",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = GetSecondaryText(),
                LineBreakMode = LineBreakMode.WordWrap
            }, 1, 0);

            stack.Add(row);
            return;
        }

        var text = value.Trim();

        var valueLayout = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center
        };

        if (!string.IsNullOrWhiteSpace(icon))
        {
            valueLayout.Add(new Label
            {
                Text = icon,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#718238"),
                VerticalTextAlignment = TextAlignment.Center
            });
        }

        var valueLabel = new Label
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#718238"),
            LineBreakMode = LineBreakMode.WordWrap
        };

        valueLayout.Add(valueLabel);

        if (label.StartsWith("Mobile", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(label, "Email", StringComparison.OrdinalIgnoreCase))
        {
            var tap = new TapGestureRecognizer();

            tap.Tapped += async (_, _) =>
            {
                try
                {
                    var uri = label.StartsWith("Mobile", StringComparison.OrdinalIgnoreCase)
                        ? $"tel:{text}"
                        : $"mailto:{text}";

                    await Launcher.Default.OpenAsync(uri);
                }
                catch
                {
                    await ToastService.Warning(
                        $"Unable to open {label.ToLowerInvariant()}.");
                }
            };

            valueLayout.GestureRecognizers.Add(tap);
        }

        row.Add(valueLayout, 1, 0);
        stack.Add(row);
    }

    private void RenderAddresses(string type)
    {
        var list = _addresses.Where(x => x.AddressType == type).ToList();
        if (list.Count == 0)
        {
            DetailContent.Add(new Label { Text = $"No {type.ToLowerInvariant()} addresses saved.", FontSize = 13, TextColor = GetSecondaryText(), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 40) });
            return;
        }
        foreach (var address in list) AddAddressCard(address);
    }

    private void RenderTimeline()
    {
        if (_activities.Count == 0)
        {
            DetailContent.Add(new Label { Text = "No activity yet.", FontSize = 13, TextColor = GetSecondaryText(), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 40) });
            return;
        }
        foreach (var item in _activities)
        {
            var dot = new Border { WidthRequest = 12, HeightRequest = 12, BackgroundColor = Color.FromArgb("#718238"), Stroke = new SolidColorBrush(Colors.Transparent), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) }, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 5, 0, 0) };
            var text = new VerticalStackLayout { Spacing = 3 };
            text.Add(new Label { Text = item.Title, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = GetPrimaryText() });
            if (!string.IsNullOrWhiteSpace(item.Description)) text.Add(new Label { Text = item.Description, FontSize = 11, TextColor = GetSecondaryText() });
            text.Add(new Label { Text = item.ActivityDateUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"), FontSize = 9, TextColor = GetSecondaryText() });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(new GridLength(24)), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
            grid.Add(dot); grid.Add(text, 1, 0);
            DetailContent.Add(new Border { Padding = 10, BackgroundColor = GetCardBg(), Stroke = new SolidColorBrush(GetBorder()), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) }, Content = grid });
        }
    }

    private void AddSection(string title, IEnumerable<(string label, string value)> rows)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = GetPrimaryText() });
        foreach (var row in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(new GridLength(112)), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
            grid.Add(new Label { Text = row.label, FontSize = 10, TextColor = GetSecondaryText() });
            grid.Add(new Label { Text = row.value, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = GetPrimaryText(), LineBreakMode = LineBreakMode.WordWrap }, 1, 0);
            stack.Add(grid);
        }
        DetailContent.Add(new Border { Padding = 15, BackgroundColor = GetCardBg(), Stroke = new SolidColorBrush(GetBorder()), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) }, Content = stack });
    }

    private void AddAddressCard(PartyAddress address)
    {
        var stack = new VerticalStackLayout { Spacing = 5 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = string.IsNullOrWhiteSpace(address.Label) ? address.AddressType : address.Label, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = GetPrimaryText() });
        if (address.IsDefault) header.Add(new Border { Padding = new Thickness(7,4), BackgroundColor = GetSoftBg(), Stroke = new SolidColorBrush(Colors.Transparent), StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) }, Content = new Label { Text = "DEFAULT", FontSize = 8, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#718238") } }, 1, 0);
        stack.Add(header);
        var lines = new[] { address.AddressLine1, address.AddressLine2, address.Area, address.City, address.State, address.Pincode, address.Country }.Where(x => !string.IsNullOrWhiteSpace(x));
        stack.Add(new Label { Text = string.Join(", ", lines), FontSize = 12, TextColor = GetSecondaryText(), LineBreakMode = LineBreakMode.WordWrap });
        DetailContent.Add(new Border { Padding = 15, BackgroundColor = GetCardBg(), Stroke = new SolidColorBrush(GetBorder()), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) }, Content = stack });
    }

    private void SetTabVisuals()
    {
        var tabs = new[] { OverviewTab, BillingTab, ShippingTab, TimelineTab };
        foreach (var tab in tabs) tab.BackgroundColor = GetCardBg();
        if (_tab == "Overview") OverviewTab.BackgroundColor = Color.FromArgb("#718238");
        if (_tab == "Billing") BillingTab.BackgroundColor = Color.FromArgb("#718238");
        if (_tab == "Shipping") ShippingTab.BackgroundColor = Color.FromArgb("#718238");
        if (_tab == "Timeline") TimelineTab.BackgroundColor = Color.FromArgb("#718238");
    }

    private void OverviewTapped(object? sender, TappedEventArgs e) { _tab = "Overview"; RenderTab(); }
    private void BillingTapped(object? sender, TappedEventArgs e) { _tab = "Billing"; RenderTab(); }
    private void ShippingTapped(object? sender, TappedEventArgs e) { _tab = "Shipping"; RenderTab(); }
    private void TimelineTapped(object? sender, TappedEventArgs e) { _tab = "Timeline"; RenderTab(); }
    private void BackTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideActionMenu();
        BackClicked?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void ActionsTapped(
        object? sender,
        TappedEventArgs e)
    {
        ActionMenuBackdrop.IsVisible = true;
        ActionMenu.IsVisible = true;
        ActionMenu.Opacity = 0;
        ActionMenu.Scale = 0.97;

        _ = Task.WhenAll(
            ActionMenu.FadeToAsync(
                1,
                120,
                Easing.CubicOut),
            ActionMenu.ScaleToAsync(
                1,
                140,
                Easing.CubicOut));
    }

    private void ActionMenuBackdropTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideActionMenu();
    }

    private void HideActionMenu()
    {
        ActionMenuBackdrop.IsVisible = false;
        ActionMenu.IsVisible = false;
        ActionMenu.Opacity = 1;
        ActionMenu.Scale = 1;
    }

    private void EditActionClicked(
        object? sender,
        EventArgs e)
    {
        HideActionMenu();

        if (_party != null)
            EditClicked?.Invoke(
                this,
                _party.Id);
    }

    private async void DeleteActionClicked(
        object? sender,
        EventArgs e)
    {
        HideActionMenu();

        if (_party == null)
            return;

        var page = GetParentPage();
        if (page == null)
            return;

        var answer =
            await page.DisplayAlertAsync(
                "Delete party",
                $"Delete {_party.Name}? The party will be removed from the active list only if it has no linked transactions.",
                "Delete",
                "Cancel");

        if (!answer)
            return;

        var deleteCheck =
            await PartyRepository.CanDeleteAsync(
                _party.Id);

        if (!deleteCheck.Allowed)
        {
            await ToastService.Warning(
                deleteCheck.Reason);
            return;
        }

        if (await PartyRepository.DeleteAsync(
                _party.Id))
        {
            await ToastService.Success(
                "Party removed successfully.");

            Deleted?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    private ContentPage? GetParentPage()
    {
        Element? current = this;
        while (current != null && current is not ContentPage) current = current.Parent;
        return current as ContentPage;
    }

    private static string GetInitials(string name) { var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 0) return "P"; return parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(); }
    private static Color GetPrimaryText() => Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
    private static Color GetSecondaryText() => Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");
    private static Color GetCardBg() => Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#1A2016") : Color.FromArgb("#FFFFFF");
    private static Color GetBorder() => Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC");
    private static Color GetSoftBg() => Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#283021") : Color.FromArgb("#EEF2E5");
}
