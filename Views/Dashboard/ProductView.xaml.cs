using System.Globalization;
using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace HisabKitab.Views.Dashboard;

public partial class ProductView : ContentView
{
    private string? _imagePath;
    private int _editingId;
    private bool _isEditing;
    private List<Unit> _units = new();

    public event EventHandler? BackClicked;
    public event EventHandler? ProductSaved;

    public ProductView()
    {
        InitializeComponent();
    }

    public async Task InitializeAsync()
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (companyId.HasValue)
        {
            _units = await UnitRepository.GetAllAsync(companyId.Value);
            UnitPicker.ItemsSource = _units.Select(x => x.Name).ToList();
        }
        GSTPicker.ItemsSource = new[] { "0", "5", "12", "18", "28" }.ToList();
    }

    public void StartNew()
    {
        _editingId = 0;
        _isEditing = false;
        TitleLabel.Text = "Add Product";
        SubtitleLabel.Text = "Create a new product";
        SaveButton.Text = "Save Product";
        _ = InitializeAsync();
        ClearForm();
    }

    public async Task LoadForEditAsync(int productId)
    {
        await InitializeAsync();

        var product = await ProductRepository.GetByIdAsync(productId);
        if (product == null)
        {
            await ToastService.Error("Product not found.");
            return;
        }

        _editingId = product.Id;
        _isEditing = true;
        TitleLabel.Text = "Edit Product";
        SubtitleLabel.Text = "Update product details";
        SaveButton.Text = "Update Product";

        NameEntry.Text = product.Name;
        CodeEntry.Text = product.ProductCode;
        BarcodeEntry.Text = product.Barcode;
        CategoryEntry.Text = product.Category;
        HSNEntry.Text = product.HSN;
        PurchaseRateEntry.Text = product.PurchaseRate.ToString("0.##");
        SaleRateEntry.Text = product.SaleRate.ToString("0.##");
        OpeningStockEntry.Text = product.OpeningStock.ToString("0.##");
        OpeningRateEntry.Text = product.OpeningRate.ToString("0.##");
        ReorderLevelEntry.Text = product.ReorderLevel.ToString("0.##");
        UnitPicker.SelectedItem = product.Unit;
        GSTPicker.SelectedItem = product.GSTPercent.ToString("0.##");

        _imagePath = product.ImagePath;

        if (!string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath))
        {
            ProductImage.Source = ImageSource.FromFile(_imagePath);
            ProductImage.IsVisible = true;
            ImagePlaceholder.IsVisible = false;
            RemoveImageButton.IsVisible = true;
            SelectImageButton.Text = "Change Image";
        }
        else
        {
            ProductImage.Source = null;
            ProductImage.IsVisible = false;
            ImagePlaceholder.IsVisible = true;
            RemoveImageButton.IsVisible = false;
            SelectImageButton.Text = "Add Image";
        }
    }

    private void BackTapped(object? sender, TappedEventArgs e) => BackClicked?.Invoke(this, EventArgs.Empty);

    private async void SelectImageClicked(object? sender, EventArgs e)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;

        if (page == null)
            return;

        var choice = await page.DisplayActionSheetAsync(
            "Product Image",
            "Cancel",
            null,
            "Take Photo",
            "Choose from Files");

        if (choice == "Take Photo")
            await TakePhotoAsync();
        else if (choice == "Choose from Files")
            await PickFileAsync();
    }

    private async Task TakePhotoAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await ToastService.Warning("Camera is not available on this device.");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync(
                new MediaPickerOptions
                {
                    Title = "Take Product Photo",
                    MaximumWidth = 1200,
                    MaximumHeight = 1200,
                    CompressionQuality = 85
                });

            if (photo != null)
                await SaveImageFileAsync(photo);
        }
        catch (Exception ex)
        {
            await ToastService.Error($"Camera failed: {ex.Message}");
        }
    }

    private async Task PickFileAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "Choose Product Image",
                    FileTypes = FilePickerFileType.Images
                });

            if (file != null)
                await SaveImageFileAsync(file);
        }
        catch (Exception ex)
        {
            await ToastService.Error($"File selection failed: {ex.Message}");
        }
    }

    private async Task SaveImageFileAsync(FileResult file)
    {
        var folder = Path.Combine(
            FileSystem.AppDataDirectory,
            "Products");

        Directory.CreateDirectory(folder);

        var ext = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(ext))
            ext = ".jpg";

        var destination = Path.Combine(
            folder,
            $"{Guid.NewGuid():N}{ext}");

        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(destination);

        await source.CopyToAsync(target);

        _imagePath = destination;

        ProductImage.Source = ImageSource.FromFile(destination);
        ProductImage.IsVisible = true;
        ImagePlaceholder.IsVisible = false;
        RemoveImageButton.IsVisible = true;
        SelectImageButton.Text = "Change Image";
    }

    private async void RemoveImageClicked(object? sender, EventArgs e)
    {
        _imagePath = null;
        ProductImage.Source = null;
        ProductImage.IsVisible = false;
        ImagePlaceholder.IsVisible = true;
        RemoveImageButton.IsVisible = false;
        SelectImageButton.Text = "Add Image";
        await ToastService.Info("Product image removed.");
    }

    private bool _isSaving;
    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_isSaving)
            return;

        try
        {
            var companyId = await SessionService.GetActiveCompanyIdAsync();

            if (!companyId.HasValue)
            {
                await ToastService.Error("No active company selected.");
                return;
            }

            var name = NameEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                await ToastService.Warning("Please enter product name.");
                NameEntry.Focus();
                return;
            }

            if (UnitPicker.SelectedIndex < 0)
            {
                await ToastService.Warning("Please select product unit.");
                return;
            }

            if (await ProductRepository.ExistsByNameAsync(
                    companyId.Value,
                    name,
                    _editingId))
            {
                await ToastService.Warning("This product already exists.");
                return;
            }

            SaveButton.IsEnabled = false;

            _isSaving = true;
            SaveButton.IsEnabled = false;
            SaveLoader.Show(_isEditing ? "Updating product...": "Saving product...");

            decimal.TryParse(
                PurchaseRateEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var purchaseRate);

            decimal.TryParse(
                SaleRateEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var saleRate);

            decimal.TryParse(
                OpeningStockEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var openingStock);

            decimal.TryParse(
                OpeningRateEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var openingRate);

            decimal.TryParse(
                ReorderLevelEntry.Text?.Replace(",", ""),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var reorderLevel);

            decimal.TryParse(
                GSTPicker.SelectedItem?.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var gst);

            Product product;

            if (_isEditing)
            {
                product = await ProductRepository.GetByIdAsync(_editingId)
                    ?? throw new InvalidOperationException("Product not found.");

                product.Name = name;
                product.ProductCode = CodeEntry.Text?.Trim() ?? string.Empty;
                product.Barcode = BarcodeEntry.Text?.Trim();
                product.Unit = UnitPicker.SelectedItem?.ToString() ?? "Piece";
                product.Category = CategoryEntry.Text?.Trim();
                product.HSN = HSNEntry.Text?.Trim();
                product.GSTPercent = gst;
                product.PurchaseRate = purchaseRate;
                product.SaleRate = saleRate;
                product.ReorderLevel = reorderLevel;
                product.ImagePath = _imagePath;
            }
            else
            {
                product = new Product
                {
                    CompanyId = companyId.Value,
                    Name = name,
                    ProductCode = CodeEntry.Text?.Trim() ?? string.Empty,
                    Barcode = BarcodeEntry.Text?.Trim(),
                    Unit = UnitPicker.SelectedItem?.ToString() ?? "Piece",
                    Category = CategoryEntry.Text?.Trim(),
                    HSN = HSNEntry.Text?.Trim(),
                    GSTPercent = gst,
                    PurchaseRate = purchaseRate,
                    SaleRate = saleRate,
                    OpeningStock = openingStock,
                    OpeningRate = openingRate,
                    CurrentStock = 0,
                    ReorderLevel = reorderLevel,
                    ImagePath = _imagePath,
                    IsActive = true
                };
            }

            await ProductRepository.SaveAsync(product);

            // MainPage immediately returns to Stock and re-enables navbar.
            ProductSaved?.Invoke(this, EventArgs.Empty);

            await ToastService.Success(
                _isEditing
                    ? "Product updated successfully."
                    : "Product created successfully.");
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

    private void ClearForm()
    {
        NameEntry.Text = string.Empty;
        CodeEntry.Text = string.Empty;
        BarcodeEntry.Text = string.Empty;
        CategoryEntry.Text = string.Empty;
        HSNEntry.Text = string.Empty;
        PurchaseRateEntry.Text = string.Empty;
        SaleRateEntry.Text = string.Empty;
        OpeningStockEntry.Text = string.Empty;
        OpeningRateEntry.Text = string.Empty;
        ReorderLevelEntry.Text = string.Empty;
        if (UnitPicker.ItemsSource is System.Collections.IList list && list.Count > 0)
            UnitPicker.SelectedIndex = 0;
        if (GSTPicker.ItemsSource is System.Collections.IList gstList && gstList.Count > 0)
            GSTPicker.SelectedIndex = 0;

        _imagePath = null;
        ProductImage.Source = null;
        ProductImage.IsVisible = false;
        ImagePlaceholder.IsVisible = true;
        RemoveImageButton.IsVisible = false;
        SelectImageButton.Text = "Add Image";
    }
}
