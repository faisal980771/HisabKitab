using System.Globalization;
using HisabKitab.Models;
using HisabKitab.Services;

namespace HisabKitab.Views.Dashboard;

public partial class StockEntryView : ContentView
{
    private Product? _product;
    private int _supplierPartyId;

    public event EventHandler? BackClicked;
    public event EventHandler? StockAdded;

    public StockEntryView()
    {
        InitializeComponent();
        StockDatePicker.Date = DateTime.Today;
    }

    public async Task LoadAsync(int productId)
    {
        _product = await ProductRepository.GetByIdAsync(productId);

        if (_product == null || !_product.IsActive)
        {
            await ToastService.Error("Product not found.");
            return;
        }

        ProductNameLabel.Text = _product.Name;
        AvailableStockLabel.Text = FormatQty(_product.CurrentStock, _product.Unit);
        RateEntry.Text = _product.PurchaseRate > 0 ? _product.PurchaseRate.ToString("0.##") : string.Empty;

        QuantityEntry.Text = string.Empty;
        SupplierEntry.Text = string.Empty;
        ReferenceEntry.Text = string.Empty;
        NotesEditor.Text = string.Empty;
        StockDatePicker.Date = DateTime.Today;

        _supplierPartyId = 0;
        SupplierSuggestionsBorder.IsVisible = false;
    }

    private void BackTapped(object? sender, TappedEventArgs e) =>
        BackClicked?.Invoke(this, EventArgs.Empty);

    private async void SupplierEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _supplierPartyId = 0;
        var text = e.NewTextValue?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            SupplierSuggestionsBorder.IsVisible = false;
            return;
        }

        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        var suggestions = await PartyRepository.SearchAsync(companyId.Value, text, "Supplier");
        SupplierSuggestions.ItemsSource = suggestions;
        SupplierSuggestionsBorder.IsVisible = suggestions.Count > 0;
    }

    private void SupplierSuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject element && element.BindingContext is Party party)
        {
            _supplierPartyId = party.Id;
            SupplierEntry.Text = party.Name;
            SupplierSuggestionsBorder.IsVisible = false;
        }
    }

    private bool _isSaving;

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_isSaving || _product == null)
            return;

        if (!decimal.TryParse(
                QuantityEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var quantity) ||
            quantity <= 0)
        {
            await ToastService.Warning(
                "Please enter a valid quantity.");

            QuantityEntry.Focus();
            return;
        }

        decimal.TryParse(
            RateEntry.Text?.Replace(",", ""),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var rate);

        try
        {
            _isSaving = true;
            SaveButton.IsEnabled = false;

            SaveLoader.Show("Saving stock...");

            var companyId = _product.CompanyId;

            if (_supplierPartyId == 0 &&
                !string.IsNullOrWhiteSpace(SupplierEntry.Text))
            {
                _supplierPartyId =
                    await PartyRepository.GetOrCreateSupplierAsync(
                        companyId,
                        SupplierEntry.Text.Trim());
            }

            await StockRepository.AddAsync(
                new StockTransaction
                {
                    CompanyId = companyId,
                    ProductId = _product.Id,
                    SupplierPartyId =
                        _supplierPartyId > 0
                            ? _supplierPartyId
                            : null,

                    TransactionType = "IN",
                    Quantity = quantity,
                    Rate = rate,
                    Amount = quantity * rate,

                    TransactionDate =
                        StockDatePicker.Date ?? DateTime.Today,

                    SupplierName =
                        string.IsNullOrWhiteSpace(SupplierEntry.Text)
                            ? null
                            : SupplierEntry.Text.Trim(),

                    ReferenceNo =
                        string.IsNullOrWhiteSpace(ReferenceEntry.Text)
                            ? null
                            : ReferenceEntry.Text.Trim(),

                    Notes =
                        string.IsNullOrWhiteSpace(NotesEditor.Text)
                            ? null
                            : NotesEditor.Text.Trim()
                });

            // MainPage will navigate and show success toast.
            StockAdded?.Invoke(
                this,
                EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
        finally
        {
            SaveLoader.Hide();

            SaveButton.IsEnabled = true;
            _isSaving = false;
        }
    }

    private static string FormatQty(decimal qty, string unit)
    {
        var u = unit;
        if (unit.Equals("Piece", StringComparison.OrdinalIgnoreCase))
            u = Math.Abs(qty - 1) < 0.0001m ? "Piece" : "Pieces";

        return $"{qty:0.##} {u}";
    }
}
