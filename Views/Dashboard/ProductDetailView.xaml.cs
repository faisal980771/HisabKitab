using HisabKitab.Models;
using HisabKitab.Services;

namespace HisabKitab.Views.Dashboard;

public partial class ProductDetailView : ContentView
{
    private Product? _product;

    public event EventHandler? BackClicked;
    public event EventHandler<int>? EditClickedEvent;
    public event EventHandler<int>? AddStockClickedEvent;
    public event EventHandler? Deleted;

    public ProductDetailView()
    {
        InitializeComponent();
    }

    public async Task LoadAsync(int productId)
    {
        _product = await ProductRepository.GetByIdAsync(productId);

        if (_product == null || !_product.IsActive)
        {
            await ToastService.Error("Product not found.");
            return;
        }

        NameLabel.Text = _product.Name;
        CodeLabel.Text = string.IsNullOrWhiteSpace(_product.ProductCode) ? "No product code" : _product.ProductCode;
        CategoryLabel.Text = string.IsNullOrWhiteSpace(_product.Category) ? "No category" : _product.Category;
        UnitLabel.Text = _product.Unit;
        PurchaseRateLabel.Text = $"₹ {_product.PurchaseRate:0.##}";
        SaleRateLabel.Text = $"₹ {_product.SaleRate:0.##}";
        GSTLabel.Text = $"{_product.GSTPercent:0.##}%";
        HSNLabel.Text = string.IsNullOrWhiteSpace(_product.HSN) ? "-" : _product.HSN;
        ReorderLabel.Text = _product.ReorderLevel.ToString("0.##");
        StockLabel.Text = $"{FormatQty(_product.CurrentStock, _product.Unit)}";

        var low = _product.ReorderLevel > 0 && _product.CurrentStock < _product.ReorderLevel;
        StockStatusLabel.Text = low ? "LOW STOCK" : "IN STOCK";
        StockStatusLabel.TextColor = Color.FromArgb(low ? "#FFE0A8" : "#FFFFFF");

        ProductImage.Source = !string.IsNullOrWhiteSpace(_product.ImagePath) && File.Exists(_product.ImagePath)
            ? ImageSource.FromFile(_product.ImagePath)
            : ImageSource.FromFile("product.svg");

        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        if (_product == null) return;

        var history = await StockRepository.GetForProductAsync(_product.Id);
        HistoryContainer.Children.Clear();

        if (history.Count == 0)
        {
            HistorySummaryLabel.Text = "No stock movement yet.";
            return;
        }

        HistorySummaryLabel.Text = $"{history.Count} stock movement(s)";

        foreach (var item in history.Take(12))
        {
            var typeText = item.TransactionType == "OUT" || item.TransactionType == "SALE" ? "Stock Out" :
                item.TransactionType == "OPENING" ? "Opening Stock" : "Stock In";

            var typeColor = item.TransactionType == "OUT" || item.TransactionType == "SALE"
                ? Color.FromArgb("#C95D5D")
                : item.TransactionType == "OPENING"
                    ? Color.FromArgb("#718238")
                    : Color.FromArgb("#4F9D5F");

            var left = new VerticalStackLayout { Spacing = 2 };
            left.Children.Add(new Label { Text = typeText, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = typeColor });
            left.Children.Add(new Label { Text = item.TransactionDate.ToString("dd MMM yyyy"), FontSize = 9, TextColor = ThemeSecondary() });

            var right = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End };
            right.Children.Add(new Label { Text = $"{item.Quantity:0.##} {_product.Unit}", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = ThemePrimary(), HorizontalTextAlignment = TextAlignment.End });
            right.Children.Add(new Label { Text = string.IsNullOrWhiteSpace(item.SupplierName) ? "No supplier" : item.SupplierName, FontSize = 9, TextColor = ThemeSecondary(), HorizontalTextAlignment = TextAlignment.End });

            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = 130 } } };
            row.Children.Add(left);
            row.Children.Add(right);
            Grid.SetColumn(right, 1);

            HistoryContainer.Children.Add(row);
        }
    }

    private async void ProductImageTapped(object? sender, TappedEventArgs e)
    {
        if (_product != null && !string.IsNullOrWhiteSpace(_product.ImagePath) && File.Exists(_product.ImagePath))
            await ProductDetailImagePreview.ShowAsync(_product.ImagePath, _product.Name);
    }

    private void BackTapped(object? sender, TappedEventArgs e) => BackClicked?.Invoke(this, EventArgs.Empty);

    private void EditClicked(object? sender, EventArgs e)
    {
        if (_product != null) EditClickedEvent?.Invoke(this, _product.Id);
    }

    private void AddStockClicked(object? sender, EventArgs e)
    {
        if (_product != null) AddStockClickedEvent?.Invoke(this, _product.Id);
    }

    private async void DeleteClicked(object? sender, EventArgs e)
    {
        if (_product == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var confirm = await page.DisplayAlertAsync("Delete Product", $"Delete '{_product.Name}'?", "Delete", "Cancel");
        if (!confirm) return;

        await ProductRepository.DeleteAsync(_product.Id);
        await ToastService.Success("Product deleted.");
        Deleted?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatQty(decimal qty, string unit)
    {
        var unitText = unit;
        if (unit.Equals("Piece", StringComparison.OrdinalIgnoreCase))
            unitText = Math.Abs(qty - 1) < 0.0001m ? "Piece" : "Pieces";

        return $"{qty:0.##} {unitText}";
    }

    private static Color ThemePrimary() =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");

    private static Color ThemeSecondary() =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");
}
