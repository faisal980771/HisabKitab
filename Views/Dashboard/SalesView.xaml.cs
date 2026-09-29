using System.Globalization;
using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Shapes;

namespace HisabKitab.Views.Dashboard;

public partial class SalesView : ContentView
{
        private readonly List<BillDraftItem> _items = new();
    private List<BillDocument> _bills = new();
    private List<PartyAddress> _billingAddresses = new();
    private List<PartyAddress> _shippingAddresses = new();
    private Party? _selectedParty;
    private BillDocument? _detailBill;
    private List<BillItem> _detailItems = new();
    private List<BillPayment> _detailPayments = new();
    private string _documentType = "Sales";
    private BillTemplate? _selectedTemplate;
    private int _editingBillId;
    private bool _loaded, _loading, _editorOpen, _detailOpen, _searchBusy;
    private string _billingSelectedAddress = string.Empty;
    private string _shippingSelectedAddress = string.Empty;
    private string _addressModalType = "Billing";
    private CancellationTokenSource? _searchCts;

    public event EventHandler? BackClicked;
    public event EventHandler? BillSaved;
    public bool IsInnerPageOpen => _editorOpen || _detailOpen;

    public SalesView()
    {
        InitializeComponent();
        PaymentModePicker.ItemsSource = new[] { "Cash", "UPI", "Bank", "Card", "Credit" };
        DetailPaymentModePicker.ItemsSource = new[] { "Cash", "UPI", "Bank", "Card", "Other" };
        Loaded += SalesView_Loaded;
    }

    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var companyId = await SessionService.GetActiveCompanyIdAsync();
            if (!companyId.HasValue) return;
            await BillRepository.EnsureSchemaAsync();
            if (_loaded && !_editorOpen && !_detailOpen) await ShowBillSkeletonAsync();
            await LoadBillsAsync(companyId.Value, _documentType);
            _loaded = true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"BILL REFRESH ERROR: {ex}"); }
        finally { await HideBillSkeletonAsync(); _loading = false; }
    }

    private async Task ShowBillSkeletonAsync() { BillListContainer.IsVisible = false; BillEmptyState.IsVisible = false; BillSkeletonContainer.IsVisible = true; await Task.Yield(); }
    private async Task HideBillSkeletonAsync() { BillSkeletonContainer.IsVisible = false; BillListContainer.IsVisible = true; await Task.Yield(); }

    public async Task OpenNewAsync(string documentType, IEnumerable<int>? productIds = null) => await ShowEditorAsync(documentType, productIds?.ToList(), null);

    public async Task HandleBackAsync()
    {
        if (_detailOpen)
        {
            await CloseDetailAsync();
            return;
        }

        if (_editorOpen)
        {
            ShowListWithoutLeavingTab();
            return;
        }

        BackClicked?.Invoke(this, EventArgs.Empty);
    }

    private async void SalesView_Loaded(object? sender, EventArgs e) { if (!_loaded) await RefreshAsync(); }
    private async void SalesRefreshView_Refreshing(object? sender, EventArgs e) { await RefreshAsync(); SalesRefreshView.IsRefreshing = false; }

    // =========================================================
    // LIST / TABS / SEARCH
    // =========================================================
    private async void QuotationTabTapped(object? sender, TappedEventArgs e) => await ChangeDocumentTypeAsync("Quotation");
    private async void PurchaseTabTapped(object? sender, TappedEventArgs e) => await ChangeDocumentTypeAsync("Purchase");
    private async void SalesTabTapped(object? sender, TappedEventArgs e) => await ChangeDocumentTypeAsync("Sales");

    private async Task ChangeDocumentTypeAsync(string type)
    {
        if (_documentType == type && !_editorOpen && !_detailOpen) return;
        _documentType = type; ApplyTabVisuals();
        if (!_editorOpen && !_detailOpen)
        {
            var companyId = await SessionService.GetActiveCompanyIdAsync();
            if (companyId.HasValue) await LoadBillsAsync(companyId.Value, type);
        }
    }

    private async Task LoadBillsAsync(int companyId, string type)
    {
        _bills = await BillRepository.GetAllAsync(companyId, type);
        RenderBillList(_bills);
    }

    private void ApplyTabVisuals()
    {
        var selectedBg = Color.FromArgb("#718238");
        var unselectedBg = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#1A2016") : Colors.White;
        var text = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
        QuotationTab.BackgroundColor = _documentType == "Quotation" ? selectedBg : unselectedBg;
        PurchaseTab.BackgroundColor = _documentType == "Purchase" ? selectedBg : unselectedBg;
        SalesTab.BackgroundColor = _documentType == "Sales" ? selectedBg : unselectedBg;
        QuotationTabLabel.TextColor = _documentType == "Quotation" ? Colors.White : text;
        PurchaseTabLabel.TextColor = _documentType == "Purchase" ? Colors.White : text;
        SalesTabLabel.TextColor = _documentType == "Sales" ? Colors.White : text;
        BillListTitleLabel.Text = _documentType;
        BillListSubTitleLabel.Text = _documentType == "Quotation" ? "Customer quotations" : _documentType == "Purchase" ? "Purchase documents" : "Sales invoices";
    }

    private void BillSearchButtonTapped(object? sender, TappedEventArgs e)
    {
        var open = !BillSearchCard.IsVisible;
        BillSearchCard.IsVisible = open;
        BillSearchBackdrop.IsVisible = open;

        if (open)
        {
            BillSearchStatusLabel.Text = string.IsNullOrWhiteSpace(BillSearchEntry.Text) ? "Type to search" : "Searching result";
            BillSearchEntry.Focus();
            BillSearchEntry.CursorPosition = BillSearchEntry.Text?.Length ?? 0;
        }
        else
        {
            BillSearchEntry.Text = string.Empty;
            HideBillSearchOverlay();
        }
    }

    private async void BillSearchBackdropTapped(object? sender, TappedEventArgs e)
    {
        BillSearchEntry.Text = string.Empty;
        HideBillSearchOverlay();
        // Keep the already loaded list; no database reload needed.
        RenderBillList(_bills);
        await Task.CompletedTask;
    }

    private async void BillSearchEntry_Completed(object? sender, EventArgs e)
    {
        _searchCts?.Cancel();
        await SearchBillsAsync(BillSearchEntry.Text);
    }

    private async void BillSearchEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var term = e.NewTextValue?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(term))
        {
            BillSearchResultsContainer.Children.Clear();
            BillSearchResultsContainer.Parent?.SetValue(VisualElement.IsVisibleProperty, false);
            BillSearchStatusLabel.Text = "Type to search";
            BillSearchSpinner.IsRunning = false;
            BillSearchSpinner.IsVisible = false;
            return;
        }

        try
        {
            BillSearchStatusLabel.Text = "Searching result";
            BillSearchSpinner.IsVisible = true;
            BillSearchSpinner.IsRunning = true;
            await Task.Delay(320, token);
            if (token.IsCancellationRequested) return;
            await SearchBillsAsync(term, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BILL SEARCH ERROR: {ex}");
            BillSearchStatusLabel.Text = "Search failed";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                BillSearchSpinner.IsRunning = false;
                BillSearchSpinner.IsVisible = false;
            }
        }
    }

    private void RenderBillSearchResults(IEnumerable<BillDocument> bills, string term)
    {
        BillSearchResultsContainer.Children.Clear();
        var results = bills.Take(8).ToList();
        var parent = BillSearchResultsContainer.Parent as ScrollView;
        if (parent != null) parent.IsVisible = results.Count > 0;

        foreach (var bill in results)
        {
            var card = CreateBillCard(bill);
            var tap = card.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
            if (tap != null)
            {
                card.GestureRecognizers.Remove(tap);
                tap.Tapped += async (_, _) =>
                {
                    HideBillSearchOverlay();
                    await OpenDetailAsync(bill.Id);
                };
                card.GestureRecognizers.Add(tap);
            }
            BillSearchResultsContainer.Children.Add(card);
        }

        BillSearchStatusLabel.Text = results.Count == 0 ? $"No bills found for '{term}'" : $"{results.Count} result(s)";
    }

    private void HideBillSearchOverlay()
    {
        BillSearchCard.IsVisible = false;
        BillSearchBackdrop.IsVisible = false;
        var parent = BillSearchResultsContainer.Parent as ScrollView;
        if (parent != null) parent.IsVisible = false;
        BillSearchResultsContainer.Children.Clear();
        BillSearchSpinner.IsRunning = false;
        BillSearchSpinner.IsVisible = false;
    }

    private async Task SearchBillsAsync(string? term, CancellationToken token = default)
    {
        term = term?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
        {
            BillSearchResultsContainer.Children.Clear();
            BillSearchResultsContainer.Parent?.SetValue(VisualElement.IsVisibleProperty, false);
            BillSearchStatusLabel.Text = "Type to search";
            return;
        }

        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        var result = await BillRepository.SearchAsync(companyId.Value, _documentType, term);
        if (token.IsCancellationRequested) return;

        RenderBillSearchResults(result, term);
    }

    private void RenderBillList(IEnumerable<BillDocument> source)
    {
        ApplyTabVisuals(); var list = source.ToList(); BillCountLabel.Text = list.Count.ToString();
        BillListContainer.Children.Clear(); BillEmptyState.IsVisible = list.Count == 0 && !BillSkeletonContainer.IsVisible;
        foreach (var bill in list) BillListContainer.Children.Add(CreateBillCard(bill));
        ListContent.IsVisible = true; EditorContent.IsVisible = false; DetailContent.IsVisible = false; AddBillFab.IsVisible = true;
        HideBillActionMenu(); HideDetailActionMenu(); HideTemplateMenu();
    }

    private View CreateBillCard(BillDocument bill)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var card = new Border { BackgroundColor = dark ? Color.FromArgb("#1A2016") : Colors.White, Stroke = dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) }, Padding = 13 };
        var statusColor = bill.PaymentStatus == "Paid" ? Color.FromArgb("#4F9D5F") : bill.PaymentStatus == "Partial" ? Color.FromArgb("#D9943B") : Color.FromArgb("#C95D5D");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(100)) }, ColumnSpacing = 10 };
        var left = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        left.Children.Add(new Label { Text = bill.PartyName, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C"), LineBreakMode = LineBreakMode.TailTruncation });
        left.Children.Add(new Label { Text = $"{bill.DocumentNo} • {bill.DocumentDate:dd/MM/yyyy}", FontSize = 10, TextColor = dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B") });
        left.Children.Add(new Label { Text = string.IsNullOrWhiteSpace(bill.PartyMobile) ? bill.PartyEmail ?? "" : bill.PartyMobile, FontSize = 10, TextColor = dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B") });
        grid.Add(left, 0, 0);
        var right = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        right.Children.Add(new Label { Text = Money(bill.GrandTotal), FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C"), HorizontalTextAlignment = TextAlignment.End });
        if (bill.DocumentType != "Quotation")
            right.Children.Add(new Label { Text = $"Balance {Money(Math.Max(0, bill.GrandTotal - bill.PaidAmount))}", FontSize = 9, FontAttributes = FontAttributes.Bold, TextColor = statusColor, HorizontalTextAlignment = TextAlignment.End });
        right.Children.Add(new Label { Text = bill.DocumentType == "Quotation" ? "QUOTATION" : bill.PaymentStatus.ToUpperInvariant(), FontSize = 9, FontAttributes = FontAttributes.Bold, TextColor = statusColor, HorizontalTextAlignment = TextAlignment.End });
        grid.Add(right, 1, 0); card.Content = grid;
        var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await OpenDetailAsync(bill.Id); card.GestureRecognizers.Add(tap); return card;
    }

    private static bool Contains(string? value, string search) => !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    private static string Money(decimal value) => InvoiceFormatService.Money(value);

    // =========================================================
    // CREATE MENU
    // =========================================================
    private void AddBillFabTapped(object? sender, TappedEventArgs e) { BillActionBackdrop.IsVisible = true; BillActionMenu.IsVisible = true; }
    private void BillActionBackdropTapped(object? sender, TappedEventArgs e) => HideBillActionMenu();
    private void HideBillActionMenu() { BillActionBackdrop.IsVisible = false; BillActionMenu.IsVisible = false; }
    private async void CreateQuotationTapped(object? sender, EventArgs e) { HideBillActionMenu(); await ShowEditorAsync("Quotation", null, null); }
    private async void CreatePurchaseTapped(object? sender, EventArgs e) { HideBillActionMenu(); await ShowEditorAsync("Purchase", null, null); }
    private async void CreateSalesTapped(object? sender, EventArgs e) { HideBillActionMenu(); await ShowEditorAsync("Sales", null, null); }

    // =========================================================
    // EDITOR / PARTY
    // =========================================================
    private async Task ShowEditorAsync(string type, List<int>? productIds, BillDocument? editBill)
    {
        _documentType = type; _editingBillId = editBill?.Id ?? 0; _editorOpen = true; _detailOpen = false; _selectedParty = null; _items.Clear();
        _billingAddresses.Clear(); _shippingAddresses.Clear(); PartySuggestionsContainer.Children.Clear(); ProductSuggestionsContainer.Children.Clear(); ProductSuggestionCard.IsVisible = false; PartySuggestionsContainer.IsVisible = false;
        PartySearchEntry.Text = string.Empty; PartyNameEntry.Text = string.Empty; PartyMobileEntry.Text = string.Empty; PartyEmailEntry.Text = string.Empty;
        _billingSelectedAddress = string.Empty; _shippingSelectedAddress = string.Empty; _addressModalType = "Billing";
        BillingAddressSelectedLabel.Text = "No billing address • Tap to add"; ShippingAddressSelectedLabel.Text = "No shipping address • Tap to add";
        AddressModalEntry.Text = string.Empty; AddressModalOptionsContainer.Children.Clear(); AddressModalBackdrop.IsVisible = false; AddressModal.IsVisible = false;
        AmountWordsLabel.Text = InvoiceFormatService.AmountInWords(0);
        BillDatePicker.Date = DateTime.Today; DueDatePicker.Date = DateTime.Today.AddDays(type == "Purchase" ? 0 : 30); BillNotesEditor.Text = string.Empty; PaymentNowEntry.Text = string.Empty; PaymentModePicker.SelectedIndex = 0;
        PartyTypeHintLabel.Text = type == "Purchase" ? "Supplier" : "Customer"; EditorTitleLabel.Text = editBill == null ? (type == "Quotation" ? "Create Quotation" : type == "Purchase" ? "Create Purchase" : "Create Sales Invoice") : $"Edit {type}"; EditorSubtitleLabel.Text = type == "Quotation" ? "Customer quotation" : type == "Purchase" ? "Purchase bill" : "Sales invoice";
        _selectedTemplate = null;
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (companyId.HasValue) _selectedTemplate = editBill?.TemplateId > 0 ? (await BillRepository.GetTemplatesAsync(companyId.Value, type)).FirstOrDefault(x => x.Id == editBill.TemplateId) : await BillRepository.GetDefaultTemplateAsync(companyId.Value, type);
        PaymentSection.IsVisible = type != "Quotation";
        PaymentAmountLabel.Text = type == "Purchase" ? "Payment paid" : "Payment received";
        SaveBillButton.Text = editBill == null ? "Save Bill" : "Update Bill";

        if (editBill != null)
        {
            _detailBill = editBill; _selectedParty = await PartyRepository.GetByIdAsync(editBill.PartyId);
            PartySearchEntry.Text = editBill.PartyName; PartyNameEntry.Text = editBill.PartyName; PartyMobileEntry.Text = editBill.PartyMobile; PartyEmailEntry.Text = editBill.PartyEmail;
            BillDatePicker.Date = editBill.DocumentDate; DueDatePicker.Date = editBill.DueDate ?? editBill.DocumentDate; BillNotesEditor.Text = editBill.Notes;
            if (_selectedParty != null) await LoadPartyAddressesAsync(_selectedParty.Id);
            var billItems = await BillRepository.GetItemsAsync(editBill.Id); var all = companyId.HasValue ? await ProductRepository.GetAllAsync(companyId.Value) : new List<Product>();
            foreach (var item in billItems)
            {
                var product = all.FirstOrDefault(x => x.Id == item.ProductId);
                if (product != null)
                {
                    var useAmount = item.DiscountPercent <= 0 && item.DiscountAmount > 0;
                    _items.Add(new BillDraftItem
                    {
                        Product = product,
                        Quantity = item.Quantity,
                        Rate = item.Rate,
                        DiscountMode = useAmount ? "Amount" : "Percent",
                        DiscountInput = useAmount ? item.DiscountAmount : item.DiscountPercent,
                        DiscountPercent = item.DiscountPercent,
                        DiscountAmount = item.DiscountAmount,
                        TaxAmount = item.TaxAmount,
                        NetAmount = item.NetAmount
                    });
                }
            }
        }
        else if (productIds != null && companyId.HasValue)
        {
            var all = await ProductRepository.GetAllAsync(companyId.Value); foreach (var id in productIds) { var product = all.FirstOrDefault(x => x.Id == id); if (product != null) AddProductToDraft(product); }
        }
        ApplyEditorVisibility(); RenderDraftItems(); UpdateTotals();
    }

    private void ApplyEditorVisibility()
    {
        ListContent.IsVisible = !_editorOpen && !_detailOpen; EditorContent.IsVisible = _editorOpen; DetailContent.IsVisible = _detailOpen; AddBillFab.IsVisible = !_editorOpen && !_detailOpen;
        BillActionBackdrop.IsVisible = false; BillActionMenu.IsVisible = false;
    }

    private void ShowListWithoutLeavingTab() { _editorOpen = false; _detailOpen = false; _editingBillId = 0; HideBillActionMenu(); HideDetailActionMenu(); HideTemplateMenu(); BillSearchCard.IsVisible = false; BillSearchBackdrop.IsVisible = false; ApplyEditorVisibility(); RenderBillList(_bills); }

    private async Task CloseDetailAsync()
    {
        var distance = Math.Max(Width, 420);
        await DetailContent.TranslateToAsync(distance, 0, 240, Easing.CubicIn);
        ShowListWithoutLeavingTab();
        DetailContent.TranslationX = 0;
    }

    private async void BackFromBillClicked(object? sender, TappedEventArgs e)
    {
        if (_detailOpen)
        {
            await CloseDetailAsync();
            return;
        }

        ShowListWithoutLeavingTab();
    }

    private async void PartySearchEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel(); _searchCts = new CancellationTokenSource(); var token = _searchCts.Token; var text = e.NewTextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) { PartySuggestionsContainer.IsVisible = false; return; }
        try
        {
            await Task.Delay(220, token); var companyId = await SessionService.GetActiveCompanyIdAsync(); if (!companyId.HasValue) return; var partyType = _documentType == "Purchase" ? "Supplier" : "Customer"; var list = await PartyRepository.SearchAsync(companyId.Value, text, partyType);
            PartySuggestionsContainer.Children.Clear(); foreach (var party in list.Take(8))
            {
                var card = new Border { Padding = 10, BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#222A1D") : Color.FromArgb("#F5F7F0"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) } };
                card.Content = new Label { Text = $"{party.Name} • {party.Mobile ?? party.Email ?? "No contact"}", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C") };
                var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await SelectPartyAsync(party); card.GestureRecognizers.Add(tap); PartySuggestionsContainer.Children.Add(card);
            }
            PartySuggestionsContainer.IsVisible = list.Count > 0;
        }
        catch (OperationCanceledException) { } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PARTY SUGGESTION ERROR: {ex}"); }
    }

    private async Task SelectPartyAsync(Party party)
    {
        _selectedParty = party; PartySearchEntry.Text = party.Name; PartyNameEntry.Text = party.Name; PartyMobileEntry.Text = party.Mobile; PartyEmailEntry.Text = party.Email; PartySuggestionsContainer.IsVisible = false; await LoadPartyAddressesAsync(party.Id);
    }

    private async Task LoadPartyAddressesAsync(int partyId)
    {
        _billingAddresses = await PartyAddressRepository.GetAllAsync(partyId, "Billing");
        _shippingAddresses = await PartyAddressRepository.GetAllAsync(partyId, "Shipping");

        var billingDefault = _billingAddresses.FirstOrDefault(x => x.IsDefault) ?? _billingAddresses.FirstOrDefault();
        var shippingDefault = _shippingAddresses.FirstOrDefault(x => x.IsDefault) ?? _shippingAddresses.FirstOrDefault();

        _billingSelectedAddress = billingDefault == null ? string.Empty : FormatAddress(billingDefault);
        _shippingSelectedAddress = shippingDefault == null ? string.Empty : FormatAddress(shippingDefault);

        BillingAddressSelectedLabel.Text = string.IsNullOrWhiteSpace(_billingSelectedAddress) ? "No billing address • Tap to add" : _billingSelectedAddress;
        ShippingAddressSelectedLabel.Text = string.IsNullOrWhiteSpace(_shippingSelectedAddress) ? "No shipping address • Tap to add" : _shippingSelectedAddress;
    }

    private async void BillingAddressSelectorTapped(object? sender, TappedEventArgs e) => await OpenAddressModalAsync("Billing");
    private async void ShippingAddressSelectorTapped(object? sender, TappedEventArgs e) => await OpenAddressModalAsync("Shipping");

    private async Task OpenAddressModalAsync(string type)
    {
        _addressModalType = type;
        AddressModalTitleLabel.Text = type == "Billing" ? "Select Billing Address" : "Select Shipping Address";
        AddressModalSubtitleLabel.Text = _selectedParty == null
            ? "Enter an address below. It will be saved when the bill is saved."
            : "Choose a saved address or enter a new one for this party.";

        AddressModalOptionsContainer.Children.Clear();
        var list = type == "Billing" ? _billingAddresses : _shippingAddresses;
        var selected = type == "Billing" ? _billingSelectedAddress : _shippingSelectedAddress;

        if (list.Count == 0)
        {
            AddressModalOptionsContainer.Children.Add(new Label
            {
                Text = "No saved addresses yet. Add one below.",
                FontSize = 11,
                TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8, 0, 4)
            });
        }
        else
        {
            foreach (var address in list)
            {
                var text = FormatAddress(address);
                var chosen = string.Equals(text, selected, StringComparison.OrdinalIgnoreCase);
                var card = new Border
                {
                    Padding = 11,
                    BackgroundColor = chosen ? Color.FromArgb("#EEF2E5") : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#20271B") : Color.FromArgb("#F7F9F3")),
                    Stroke = chosen ? Color.FromArgb("#718238") : (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC")),
                    StrokeThickness = chosen ? 1.5 : 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }
                };
                var inner = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(28)) }, ColumnSpacing = 6 };
                inner.Add(new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = string.IsNullOrWhiteSpace(address.Label) ? type : address.Label, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#718238") },
                        new Label { Text = text, FontSize = 11, TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C"), LineBreakMode = LineBreakMode.WordWrap }
                    }
                }, 0, 0);
                inner.Add(new Label { Text = chosen ? "✓" : "›", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#718238"), VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center }, 1, 0);
                card.Content = inner;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => SelectSavedAddress(address);
                card.GestureRecognizers.Add(tap);
                AddressModalOptionsContainer.Children.Add(card);
            }
        }

        AddressModalEntry.Text = selected;
        AddressModalBackdrop.IsVisible = true;
        AddressModal.IsVisible = true;
        AddressModal.Opacity = 0;
        AddressModal.Scale = 0.96;
        AddressModal.TranslationY = 18;
        await Task.WhenAll(
            AddressModal.FadeToAsync(1, 150, Easing.CubicOut),
            AddressModal.ScaleToAsync(1, 180, Easing.CubicOut),
            AddressModal.TranslateToAsync(0, 0, 200, Easing.CubicOut));
        AddressModalEntry.Focus();
    }

    private async void SelectSavedAddress(PartyAddress address)
    {
        var text = FormatAddress(address);
        if (_addressModalType == "Billing")
        {
            _billingSelectedAddress = text;
            BillingAddressSelectedLabel.Text = text;
        }
        else
        {
            _shippingSelectedAddress = text;
            ShippingAddressSelectedLabel.Text = text;
        }
        await HideAddressModalAnimatedAsync();
    }

    private async void SaveAddressModalClicked(object? sender, EventArgs e)
    {
        var text = AddressModalEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            await ToastService.Warning("Please enter an address.");
            return;
        }

        if (_addressModalType == "Billing")
        {
            _billingSelectedAddress = text;
            BillingAddressSelectedLabel.Text = text;
        }
        else
        {
            _shippingSelectedAddress = text;
            ShippingAddressSelectedLabel.Text = text;
        }

        // Persist immediately when a party is already selected. New parties are persisted in SaveBillClicked.
        if (_selectedParty != null)
        {
            await PersistAddressIfNewAsync(_selectedParty, _addressModalType, text);
        }
        await HideAddressModalAnimatedAsync();
    }

    private async Task PersistAddressIfNewAsync(Party party, string type, string text)
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue || string.IsNullOrWhiteSpace(text)) return;
        var source = type == "Billing" ? _billingAddresses : _shippingAddresses;
        if (source.Any(x => string.Equals(FormatAddress(x), text, StringComparison.OrdinalIgnoreCase))) return;

        await PartyAddressRepository.AddAsync(new PartyAddress
        {
            CompanyId = companyId.Value,
            PartyId = party.Id,
            AddressType = type,
            Label = type == "Billing" ? "Invoice Billing" : "Invoice Shipping",
            AddressLine1 = text,
            Country = "India",
            IsDefault = source.Count == 0,
            IsActive = true
        });

        if (type == "Billing") _billingAddresses = await PartyAddressRepository.GetAllAsync(party.Id, "Billing");
        else _shippingAddresses = await PartyAddressRepository.GetAllAsync(party.Id, "Shipping");
    }

    private void AddressModalBackdropTapped(object? sender, TappedEventArgs e) => _ = HideAddressModalAnimatedAsync();
    private void AddressModalCloseButtonClicked(object? sender, EventArgs e) => _ = HideAddressModalAnimatedAsync();
    private async Task HideAddressModalAnimatedAsync()
    {
        if (!AddressModal.IsVisible) return;
        await Task.WhenAll(
            AddressModal.FadeToAsync(0, 100, Easing.CubicIn),
            AddressModal.ScaleToAsync(0.97, 110, Easing.CubicIn),
            AddressModal.TranslateToAsync(0, 12, 120, Easing.CubicIn));
        AddressModalBackdrop.IsVisible = false;
        AddressModal.IsVisible = false;
        AddressModal.Opacity = 1;
        AddressModal.Scale = 1;
        AddressModal.TranslationY = 0;
    }

    private async Task<string?> ResolveAddressForSaveAsync(int companyId, Party party, string type)
    {
        var selectedText = type == "Billing" ? _billingSelectedAddress : _shippingSelectedAddress;
        var source = type == "Billing" ? _billingAddresses : _shippingAddresses;
        if (!string.IsNullOrWhiteSpace(selectedText))
        {
            var existing = source.FirstOrDefault(x => string.Equals(FormatAddress(x), selectedText, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return FormatAddress(existing);
            await PartyAddressRepository.AddAsync(new PartyAddress
            {
                CompanyId = companyId,
                PartyId = party.Id,
                AddressType = type,
                Label = type == "Billing" ? "Invoice Billing" : "Invoice Shipping",
                AddressLine1 = selectedText,
                Country = "India",
                IsDefault = source.Count == 0,
                IsActive = true
            });
            if (type == "Billing") _billingAddresses = await PartyAddressRepository.GetAllAsync(party.Id, "Billing");
            else _shippingAddresses = await PartyAddressRepository.GetAllAsync(party.Id, "Shipping");
            return selectedText;
        }
        return null;
    }

    private static string FormatAddress(PartyAddress a) => string.Join(", ", new[] { a.Label, a.AddressLine1, a.AddressLine2, a.Area, a.City, a.State, a.Pincode, a.Country }.Where(x => !string.IsNullOrWhiteSpace(x)));

    // =========================================================
    // PRODUCT SEARCH / ITEMS
    // =========================================================
    private async void ProductSearchEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel(); _searchCts = new CancellationTokenSource(); var token = _searchCts.Token; var text = e.NewTextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) { ProductSuggestionsContainer.Children.Clear(); ProductSuggestionCard.IsVisible = false; return; }
        try
        {
            await Task.Delay(180, token); var companyId = await SessionService.GetActiveCompanyIdAsync(); if (!companyId.HasValue) return; var all = await ProductRepository.GetAllAsync(companyId.Value);
            var list = all.Where(x => Contains(x.Name,text) || Contains(x.ProductCode,text) || Contains(x.Barcode,text) || Contains(x.Category,text) || Contains(x.HSN,text) || Contains(x.Unit,text)).Take(10).ToList();
            ProductSuggestionsContainer.Children.Clear();
            foreach (var product in list)
            {
                var card = new Border { Padding = 10, BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#222A1D") : Color.FromArgb("#F5F7F0"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) } };
                card.Content = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(92)) }, ColumnSpacing = 8, Children = { new Label { Text = $"{product.Name}\n{product.ProductCode} • {product.Unit}", FontSize = 11, FontAttributes = FontAttributes.Bold }, new Label { Text = $"₹ {( _documentType == "Purchase" ? product.PurchaseRate : product.SaleRate):0.##}", FontSize = 11, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#718238") } } };
                var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => AddProductToDraft(product); card.GestureRecognizers.Add(tap); ProductSuggestionsContainer.Children.Add(card);
            }
            ProductSuggestionCard.IsVisible = list.Count > 0;
        }
        catch (OperationCanceledException) { } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PRODUCT SUGGESTION ERROR: {ex}"); }
    }

    private async void ProductSearchEntry_Completed(object? sender, EventArgs e)
    {
        var text = ProductSearchEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        var all = await ProductRepository.GetAllAsync(companyId.Value);
        var product = all.FirstOrDefault(x =>
            string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ProductCode, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Barcode, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.HSN, text, StringComparison.OrdinalIgnoreCase));

        if (product != null)
            AddProductToDraft(product);
        else
            await ToastService.Info("No matching product found.");
    }

    private void ProductSuggestionsClear() { ProductSuggestionsContainer.Children.Clear(); ProductSuggestionCard.IsVisible = false; }
    private void AddProductToDraft(Product product)
    {
        var existing = _items.FirstOrDefault(x => x.ProductId == product.Id);

        if (existing != null)
            existing.Quantity += 1;
        else
            _items.Add(new BillDraftItem
            {
                Product = product,
                Quantity = 1,
                Rate = _documentType == "Purchase" ? product.PurchaseRate : product.SaleRate,
                DiscountMode = "Percent",
                DiscountInput = 0
            });

        ProductSearchEntry.Text = string.Empty;
        ProductSuggestionsClear();
        RenderDraftItems();
        UpdateTotals();
    }

    private void RenderDraftItems()
    {
        BillItemsContainer.Children.Clear();
        NoItemsLabel.IsVisible = _items.Count == 0;

        foreach (var item in _items)
        {
            RecalculateDraftItem(item);

            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var textColor = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
            var secondary = dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");

            var card = new Border
            {
                Padding = new Thickness(9, 8),
                BackgroundColor = dark ? Color.FromArgb("#20271B") : Color.FromArgb("#F7F9F3"),
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }
            };

            var root = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                RowSpacing = 7
            };

            var top = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(28))
                },
                ColumnSpacing = 4
            };

            var productInfo = new VerticalStackLayout { Spacing = 1 };
            productInfo.Children.Add(new Label
            {
                Text = item.Product.Name,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                LineBreakMode = LineBreakMode.WordWrap
            });
            productInfo.Children.Add(new Label
            {
                Text = $"{item.Product.ProductCode} • {item.Product.Unit} • GST {item.Product.GSTPercent:0.##}%",
                FontSize = 8,
                TextColor = secondary
            });
            top.Add(productInfo, 0, 0);

            var remove = new Button
            {
                Text = "×",
                WidthRequest = 28,
                HeightRequest = 28,
                Padding = 0,
                CornerRadius = 9,
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#C95D5D"),
                FontAttributes = FontAttributes.Bold,
                FontSize = 16
            };
            remove.Clicked += (_, _) =>
            {
                _items.Remove(item);
                RenderDraftItems();
                UpdateTotals();
            };
            top.Add(remove, 1, 0);

            root.Add(top, 0, 0);

            var controls = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(new GridLength(55)),
                    new ColumnDefinition(new GridLength(76)),
                    new ColumnDefinition(new GridLength(96)),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 6
            };

            var amountLabel = new Label
            {
                Text = Money(item.NetAmount),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = Color.FromArgb("#718238")
            };

            var qty = MakeNumericEntry(item.Quantity, "Qty");
            qty.TextChanged += (_, _) =>
            {
                if (decimal.TryParse(qty.Text?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
                {
                    item.Quantity = v;
                    RecalculateDraftItem(item);
                    amountLabel.Text = Money(item.NetAmount);
                    UpdateTotals();
                }
            };
            controls.Add(FieldWithLabel("Qty", qty), 0, 0);

            var rate = MakeNumericEntry(item.Rate, "Rate");
            rate.TextChanged += (_, _) =>
            {
                if (decimal.TryParse(rate.Text?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0)
                {
                    item.Rate = v;
                    RecalculateDraftItem(item);
                    amountLabel.Text = Money(item.NetAmount);
                    UpdateTotals();
                }
            };
            controls.Add(FieldWithLabel("Rate", rate), 1, 0);

            var discountGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(new GridLength(38)),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 4
            };

            var discountPicker = new Picker
            {
                ItemsSource = new[] { "%", "₹" },
                SelectedIndex = item.DiscountMode == "Amount" ? 1 : 0,
                HeightRequest = 36,
                FontSize = 10
            };
            discountPicker.SelectedIndexChanged += (_, _) =>
            {
                item.DiscountMode = discountPicker.SelectedIndex == 1 ? "Amount" : "Percent";
                if (item.DiscountMode == "Percent" && item.DiscountInput == 0 && item.DiscountAmount > 0)
                    item.DiscountInput = item.DiscountPercent;
                RecalculateDraftItem(item);
                RenderDraftItems();
                UpdateTotals();
            };
            discountGrid.Add(discountPicker, 0, 0);

            var disc = MakeNumericEntry(item.DiscountInput, "0");
            disc.TextChanged += (_, _) =>
            {
                if (decimal.TryParse(disc.Text?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0)
                {
                    item.DiscountInput = v;
                    RecalculateDraftItem(item);
                    amountLabel.Text = Money(item.NetAmount);
                    UpdateTotals();
                }
            };
            discountGrid.Add(disc, 1, 0);
            controls.Add(FieldWithLabel("Discount", discountGrid), 2, 0);

            amountLabel.Text = Money(item.NetAmount);
            controls.Add(FieldWithLabel("Amount", amountLabel), 3, 0);

            root.Add(controls, 0, 1);
            card.Content = root;
            BillItemsContainer.Children.Add(card);
        }
    }

    private static Entry MakeNumericEntry(decimal value, string placeholder)
    {
        return new Entry
        {
            Text = value.ToString("0.##", CultureInfo.InvariantCulture),
            Placeholder = placeholder,
            Keyboard = Keyboard.Numeric,
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            HeightRequest = 36,
            Margin = 0
        };
    }

    private static VerticalStackLayout FieldWithLabel(string label, View field)
    {
        var stack = new VerticalStackLayout { Spacing = 1 };
        stack.Children.Add(new Label
        {
            Text = label,
            FontSize = 8,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#737A6B")
        });
        stack.Children.Add(field);
        return stack;
    }

    private void RecalculateDraftItem(BillDraftItem item)
    {
        var gross = Math.Round(item.Quantity * item.Rate, 2);

        if (item.DiscountMode == "Amount")
        {
            item.DiscountAmount = Math.Min(gross, Math.Max(0, item.DiscountInput));
            item.DiscountPercent = gross > 0 ? Math.Round(item.DiscountAmount / gross * 100m, 4) : 0;
        }
        else
        {
            item.DiscountPercent = Math.Max(0, item.DiscountInput);
            item.DiscountAmount = Math.Round(gross * item.DiscountPercent / 100m, 2);
            if (item.DiscountAmount > gross) item.DiscountAmount = gross;
        }

        var taxable = Math.Max(0, gross - item.DiscountAmount);
        item.TaxAmount = Math.Round(taxable * item.Product.GSTPercent / 100m, 2);
        item.NetAmount = Math.Round(taxable + item.TaxAmount, 2);
    }

    private void UpdateTotals()
    {
        foreach (var item in _items) RecalculateDraftItem(item);

        var subtotal = Math.Round(_items.Sum(x => x.Quantity * x.Rate), 2);
        var discount = Math.Round(_items.Sum(x => x.DiscountAmount), 2);
        var tax = Math.Round(_items.Sum(x => x.TaxAmount), 2);
        var grand = Math.Round(subtotal - discount + tax, 2);

        SubTotalLabel.Text = Money(subtotal);
        DiscountTotalLabel.Text = Money(discount);
        TaxTotalLabel.Text = Money(tax);
        GrandTotalLabel.Text = Money(grand);
        AmountWordsLabel.Text = InvoiceFormatService.AmountInWords(grand);
    }

    // =========================================================
    // SAVE / EDIT
    // =========================================================
    private async void SaveBillClicked(object? sender, EventArgs e)
    {
        if (_items.Count == 0) { await ToastService.Warning("Please add at least one product."); return; }
        var name = PartyNameEntry.Text?.Trim() ?? string.Empty; var mobile = PartyMobileEntry.Text?.Trim(); var email = PartyEmailEntry.Text?.Trim(); var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) { await ToastService.Error("No active company selected."); return; }
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(mobile)) { await ToastService.Warning("Please enter party name or mobile."); PartySearchEntry.Focus(); return; }
        var partyType = _documentType == "Purchase" ? "Supplier" : "Customer"; var oldItems = _editingBillId > 0 ? await BillRepository.GetItemsAsync(_editingBillId) : new List<BillItem>(); var isEdit = _editingBillId > 0;
        try
        {
            SaveBillButton.IsEnabled = false;
            await BillSaveLoader.RunAsync(async () =>
            {
                var party = _selectedParty ?? await BillRepository.ResolveOrCreatePartyAsync(companyId.Value, partyType, name, mobile, email); _selectedParty = party;
                var billingText = await ResolveAddressForSaveAsync(companyId.Value, party, "Billing");
                var shippingText = await ResolveAddressForSaveAsync(companyId.Value, party, "Shipping");
                var doc = new BillDocument { Id = _editingBillId, CompanyId = companyId.Value, DocumentType = _documentType, DocumentDate = BillDatePicker.Date ?? DateTime.Today, DueDate = DueDatePicker.Date, PartyId = party.Id, PartyType = partyType, PartyName = party.Name, PartyMobile = party.Mobile, PartyEmail = party.Email, BillingAddress = billingText, ShippingAddress = shippingText, PartyGSTIN = party.GSTIN, PartyPAN = party.PAN, ContactPerson = party.ContactPerson, SubTotal = _items.Sum(x=>x.Quantity*x.Rate), DiscountTotal = _items.Sum(x=>x.DiscountAmount), TaxTotal = _items.Sum(x=>x.TaxAmount), GrandTotal = Math.Round(_items.Sum(x=>x.NetAmount),2), PaidAmount = _editingBillId > 0 && _detailBill != null ? _detailBill.PaidAmount : 0, PaymentStatus = _editingBillId > 0 && _detailBill != null ? _detailBill.PaymentStatus : "Unpaid", Notes = BillNotesEditor.Text?.Trim(), TemplateId = _selectedTemplate?.Id ?? 0, Status = _documentType == "Quotation" ? "Draft" : "Confirmed" };
                if (isEdit && _detailBill != null) { doc.DocumentNo = _detailBill.DocumentNo; doc.CreatedAtUtc = _detailBill.CreatedAtUtc; }
                var billItems = _items.Select(x => new BillItem { ProductId=x.Product.Id, ProductName=x.Product.Name, ProductCode=x.Product.ProductCode, ProductBarcode=x.Product.Barcode, ProductHSN=x.Product.HSN, ProductImagePath=x.Product.ImagePath, Unit=x.Product.Unit, Quantity=x.Quantity, Rate=x.Rate, DiscountPercent=x.DiscountPercent, DiscountAmount=x.DiscountAmount, TaxPercent=x.Product.GSTPercent, TaxAmount=x.TaxAmount, NetAmount=x.NetAmount }).ToList();
                decimal payment = 0; string? paymentMode = null; if (_documentType != "Quotation") { decimal.TryParse(PaymentNowEntry.Text?.Replace(",",""), NumberStyles.Any, CultureInfo.InvariantCulture, out payment); paymentMode = PaymentModePicker.SelectedItem?.ToString(); }
                await BillRepository.SaveAsync(doc, billItems, payment, paymentMode, null);
                if (!isEdit && _documentType is "Purchase" or "Sales") await ApplyStockForNewBillAsync(doc, billItems);
                else if (isEdit && _documentType is "Purchase" or "Sales") await ApplyStockCorrectionAsync(doc, oldItems, billItems);
                await PartyActivityRepository.AddAsync(new PartyActivity { CompanyId=companyId.Value, PartyId=party.Id, ActivityType=isEdit ? "BILL_UPDATE" : _documentType=="Sales" ? "INVOICE" : _documentType.ToUpperInvariant(), RefType="BillDocument", RefId=doc.Id, Title=$"{_documentType} {doc.DocumentNo}", Description=isEdit ? $"{_documentType} updated." : $"{_documentType} created.", Amount=doc.GrandTotal, ActivityDateUtc=doc.DocumentDate.ToUniversalTime() });
                _detailBill = doc;
            }, _documentType == "Quotation" ? (isEdit ? "Updating quotation..." : "Saving quotation...") : _documentType == "Purchase" ? (isEdit ? "Updating purchase..." : "Saving purchase...") : (isEdit ? "Updating sales invoice..." : "Saving sales invoice..."));
            _items.Clear(); _editorOpen=false; ApplyEditorVisibility(); await RefreshAsync(); BillSaved?.Invoke(this, EventArgs.Empty); await ToastService.Success(isEdit ? "Bill updated successfully." : $"{_documentType} saved successfully.");
        }
        catch (Exception ex) { await ToastService.Error(ex.Message); }
        finally { SaveBillButton.IsEnabled = true; }
    }

    private async Task ApplyStockForNewBillAsync(BillDocument bill, List<BillItem> items)
    {
        foreach (var item in items) await StockRepository.AddAsync(new StockTransaction { CompanyId=bill.CompanyId, ProductId=item.ProductId, TransactionType=bill.DocumentType=="Purchase" ? "IN" : "SALE", Quantity=item.Quantity, Rate=item.Rate, Amount=item.Quantity*item.Rate, TransactionDate=bill.DocumentDate, Notes=bill.DocumentNo });
    }

    private async Task ApplyStockCorrectionAsync(BillDocument bill, List<BillItem> oldItems, List<BillItem> newItems)
    {
        var oldMap = oldItems.GroupBy(x=>x.ProductId).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Quantity)); var newMap = newItems.GroupBy(x=>x.ProductId).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Quantity));
        foreach (var id in oldMap.Keys.Union(newMap.Keys))
        {
            var delta = newMap.GetValueOrDefault(id) - oldMap.GetValueOrDefault(id); if (delta == 0) continue; var item = newItems.FirstOrDefault(x=>x.ProductId==id) ?? oldItems.First(x=>x.ProductId==id); var purchase = bill.DocumentType=="Purchase";
            var type = purchase ? (delta > 0 ? "IN" : "OUT") : (delta > 0 ? "SALE" : "IN");
            var qty = Math.Abs(delta); await StockRepository.AddAsync(new StockTransaction { CompanyId=bill.CompanyId, ProductId=id, TransactionType=type, Quantity=qty, Rate=item.Rate, Amount=qty*item.Rate, TransactionDate=bill.DocumentDate, Notes=$"Correction {bill.DocumentNo}" });
        }
    }

    // =========================================================
    // DETAIL / PAYMENT / EDIT / DELETE
    // =========================================================
    private async Task OpenDetailAsync(int billId)
    {
        var bill = await BillRepository.GetByIdAsync(billId);
        if (bill == null) return;

        _detailBill = bill;
        _detailItems = await BillRepository.GetItemsAsync(billId);
        // Older bills may not have a product-image snapshot. Fall back to the current product master image.
        if (_detailItems.Any(x => string.IsNullOrWhiteSpace(x.ProductImagePath)))
        {
            var productIds = _detailItems.Where(x => x.ProductId > 0).Select(x => x.ProductId).Distinct().ToList();
            foreach (var productId in productIds)
            {
                var product = await ProductRepository.GetByIdAsync(productId);
                var item = _detailItems.FirstOrDefault(x => x.ProductId == productId && string.IsNullOrWhiteSpace(x.ProductImagePath));
                if (item != null && product != null) item.ProductImagePath = product.ImagePath;
            }
        }
        _detailPayments = await BillRepository.GetPaymentsAsync(billId);
        _detailOpen = true;
        _editorOpen = false;
        HideBillSearchOverlay();

        RenderDetail();
        ApplyEditorVisibility();
        await DetailScrollView.ScrollToAsync(0, 0, false);

        DetailContent.TranslationX = Math.Max(Width, 420);
        await Task.Yield();
        await DetailContent.TranslateToAsync(0, 0, 280, Easing.CubicOut);
    }

    private void RenderDetail()
    {
        if (_detailBill == null) return;

        var b = _detailBill;
        DetailTitleLabel.Text = b.DocumentType == "Sales" ? "Sales Invoice" : b.DocumentType;
        DetailNoLabel.Text = b.DocumentNo;
        DetailPartyNameLabel.Text = b.PartyName;
        DetailPartyMetaLabel.Text = string.IsNullOrWhiteSpace(b.PartyMobile)
            ? b.DocumentDate.ToString("dd/MM/yyyy")
            : $"{b.PartyMobile} • {b.DocumentDate:dd/MM/yyyy}";
        DetailStatusLabel.Text = b.DocumentType == "Quotation" ? "QUOTATION" : b.PaymentStatus.ToUpperInvariant();
        DetailGrandTotalLabel.Text = Money(b.GrandTotal);
        DetailBalanceLabel.Text = b.DocumentType == "Quotation" ? "Quotation" : $"Balance {Money(Math.Max(0, b.GrandTotal - b.PaidAmount))}";
        DetailAmountWordsLabel.Text = InvoiceFormatService.AmountInWords(b.GrandTotal);

        DetailPartyInfoLabel.Text = $"{b.PartyName} • {b.PartyType}";
        DetailMobileInfoLabel.Text = string.IsNullOrWhiteSpace(b.PartyMobile) ? "Not provided" : b.PartyMobile;
        DetailEmailInfoLabel.Text = string.IsNullOrWhiteSpace(b.PartyEmail) ? "Not provided" : b.PartyEmail;
        DetailDateInfoLabel.Text = b.DocumentDate.ToString("dd/MM/yyyy");
        DetailDueInfoLabel.Text = b.DueDate?.ToString("dd/MM/yyyy") ?? "Not set";
        DetailBillingInfoLabel.Text = string.IsNullOrWhiteSpace(b.BillingAddress) ? "Not provided" : b.BillingAddress;
        DetailShippingInfoLabel.Text = string.IsNullOrWhiteSpace(b.ShippingAddress) ? "Not provided" : b.ShippingAddress;
        DetailContactInfoLabel.Text = string.IsNullOrWhiteSpace(b.ContactPerson) ? "Not provided" : b.ContactPerson;
        DetailGstinInfoLabel.Text = string.IsNullOrWhiteSpace(b.PartyGSTIN) ? "Not provided" : b.PartyGSTIN;
        DetailPanInfoLabel.Text = string.IsNullOrWhiteSpace(b.PartyPAN) ? "Not provided" : b.PartyPAN;
        DetailNotesLabel.Text = string.IsNullOrWhiteSpace(b.Notes) ? "No notes" : b.Notes;
        DetailNotesCard.IsVisible = !string.IsNullOrWhiteSpace(b.Notes);

        DetailMobileInfoLabel.GestureRecognizers.Clear();
        DetailEmailInfoLabel.GestureRecognizers.Clear();

        if (!string.IsNullOrWhiteSpace(b.PartyMobile))
        {
            DetailMobileInfoLabel.TextColor = Color.FromArgb("#718238");
            DetailMobileInfoLabel.TextDecorations = TextDecorations.Underline;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                try { await Launcher.Default.OpenAsync($"tel:{b.PartyMobile.Trim()}"); }
                catch { await ToastService.Warning("Unable to open phone."); }
            };
            DetailMobileInfoLabel.GestureRecognizers.Add(tap);
        }
        else
        {
            DetailMobileInfoLabel.TextColor =
                Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#A8AF9E")
                    : Color.FromArgb("#737A6B");
            DetailMobileInfoLabel.TextDecorations = TextDecorations.None;
        }

        if (!string.IsNullOrWhiteSpace(b.PartyEmail))
        {
            DetailEmailInfoLabel.TextColor = Color.FromArgb("#718238");
            DetailEmailInfoLabel.TextDecorations = TextDecorations.Underline;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                try { await Launcher.Default.OpenAsync($"mailto:{b.PartyEmail.Trim()}"); }
                catch { await ToastService.Warning("Unable to open email."); }
            };
            DetailEmailInfoLabel.GestureRecognizers.Add(tap);
        }
        else
        {
            DetailEmailInfoLabel.TextColor =
                Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#A8AF9E")
                    : Color.FromArgb("#737A6B");
            DetailEmailInfoLabel.TextDecorations = TextDecorations.None;
        }

        DetailItemsContainer.Children.Clear();
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var textColor = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
        var secondary = dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");

        foreach (var item in _detailItems)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(56)),
                    new ColumnDefinition(new GridLength(84)),
                    new ColumnDefinition(new GridLength(104))
                },
                ColumnSpacing = 5,
                Padding = new Thickness(4, 7)
            };

            var info = new VerticalStackLayout { Spacing = 1 };
            if (!string.IsNullOrWhiteSpace(item.ProductImagePath) && File.Exists(item.ProductImagePath))
            {
                var image = new Image { Source = ImageSource.FromFile(item.ProductImagePath), WidthRequest = 52, HeightRequest = 52, Aspect = Aspect.AspectFill, Margin = new Thickness(0, 0, 0, 4) };
                info.Children.Add(image);
            }
            info.Children.Add(new Label
            {
                Text = item.ProductName,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 3
            });

            var meta = item.ProductCode;
            if (item.DiscountAmount > 0)
                meta += $" • Disc {Money(item.DiscountAmount)}";
            meta += $" • GST {item.TaxPercent:0.##}% ({Money(item.TaxAmount)})";

            info.Children.Add(new Label
            {
                Text = meta,
                FontSize = 10,
                TextColor = secondary,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 3
            });

            grid.Add(info, 0, 0);

            grid.Add(new Label
            {
                Text = item.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center,
                TextColor = textColor
            }, 1, 0);

            grid.Add(new Label
            {
                Text = Money(item.Rate),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center,
                TextColor = textColor
            }, 2, 0);

            grid.Add(new Label
            {
                Text = Money(item.NetAmount),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center,
                TextColor = Color.FromArgb("#718238")
            }, 3, 0);

            DetailItemsContainer.Children.Add(grid);
        }

        DetailPaymentSummaryLabel.Text =
            $"Paid {Money(b.PaidAmount)} • Balance {Money(Math.Max(0, b.GrandTotal - b.PaidAmount))}";

        DetailPaymentTitleLabel.Text =
            b.DocumentType == "Purchase"
                ? "Payment paid"
                : b.DocumentType == "Quotation"
                    ? "Payment"
                    : "Payment received";

        DetailPaymentAmountLabel.Text =
            b.DocumentType == "Purchase"
                ? "Payment paid"
                : "Payment received";

        DetailPaymentCard.IsVisible = b.DocumentType != "Quotation";
        DetailPaymentEntryGrid.IsVisible = b.DocumentType != "Quotation";
        RecordPaymentButton.IsVisible = b.DocumentType != "Quotation";

        _ = RenderPaymentsAsync();
    }

    private async Task RenderPaymentsAsync()
    {
        DetailPaymentsContainer.Children.Clear();

        foreach (var p in _detailPayments)
        {
            DetailPaymentsContainer.Children.Add(new Label
            {
                Text = $"{p.PaymentDate:dd/MM/yyyy} • {p.PaymentMode} • {Money(p.Amount)}",
                FontSize = 9,
                FontAttributes = FontAttributes.Bold
            });
        }

        await Task.CompletedTask;
    }

    private void DetailActionButtonTapped(object? sender, TappedEventArgs e) { DetailActionBackdrop.IsVisible=true; DetailActionMenu.IsVisible=true; }
    private void DetailActionBackdropTapped(object? sender, TappedEventArgs e) => HideDetailActionMenu();
    private void HideDetailActionMenu(){DetailActionBackdrop.IsVisible=false;DetailActionMenu.IsVisible=false;}
    private void DetailActionCloseTapped(object? sender, EventArgs e)=>HideDetailActionMenu();
    private async void EditBillTapped(object? sender, EventArgs e){if(_detailBill==null)return; HideDetailActionMenu(); await ShowEditorAsync(_detailBill.DocumentType,null,_detailBill);}
    private async void DeleteBillTapped(object? sender, EventArgs e)
    {
        if(_detailBill==null)return; HideDetailActionMenu(); var page=Application.Current?.Windows.FirstOrDefault()?.Page; if(page==null)return;
        var ok=await page.DisplayAlertAsync("Delete bill",$"Delete { _detailBill.DocumentNo }? This will be blocked when payments or linked stock transactions exist.","Delete","Cancel"); if(!ok)return;
        try{var result=await BillRepository.DeleteAsync(_detailBill.Id); if(!result.Allowed){await ToastService.Warning(result.Reason);return;} ShowListWithoutLeavingTab(); await ToastService.Success("Bill deleted successfully.");}catch(Exception ex){await ToastService.Error(ex.Message);}
    }

    private async void RecordPaymentClicked(object? sender, EventArgs e)
    {
        if (_detailBill == null || _detailBill.DocumentType=="Quotation") return; if(!decimal.TryParse(DetailPaymentAmountEntry.Text?.Replace(",",""),NumberStyles.Any,CultureInfo.InvariantCulture,out var amount)||amount<=0){await ToastService.Warning("Enter a valid payment amount.");return;}
        try{var mode=DetailPaymentModePicker.SelectedItem?.ToString()??"Cash";await BillRepository.AddPaymentAsync(_detailBill,amount,mode,null,DateTime.Today,null);var refreshed=await BillRepository.GetByIdAsync(_detailBill.Id);if(refreshed!=null)_detailBill=refreshed;_detailPayments=await BillRepository.GetPaymentsAsync(_detailBill.Id);DetailPaymentAmountEntry.Text=string.Empty;RenderDetail();await ToastService.Success(_detailBill.DocumentType=="Purchase"?"Payment marked as paid.":"Payment received successfully.");}catch(Exception ex){await ToastService.Error(ex.Message);}
    }

    // =========================================================
    // PDF / SHARE
    // =========================================================
    private async void PrintBillClicked(object? sender, EventArgs e) { await GenerateAndUsePdfAsync(true); }
    private async void SharePdfClicked(object? sender, EventArgs e) { await GenerateAndUsePdfAsync(false); }

    private async Task GenerateAndUsePdfAsync(bool print)
    {
        if(_detailBill==null)return; try
        {
            var companyId=await SessionService.GetActiveCompanyIdAsync();if(!companyId.HasValue)return;var company=await CompanyRepository.GetByIdAsync(companyId.Value);if(company==null)return;
            var template=_detailBill.TemplateId>0?(await BillRepository.GetTemplatesAsync(companyId.Value,_detailBill.DocumentType)).FirstOrDefault(x=>x.Id==_detailBill.TemplateId):await BillRepository.GetDefaultTemplateAsync(companyId.Value,_detailBill.DocumentType); template??=await BillRepository.GetDefaultTemplateAsync(companyId.Value,_detailBill.DocumentType);
            var result=await BillPdfService.CreateAsync(_detailBill,_detailItems,template,company);
            if(print) await BillPdfService.PrintAsync(result.FilePath,$"{_detailBill.DocumentType} {_detailBill.DocumentNo}",result.PageCount); else await Share.Default.RequestAsync(new ShareFileRequest($"{_detailBill.DocumentType} PDF",new ShareFile(result.FilePath)));
        }
        catch(Exception ex){await ToastService.Error($"PDF error: {ex.Message}");}
    }

    // =========================================================
    // TEMPLATE MASTER
    // =========================================================
    private async void TemplateButtonClicked(object? sender, TappedEventArgs e){var companyId=await SessionService.GetActiveCompanyIdAsync();if(!companyId.HasValue)return;await ShowTemplateMenuAsync(companyId.Value,_documentType);}
    private async Task ShowTemplateMenuAsync(int companyId,string documentType)
    {
        var templates=await BillRepository.GetTemplatesAsync(companyId,documentType);TemplatePreviewContainer.Children.Clear();foreach(var t in templates)TemplatePreviewContainer.Children.Add(CreateTemplatePreviewCard(t));TemplateBackdrop.IsVisible=true;TemplateMenu.IsVisible=true;TemplateMenu.Opacity=0;TemplateMenu.Scale=0.96;TemplateMenu.TranslationY=18;await Task.WhenAll(TemplateMenu.FadeToAsync(1,150,Easing.CubicOut),TemplateMenu.ScaleToAsync(1,180,Easing.CubicOut),TemplateMenu.TranslateToAsync(0,0,200,Easing.CubicOut));
    }
    private View CreateTemplatePreviewCard(BillTemplate template)
    {
        var selected = _selectedTemplate?.Id == template.Id || (_selectedTemplate == null && template.IsDefault);
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var outer = new Border
        {
            Padding = 8,
            BackgroundColor = selected ? (dark ? Color.FromArgb("#34402A") : Color.FromArgb("#E8EDDB")) : (dark ? Color.FromArgb("#222A1D") : Color.FromArgb("#F7F9F3")),
            Stroke = selected ? Color.FromArgb("#718238") : (dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC")),
            StrokeThickness = selected ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) }
        };
        var previewPath = template.LayoutKey switch
        {
            "PurpleModern" => "invoice_template_purple.jpg",
            "TealProfessional" => "invoice_template_teal.jpg",
            "CurveTeal" or "CurveNavy" => "invoice_template_curve.jpg",
            "BluePanel" => "invoice_template_blue.webp",
            _ => "invoice_template_classic.jpg"
        };
        var stack = new VerticalStackLayout { Spacing = 7 };
        stack.Children.Add(new Image { Source = previewPath, HeightRequest = 170, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center });
        var templateName = new Label
        {
            Text = template.Name + (selected ? "  ✓" : ""),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var useButton = new Button
        {
            Text = "Use",
            HeightRequest = 34,
            CornerRadius = 10,
            BackgroundColor = selected ? Color.FromArgb("#718238") : Colors.Transparent,
            TextColor = selected ? Colors.White : Color.FromArgb("#718238"),
            BorderColor = Color.FromArgb("#718238"),
            BorderWidth = selected ? 0 : 1,
            CommandParameter = template.Id
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(76))
            },
            ColumnSpacing = 8
        };
        grid.Add(templateName, 0, 0);
        grid.Add(useButton, 1, 0);
        useButton.Clicked += TemplateOptionClicked;
        stack.Children.Add(grid);
        outer.Content = stack;
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await SetTemplateAsync(template.Id);
        outer.GestureRecognizers.Add(tap);
        return outer;
    }
    private async void TemplateOptionClicked(object? sender, EventArgs e) { if (sender is Button b && int.TryParse(b.CommandParameter?.ToString(), out var id)) await SetTemplateAsync(id); }
    private async Task SetTemplateAsync(int id)
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;
        await BillRepository.SetDefaultTemplateAsync(companyId.Value, _documentType, id);
        _selectedTemplate = (await BillRepository.GetTemplatesAsync(companyId.Value, _documentType)).FirstOrDefault(x => x.Id == id);
        await HideTemplateMenuAnimatedAsync();
        await ToastService.Info($"{_selectedTemplate?.Name ?? "Template"} set as default.");
    }

    private void TemplateBackdropTapped(object? sender, TappedEventArgs e) => _ = HideTemplateMenuAnimatedAsync();
    private void TemplateCloseButtonClicked(object? sender, EventArgs e) => _ = HideTemplateMenuAnimatedAsync();
    private async Task HideTemplateMenuAnimatedAsync()
    {
        if (!TemplateMenu.IsVisible) return;
        await Task.WhenAll(TemplateMenu.FadeToAsync(0, 100, Easing.CubicIn), TemplateMenu.ScaleToAsync(0.97, 110, Easing.CubicIn), TemplateMenu.TranslateToAsync(0, 12, 120, Easing.CubicIn));
        TemplateBackdrop.IsVisible = false;
        TemplateMenu.IsVisible = false;
        TemplateMenu.Opacity = 1;
        TemplateMenu.Scale = 1;
        TemplateMenu.TranslationY = 0;
    }
    private void HideTemplateMenu()
    {
        TemplateBackdrop.IsVisible = false;
        TemplateMenu.IsVisible = false;
        TemplateMenu.Opacity = 1;
        TemplateMenu.Scale = 1;
        TemplateMenu.TranslationY = 0;
    }

    private sealed class BillDraftItem
    {
        public Product Product { get; init; } = null!;
        public int ProductId => Product.Id;
        public decimal Quantity { get; set; } = 1;
        public decimal Rate { get; set; }
        public string DiscountMode { get; set; } = "Percent";
        public decimal DiscountInput { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal NetAmount { get; set; }
    }
}
