using Microsoft.Maui.ApplicationModel;
using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Controls.Shapes;
using SQLite;

namespace HisabKitab.Views.Dashboard;

public partial class StockView : ContentView
{
    private const int PageSize = 30;

    private enum SortMode
    {
        Alphabetical,
        PriceAsc,
        PriceDesc,
        Newest,
        StockAsc,
        StockDesc,
        LowStock,
        Sku
    }

    private List<Product> _products = new();
    private readonly Dictionary<char, View> _letterTargets = new();
    private readonly HashSet<int> _selectedProductIds = new();
    private readonly HashSet<int> _loadedProductIds = new();
    private bool _searchExecuting;

    private SortMode _sortMode = SortMode.Alphabetical;
    private string _searchText = string.Empty;

    private int _loadedCount;
    private int _filteredTotal;

    private int _restoreProductIndex = -1;
    private double _restoreScrollY;
    private bool _restoreListPosition;

    private bool _isLoadingFirstPage;
    private bool _isLoadingMore;
    private bool _refreshRunning;
    private bool _hasLoadedData;
    private bool _forceRefreshRequested;
    private bool _backgroundRefreshRunning;

    private int _cacheTotalProducts;
    private decimal _cacheTotalStock;
    private string _cacheLastUpdated = string.Empty;
    private int _cacheCompanyId;

    private double _pullDistance;
    private bool _pulling;

    private int _alphabetStartIndex = -1;
    private int _lastAlphabetIndex = -1;
    private bool _alphabetDragging;

    private DateTime _lastAlphabetHaptic = DateTime.MinValue;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _alphabetBubbleCts;

    public event EventHandler? AddProductClickedEvent;
    public event EventHandler<int>? ProductClickedEvent;
    public event EventHandler? RefreshRequested;

    public event EventHandler<BillCreateRequestEventArgs>? BillCreateRequested;

    public StockView()
    {
        InitializeComponent();

        AttachPullGesture(StockHeader);
        AttachPullGesture(StockSummaryCard);
        AttachPullGesture(ProductsHeader);

        BuildAlphabetBar();
        ApplySortButtonVisuals();
    }

    // =========================================================
    // DATA LOAD
    // =========================================================

    public async Task RefreshAsync()
    {
        var companyId =
            await SessionService.GetActiveCompanyIdAsync();

        if (!companyId.HasValue)
            return;

        // The StockView instance is kept alive by MainPage.
        // Returning from ProductDetail should show the already-rendered
        // list immediately; never rebuild it just because Back was tapped.
        if (_hasLoadedData &&
            _cacheCompanyId == companyId.Value &&
            !_forceRefreshRequested)
        {
            _ = CheckForDataChangesAsync(
                companyId.Value);
            return;
        }

        await FullRefreshAsync(
            companyId.Value);
    }

    public async Task ForceRefreshAsync()
    {
        var companyId =
            await SessionService.GetActiveCompanyIdAsync();

        if (companyId.HasValue)
            await FullRefreshAsync(
                companyId.Value);
    }

    private async Task FullRefreshAsync(
        int companyId)
    {
        if (_isLoadingFirstPage)
            return;

        try
        {
            _isLoadingFirstPage = true;
            _forceRefreshRequested = false;

            await LoadSummaryAsync(
                companyId);

            await LoadFirstPageAsync(
                companyId);

            _hasLoadedData = true;
            _cacheCompanyId = companyId;
            await UpdateCacheStampAsync(
                companyId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"STOCK REFRESH ERROR: {ex}");

            await ToastService.Error(
                "Unable to load stock.");
        }
        finally
        {
            _isLoadingFirstPage = false;
        }
    }

    private async Task CheckForDataChangesAsync(
        int companyId)
    {
        if (_backgroundRefreshRunning ||
            _isLoadingFirstPage ||
            !_hasLoadedData)
            return;

        _backgroundRefreshRunning = true;

        try
        {
            var stamp =
                await GetCacheStampAsync(
                    companyId);

            if (stamp == null)
                return;

            var changed =
                stamp.TotalProducts != _cacheTotalProducts ||
                stamp.TotalStock != Convert.ToDouble(_cacheTotalStock) ||
                !string.Equals(
                    stamp.LastUpdated ?? string.Empty,
                    _cacheLastUpdated,
                    StringComparison.Ordinal);

            if (changed)
                await MainThread.InvokeOnMainThreadAsync(
                    async () =>
                        await FullRefreshAsync(
                            companyId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"STOCK BACKGROUND CHECK ERROR: {ex}");
        }
        finally
        {
            _backgroundRefreshRunning = false;
        }
    }

    private sealed class StockSummaryRow
    {
        public int TotalProducts { get; set; }
        public int LowStock { get; set; }
        public double TotalStock { get; set; }
        public string? LastUpdated { get; set; }
    }

    private sealed class StockCacheStamp
    {
        public int TotalProducts { get; set; }
        public double TotalStock { get; set; }
        public string? LastUpdated { get; set; }
    }

    private async Task LoadSummaryAsync(
        int companyId)
    {
        await DatabaseService.InitializeAsync();

        var rows =
            await DatabaseService.Db.QueryAsync<StockSummaryRow>(
                "SELECT COUNT(*) AS TotalProducts, " +
                "SUM(CASE WHEN ReorderLevel > 0 AND CurrentStock < ReorderLevel THEN 1 ELSE 0 END) AS LowStock, " +
                "COALESCE(SUM(CurrentStock),0) AS TotalStock, " +
                "COALESCE(MAX(UpdatedAtUtc),'') AS LastUpdated " +
                "FROM Product WHERE CompanyId = ?",
                companyId);

        var summary =
            rows.FirstOrDefault() ??
            new StockSummaryRow();

        TotalProductsLabel.Text =
            summary.TotalProducts.ToString();

        LowStockLabel.Text =
            summary.LowStock.ToString();

        TotalStockLabel.Text =
            ((decimal)summary.TotalStock).ToString("0.##");

        _cacheTotalProducts =
            summary.TotalProducts;

        _cacheTotalStock =
            (decimal)summary.TotalStock;

        _cacheLastUpdated =
            summary.LastUpdated ?? string.Empty;
    }

    private async Task<StockCacheStamp?> GetCacheStampAsync(
        int companyId)
    {
        await DatabaseService.InitializeAsync();

        var rows =
            await DatabaseService.Db.QueryAsync<StockCacheStamp>(
                "SELECT COUNT(*) AS TotalProducts, " +
                "COALESCE(SUM(CurrentStock),0) AS TotalStock, " +
                "COALESCE(MAX(UpdatedAtUtc),'') AS LastUpdated " +
                "FROM Product WHERE CompanyId = ?",
                companyId);

        return rows.FirstOrDefault();
    }

    private async Task UpdateCacheStampAsync(
        int companyId)
    {
        var stamp =
            await GetCacheStampAsync(
                companyId);

        if (stamp == null)
            return;

        _cacheTotalProducts =
            stamp.TotalProducts;

        _cacheTotalStock =
            (decimal)stamp.TotalStock;

        _cacheLastUpdated =
            stamp.LastUpdated ?? string.Empty;
    }

    private async Task LoadFirstPageAsync(
        int companyId)
    {
        ShowProductSkeleton();

        var restorePosition = _restoreListPosition;
        var restoreIndex = _restoreProductIndex;
        var restoreY = _restoreScrollY;

        try
        {
            ProductListContainer.Children.Clear();
            _letterTargets.Clear();
            _loadedProductIds.Clear();
            _loadedCount = 0;

            _filteredTotal =
                await GetFilteredCountAsync(
                    companyId,
                    _searchText);

            var page =
                await QueryProductsAsync(
                    companyId,
                    _searchText,
                    0,
                    PageSize);

            _products = new List<Product>();
            AppendProductCards(page);

            if (restorePosition &&
                restoreIndex >= 0)
            {
                while (_loadedCount <= restoreIndex &&
                       _loadedCount < _filteredTotal)
                {
                    var nextPage =
                        await QueryProductsAsync(
                            companyId,
                            _searchText,
                            _loadedCount,
                            PageSize);

                    if (nextPage.Count == 0)
                        break;

                    AppendProductCards(nextPage);
                }
            }

            UpdateListState();

            if (restorePosition)
            {
                await Task.Delay(120);

                var maxY =
                    Math.Max(
                        0,
                        ProductScrollView.ContentSize.Height -
                        ProductScrollView.Height);

                var targetY =
                    Math.Max(
                        0,
                        Math.Min(restoreY, maxY));

                await ProductScrollView.ScrollToAsync(
                    0,
                    targetY,
                    false);

                await Task.Delay(60);

                maxY =
                    Math.Max(
                        0,
                        ProductScrollView.ContentSize.Height -
                        ProductScrollView.Height);

                targetY =
                    Math.Max(
                        0,
                        Math.Min(restoreY, maxY));

                await ProductScrollView.ScrollToAsync(
                    0,
                    targetY,
                    false);
            }

            _restoreListPosition = false;
            _restoreProductIndex = -1;
            _restoreScrollY = 0;
        }
        finally
        {
            HideProductSkeleton();
        }
    }
    private async Task LoadMoreAsync()
    {
        if (_isLoadingMore ||
            _loadedCount >= _filteredTotal)
            return;

        var companyId =
            await SessionService.GetActiveCompanyIdAsync();

        if (!companyId.HasValue)
            return;

        _isLoadingMore = true;
        BottomLoadIndicator.IsVisible = true;
        BottomLoadSpinner.IsRunning = true;

        try
        {
            var offset = _loadedCount;

            var page =
                await QueryProductsAsync(
                    companyId.Value,
                    _searchText,
                    offset,
                    PageSize);

            if (page.Count == 0)
                return;

            AppendProductCards(page);
            _loadedCount =
                Math.Min(
                    _loadedCount,
                    _filteredTotal);

            UpdateListState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"STOCK LOAD MORE ERROR: {ex}");

            await ToastService.Error(
                "Unable to load more products.");
        }
        finally
        {
            BottomLoadSpinner.IsRunning = false;
            BottomLoadIndicator.IsVisible = false;
            _isLoadingMore = false;
        }
    }

    private void ProductScrollView_Scrolled(
        object? sender,
        ScrolledEventArgs e)
    {
        if (_isLoadingMore ||
            _loadedCount >= _filteredTotal)
            return;

        var remaining =
            ProductScrollView.ContentSize.Height -
            e.ScrollY -
            ProductScrollView.Height;

        if (remaining <= 260)
            _ = LoadMoreAsync();
    }

    private async Task<int> GetFilteredCountAsync(
        int companyId,
        string search)
    {
        await DatabaseService.InitializeAsync();

        if (string.IsNullOrWhiteSpace(search))
        {
            var countRows =
                await DatabaseService.Db.QueryAsync<CountRow>(
                    "SELECT COUNT(*) AS Count FROM Product WHERE CompanyId = ?",
                    companyId);

            return countRows.FirstOrDefault()?.Count ?? 0;
        }

        var like = $"%{search.Trim()}%";
        var rows =
            await DatabaseService.Db.QueryAsync<CountRow>(
                "SELECT COUNT(*) AS Count FROM Product " +
                "WHERE CompanyId = ? AND (" +
                "Name LIKE ? COLLATE NOCASE OR " +
                "ProductCode LIKE ? COLLATE NOCASE OR " +
                "Barcode LIKE ? COLLATE NOCASE OR " +
                "Category LIKE ? COLLATE NOCASE OR " +
                "HSN LIKE ? COLLATE NOCASE OR " +
                "Unit LIKE ? COLLATE NOCASE)",
                companyId, like, like, like, like, like, like);

        return rows.FirstOrDefault()?.Count ?? 0;
    }

    private sealed class CountRow
    {
        public int Count { get; set; }
    }

    private async Task<List<Product>> QueryProductsAsync(
        int companyId,
        string search,
        int skip,
        int take)
    {
        await DatabaseService.InitializeAsync();

        var where =
            " WHERE CompanyId = ? ";

        var args =
            new List<object>
            { companyId };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like =
                $"%{search.Trim()}%";

            where +=
                " AND (Name LIKE ? COLLATE NOCASE OR " +
                "ProductCode LIKE ? COLLATE NOCASE OR " +
                "Barcode LIKE ? COLLATE NOCASE OR " +
                "Category LIKE ? COLLATE NOCASE OR " +
                "HSN LIKE ? COLLATE NOCASE OR " +
                "Unit LIKE ? COLLATE NOCASE) ";

            args.Add(like);
            args.Add(like);
            args.Add(like);
            args.Add(like);
            args.Add(like);
            args.Add(like);
        }

        var orderBy =
            GetSqlOrderBy();

        var sql =
            "SELECT * FROM Product" +
            where +
            " ORDER BY " +
            orderBy +
            $" LIMIT {take} OFFSET {skip}";

        return await DatabaseService.Db
            .QueryAsync<Product>(
                sql,
                args.ToArray());
    }

    private string GetSqlOrderBy()
    {
        return _sortMode switch
        {
            SortMode.PriceAsc =>
                "SaleRate ASC, Name COLLATE NOCASE ASC, Id ASC",

            SortMode.PriceDesc =>
                "SaleRate DESC, Name COLLATE NOCASE ASC, Id ASC",

            SortMode.Newest =>
                "CreatedAtUtc DESC, Id DESC",

            SortMode.StockAsc =>
                "CurrentStock ASC, Name COLLATE NOCASE ASC, Id ASC",

            SortMode.StockDesc =>
                "CurrentStock DESC, Name COLLATE NOCASE ASC, Id ASC",

            SortMode.LowStock =>
                "CASE WHEN ReorderLevel > 0 AND CurrentStock < ReorderLevel THEN 0 ELSE 1 END ASC, " +
                "CurrentStock ASC, Name COLLATE NOCASE ASC, Id ASC",

            SortMode.Sku =>
                "CASE WHEN ProductCode IS NOT NULL AND ProductCode <> '' AND ProductCode NOT GLOB '*[^0-9]*' THEN 0 ELSE 1 END ASC, " +
                "CASE WHEN ProductCode IS NOT NULL AND ProductCode <> '' AND ProductCode NOT GLOB '*[^0-9]*' THEN CAST(ProductCode AS INTEGER) END ASC, " +
                "ProductCode COLLATE NOCASE ASC, Name COLLATE NOCASE ASC, Id ASC",

            _ =>
                "Name COLLATE NOCASE ASC, Id ASC"
        };
    }

    private static bool MatchesSearch(
        Product product,
        string search)
    {
        var term = search.Trim();
        if (term.Length == 0) return true;

        var comparison =
            StringComparison.OrdinalIgnoreCase;

        return (product.Name ?? string.Empty).Contains(term, comparison) ||
               (product.ProductCode ?? string.Empty).Contains(term, comparison) ||
               (product.Barcode ?? string.Empty).Contains(term, comparison) ||
               (product.Category ?? string.Empty).Contains(term, comparison) ||
               (product.HSN ?? string.Empty).Contains(term, comparison) ||
               (product.Unit ?? string.Empty).Contains(term, comparison);
    }

    private IEnumerable<Product> ApplyMemorySort(
        IEnumerable<Product> products)
    {
        return _sortMode switch
        {
            SortMode.PriceAsc =>
                products.OrderBy(x => x.SaleRate).ThenBy(x => x.Name),
            SortMode.PriceDesc =>
                products.OrderByDescending(x => x.SaleRate).ThenBy(x => x.Name),
            SortMode.Newest =>
                products.OrderByDescending(x => x.CreatedAtUtc),
            SortMode.StockAsc =>
                products.OrderBy(x => x.CurrentStock).ThenBy(x => x.Name),
            SortMode.StockDesc =>
                products.OrderByDescending(x => x.CurrentStock).ThenBy(x => x.Name),
            SortMode.LowStock =>
                products.OrderByDescending(x => x.ReorderLevel > 0 && x.CurrentStock < x.ReorderLevel)
                        .ThenBy(x => x.CurrentStock)
                        .ThenBy(x => x.Name),
            SortMode.Sku =>
                products.OrderBy(x => x.ProductCode).ThenBy(x => x.Name),
            _ =>
                products.OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static AsyncTableQuery<Product> ApplySearchFilter(
        AsyncTableQuery<Product> query,
        string search)
    {
        return query.Where(x =>
            (x.Name ?? string.Empty).Contains(search) ||
            (x.ProductCode ?? string.Empty).Contains(search) ||
            (x.Barcode ?? string.Empty).Contains(search) ||
            (x.Category ?? string.Empty).Contains(search) ||
            (x.HSN ?? string.Empty).Contains(search) ||
            (x.Unit ?? string.Empty).Contains(search));
    }

    // =========================================================
    // PULL TO REFRESH
    // =========================================================

    private void AttachPullGesture(View view)
    {
        var pan =
            new PanGestureRecognizer();

        pan.PanUpdated +=
            StockPullPanUpdated;

        view.GestureRecognizers.Add(pan);
    }

    private void StockPullPanUpdated(
        object? sender,
        PanUpdatedEventArgs e)
    {
        if (_refreshRunning ||
            ProductScrollView.ScrollY > 1)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:

                _pulling = true;
                _pullDistance = 0;

                PullRefreshSpinner.IsRunning =
                    false;

                _ = ShowPullIndicatorAsync();

                break;

            case GestureStatus.Running:

                if (!_pulling ||
                    e.TotalY <= 0)
                    return;

                _pullDistance =
                    Math.Min(
                        e.TotalY,
                        120);

                var progress =
                    Math.Min(
                        _pullDistance / 75.0,
                        1.0);

                PullRefreshIndicator.TranslationY =
                    -28 +
                    progress * 28;

                PullRefreshSpinner.IsRunning =
                    progress >= 1;

                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:

                if (!_pulling)
                    return;

                _pulling = false;

                if (_pullDistance >= 75)
                    _ = TriggerRefreshAsync();
                else
                    _ = HidePullIndicatorAsync();

                break;
        }
    }

    private async Task ShowPullIndicatorAsync()
    {
        PullRefreshIndicator.TranslationY =
            -28;

        PullRefreshIndicator.Opacity = 0;

        await PullRefreshIndicator.FadeToAsync(
            1,
            100,
            Easing.CubicOut);
    }

    private async Task TriggerRefreshAsync()
    {
        if (_refreshRunning)
            return;

        _refreshRunning = true;

        try
        {
            PullRefreshSpinner.IsRunning =
                true;

            _forceRefreshRequested = true;

            if (RefreshRequested != null)
                RefreshRequested.Invoke(
                    this,
                    EventArgs.Empty);
            else
                await RefreshAsync();

            await Task.Delay(200);
        }
        finally
        {
            PullRefreshSpinner.IsRunning =
                false;

            await HidePullIndicatorAsync();

            _refreshRunning = false;
        }
    }

    private async Task HidePullIndicatorAsync()
    {
        PullRefreshSpinner.IsRunning =
            false;

        await PullRefreshIndicator.FadeToAsync(
            0,
            120,
            Easing.CubicIn);

        PullRefreshIndicator.TranslationY =
            -28;

        _pullDistance = 0;
    }

    // =========================================================
    // SEARCH
    // =========================================================

    private async void SearchButtonTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideSortMenu();
        HideSelectionMenu();

        AlphabetIndexContainer.IsVisible = false;

        SearchOverlay.IsVisible = true;
        SearchOverlay.Opacity = 0;

        SetAndroidBlur(true);

        ProductSearchBar.Text = _searchText;

        SearchStatusLabel.Text =
            string.IsNullOrWhiteSpace(_searchText)
                ? "Type to search..."
                : "Searching result";

        await SearchOverlay.FadeToAsync(
            1,
            180,
            Easing.CubicOut);

        ProductSearchBar.Focus();
    }

    private async void SearchOverlayTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                ProductSearchBar.Text))
        {
            ResetSearchFilter();

            await RefreshCurrentFilterAsync();

            await CloseSearchAsync();
        }
    }

    private async void CloseSearchTapped(
        object? sender,
        TappedEventArgs e)
    {
        await CloseSearchAsync();
    }

    private async Task CloseSearchAsync()
    {
        _searchCts?.Cancel();

        SearchSpinner.IsRunning = false;
        SearchSpinner.IsVisible = false;

        await SearchOverlay.FadeToAsync(
            0,
            120,
            Easing.CubicIn);

        SearchOverlay.IsVisible = false;

        AlphabetIndexContainer.IsVisible =
            _loadedCount > 0;

        SetAndroidBlur(false);
    }

    private async void ProductSearchBar_TextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        _searchCts?.Cancel();

        var typed =
            e.NewTextValue?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(typed))
        {
            SearchSpinner.IsVisible = false;
            SearchSpinner.IsRunning = false;

            SearchStatusLabel.Text =
                "Type to search...";

            SearchSuggestions.ItemsSource = null;
            SearchSuggestions.IsVisible = false;
            return;
        }

        var cts =
            new CancellationTokenSource();

        _searchCts = cts;

        SearchSpinner.IsVisible = true;
        SearchSpinner.IsRunning = true;

        SearchStatusLabel.Text =
            "Searching result";

        try
        {
            await Task.Delay(
                3000,
                cts.Token);

            if (cts.IsCancellationRequested)
                return;

            var companyId =
                await SessionService
                    .GetActiveCompanyIdAsync();

            if (!companyId.HasValue)
                return;

            var suggestions =
                await QueryProductsAsync(
                    companyId.Value,
                    typed,
                    0,
                    20);

            if (cts.IsCancellationRequested)
                return;

            SearchSuggestions.ItemsSource =
                suggestions;

            SearchSuggestions.IsVisible =
                suggestions.Count > 0;

            SearchStatusLabel.Text =
                suggestions.Count > 0
                    ? "Searching result"
                    : "No matching products.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SearchStatusLabel.Text =
                "Search failed.";

            System.Diagnostics.Debug.WriteLine(
                $"PRODUCT SEARCH ERROR: {ex}");
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                SearchSpinner.IsRunning = false;
                SearchSpinner.IsVisible = false;
            }
        }
    }

    private async void ProductSearchBar_SearchButtonPressed(
        object? sender,
        EventArgs e)
    {
        await ExecuteSearchNowAsync();
    }

    private async Task ExecuteSearchNowAsync()
    {
        if (_searchExecuting)
            return;

        var search =
            ProductSearchBar.Text?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(search))
            return;

        _searchExecuting = true;
        _searchCts?.Cancel();

        SearchSpinner.IsVisible = true;
        SearchSpinner.IsRunning = true;
        SearchStatusLabel.Text =
            "Searching result";

        try
        {
            _searchText = search;

            var companyId =
                await SessionService
                    .GetActiveCompanyIdAsync();

            if (!companyId.HasValue)
                return;

            await LoadFirstPageAsync(
                companyId.Value);

            SearchFilterLabel.Text =
                $"⌕ {search}";

            SearchFilterPill.IsVisible =
                true;

            SearchSuggestions.ItemsSource = null;
            SearchSuggestions.IsVisible = false;

            await CloseSearchAsync();
        }
        catch (Exception ex)
        {
            await ToastService.Error(
                ex.Message);
        }
        finally
        {
            SearchSpinner.IsRunning = false;
            SearchSpinner.IsVisible = false;
            _searchExecuting = false;
        }
    }

    private void SearchSuggestionTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (sender is not BindableObject item ||
            item.BindingContext is not Product product)
            return;

        _searchCts?.Cancel();

        SearchSuggestions.ItemsSource = null;
        SearchSuggestions.IsVisible = false;

        SearchSpinner.IsRunning = false;
        SearchSpinner.IsVisible = false;

        // Suggestion tap opens the detail directly. Clear the temporary
        // keyword/filter so returning to Stock shows the normal list.
        _searchText = string.Empty;
        SearchFilterLabel.Text = string.Empty;
        SearchFilterPill.IsVisible = false;
        ProductSearchBar.Text = string.Empty;

        _ = OpenProductFromSearchAsync(
            product.Id);
    }

    private async Task OpenProductFromSearchAsync(
        int productId)
    {
        _restoreListPosition = true;
        _restoreScrollY =
            Math.Max(
                0,
                ProductScrollView.ScrollY);
        _restoreProductIndex = -1;

        await CloseSearchAsync();

        ProductClickedEvent?.Invoke(
            this,
            productId);
    }

    private void ClearSearchFilterTapped(
        object? sender,
        TappedEventArgs e)
    {
        ResetSearchFilter();

        _ = RefreshCurrentFilterAsync();
    }

    private void ResetSearchFilter()
    {
        _searchCts?.Cancel();

        _searchText = string.Empty;
        SearchFilterLabel.Text = string.Empty;
        SearchFilterPill.IsVisible = false;

        ProductSearchBar.Text = string.Empty;

        SearchSuggestions.ItemsSource = null;
        SearchSuggestions.IsVisible = false;

        SearchStatusLabel.Text =
            "Type to search...";
    }

    private async Task RefreshCurrentFilterAsync()
    {
        var companyId =
            await SessionService
                .GetActiveCompanyIdAsync();

        if (companyId.HasValue)
        {
            await FullRefreshAsync(
                companyId.Value);
        }
    }

    // =========================================================
    // SELECTION
    // =========================================================

    private void ToggleProductSelection(
        Product product,
        bool selected)
    {
        if (selected)
            _selectedProductIds.Add(
                product.Id);
        else
            _selectedProductIds.Remove(
                product.Id);

        UpdateSelectionUI();
    }

    private void UpdateSelectionUI()
    {
        var count =
            _selectedProductIds.Count;

        SelectedActionsButton.IsVisible =
            count > 0;

        SelectedCountBadge.IsVisible =
            count > 0;

        SelectedCountLabel.Text =
            count > 99
                ? "99+"
                : count.ToString();
    }

    private async void SelectedActionsButtonTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideSortMenu();

        SearchOverlay.IsVisible = false;
        SetAndroidBlur(false);

        SelectionBackdrop.IsVisible = true;
        SelectionMenu.IsVisible = true;
        SelectionMenu.Opacity = 0;
        SelectionMenu.Scale = 0.97;

        await Task.WhenAll(
            SelectionMenu.FadeToAsync(1, 120, Easing.CubicOut),
            SelectionMenu.ScaleToAsync(1, 140, Easing.CubicOut));
    }

    private void SelectionBackdropTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideSelectionMenu();
    }

    private void HideSelectionMenu()
    {
        SelectionBackdrop.IsVisible = false;
        SelectionMenu.IsVisible = false;
        SelectionMenu.Opacity = 1;
        SelectionMenu.Scale = 1;
    }

    private async void CreateInvoiceClicked(object? sender, EventArgs e)
    {
        RaiseBillCreateRequested("Sales");
        await Task.CompletedTask;
    }

    private async void CreatePurchaseClicked(object? sender, EventArgs e)
    {
        RaiseBillCreateRequested("Purchase");
        await Task.CompletedTask;
    }

    private async void CreateStockEntryClicked(
        object? sender,
        EventArgs e)
    {
        HideSelectionMenu();

        await ToastService.Info(
            $"Add Stock • {_selectedProductIds.Count} products selected.");
    }

    private async void CreateQuotationClicked(object? sender, EventArgs e)
    {
        RaiseBillCreateRequested("Quotation");
        await Task.CompletedTask;
    }

    private void RaiseBillCreateRequested(string documentType)
    {
        var ids = _selectedProductIds.ToList();
        HideSelectionMenu();
        if (ids.Count == 0) return;

        BillCreateRequested?.Invoke(
            this,
            new BillCreateRequestEventArgs(documentType, ids));

        _selectedProductIds.Clear();
        UpdateSelectionUI();
    }


    private async void SelectAllClicked(
        object? sender,
        EventArgs e)
    {
        HideSortMenu();
        HideSelectionMenu();

        try
        {
            var companyId =
                await SessionService
                    .GetActiveCompanyIdAsync();

            if (!companyId.HasValue)
                return;

            var all =
                await GetAllFilteredProductsAsync(
                    companyId.Value,
                    _searchText);

            _selectedProductIds.Clear();
            foreach (var product in all)
                _selectedProductIds.Add(product.Id);

            UpdateSelectionUI();
            await RefreshCurrentFilterAsync();
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
    }

    private async void UnselectAllClicked(
        object? sender,
        EventArgs e)
    {
        _selectedProductIds.Clear();
        UpdateSelectionUI();
        HideSortMenu();
        HideSelectionMenu();
        await RefreshCurrentFilterAsync();
    }

    private async Task<List<Product>> GetAllFilteredProductsAsync(
        int companyId,
        string search)
    {
        await DatabaseService.InitializeAsync();

        var all =
            await DatabaseService.Db
                .Table<Product>()
                .Where(x =>
                    x.CompanyId == companyId)
                .ToListAsync();

        return string.IsNullOrWhiteSpace(search)
            ? all
            : all.Where(x => MatchesSearch(x, search)).ToList();
    }

    private void ClearSelectionClicked(
        object? sender,
        EventArgs e)
    {
        _selectedProductIds.Clear();

        UpdateSelectionUI();

        HideSelectionMenu();

        // Redraw the current loaded page so all checkboxes reset.
        _ = RefreshCurrentFilterAsync();
    }

    // =========================================================
    // SORT
    // =========================================================

    private void SortButtonTapped(
        object? sender,
        TappedEventArgs e)
    {
        SearchOverlay.IsVisible =
            false;

        HideSelectionMenu();

        SetAndroidBlur(false);

        SortBackdrop.IsVisible =
            true;

        SortMenu.IsVisible =
            true;

        AlphabetIndexContainer.IsVisible =
            false;

        ApplySortButtonVisuals();
    }

    private void SortBackdropTapped(
        object? sender,
        TappedEventArgs e)
    {
        HideSortMenu();
    }

    private void HideSortMenu()
    {
        SortBackdrop.IsVisible =
            false;

        SortMenu.IsVisible =
            false;

        AlphabetIndexContainer.IsVisible =
            _loadedCount > 0;
    }

    private async void SortOptionClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button)
            return;

        var option =
            button.CommandParameter?.ToString();

        _sortMode =
            option switch
            {
                "PriceAsc" => SortMode.PriceAsc,
                "PriceDesc" => SortMode.PriceDesc,
                "Newest" => SortMode.Newest,
                "StockAsc" => SortMode.StockAsc,
                "StockDesc" => SortMode.StockDesc,
                "LowStock" => SortMode.LowStock,
                "SKU" => SortMode.Sku,
                _ => SortMode.Alphabetical
            };

        SortCurrentLabel.Text =
            option switch
            {
                "PriceAsc" => "Price ↑",
                "PriceDesc" => "Price ↓",
                "Newest" => "Newest",
                "StockAsc" => "Stock ↑",
                "StockDesc" => "Stock ↓",
                "LowStock" => "Low Stock",
                "SKU" => "SKU A-Z",
                _ => "A-Z"
            };

        ApplySortButtonVisuals(
            button);

        // Keep the selected row highlighted long enough for the user to see the tap.
        await Task.Delay(180);

        ShowProductSkeleton();

        HideSortMenu();

        await RefreshCurrentFilterAsync();
    }

    private void ApplySortButtonVisuals(
        Button? selected = null)
    {
        var activeCommand =
            GetSortCommand();

        foreach (var child in
                 SortMenuContent.Children)
        {
            if (child is not Button button)
                continue;

            var active =
                selected != null
                    ? ReferenceEquals(
                        button,
                        selected)
                    : button.CommandParameter?.ToString() ==
                      activeCommand;

            button.BackgroundColor =
                active
                    ? Color.FromArgb("#718238")
                    : Colors.Transparent;

            button.TextColor =
                active
                    ? Colors.White
                    : GetThemeTextColor();

            button.FontAttributes =
                active
                    ? FontAttributes.Bold
                    : FontAttributes.None;
        }
    }

    private string GetSortCommand()
    {
        return _sortMode switch
        {
            SortMode.PriceAsc => "PriceAsc",
            SortMode.PriceDesc => "PriceDesc",
            SortMode.Newest => "Newest",
            SortMode.StockAsc => "StockAsc",
            SortMode.StockDesc => "StockDesc",
            SortMode.LowStock => "LowStock",
            SortMode.Sku => "SKU",
            _ => "AZ"
        };
    }

    private static Color GetThemeTextColor()
    {
        return Application.Current?.RequestedTheme ==
               AppTheme.Dark
            ? Color.FromArgb("#F4F6EF")
            : Color.FromArgb("#20251C");
    }

    private CancellationTokenSource? _skeletonWaveCts;

    private void ShowProductSkeleton()
    {
        _skeletonWaveCts?.Cancel();
        _skeletonWaveCts =
            new CancellationTokenSource();

        ProductSkeletonContainer.IsVisible = true;
        SkeletonWave.IsVisible = true;
        SkeletonWave.TranslationX = -110;

        _ = AnimateSkeletonWaveAsync(
            _skeletonWaveCts.Token);
    }

    private void HideProductSkeleton()
    {
        _skeletonWaveCts?.Cancel();
        _skeletonWaveCts = null;

        SkeletonWave.IsVisible = false;
        SkeletonWave.TranslationX = -110;
        ProductSkeletonContainer.IsVisible = false;
    }

    private async Task AnimateSkeletonWaveAsync(
        CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested &&
                   ProductSkeletonContainer.IsVisible)
            {
                var width =
                    Math.Max(
                        ProductSkeletonContainer.Width,
                        320);

                SkeletonWave.TranslationX = -110;

                await SkeletonWave.TranslateToAsync(
                    width + 110,
                    0,
                    850,
                    Easing.Linear);

                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    // =========================================================
    // ALPHABET
    // =========================================================

    private void BuildAlphabetBar()
    {
        AlphabetBar.Children.Clear();

        const string letters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        for (var i = 0; i < letters.Length; i++)
        {
            var index = i;

            var label =
                new Label
                {
                    Text =
                        letters[index].ToString(),
                    FontSize = 8,
                    FontAttributes =
                        FontAttributes.Bold,
                    TextColor =
                        Color.FromArgb("#718238"),
                    HorizontalOptions =
                        LayoutOptions.Center,
                    VerticalOptions =
                        LayoutOptions.Center
                };

            var box =
                new Border
                {
                    HeightRequest = 14,
                    WidthRequest = 20,
                    Padding = 0,
                    BackgroundColor =
                        Colors.Transparent,
                    Stroke =
                        Colors.Transparent,
                    StrokeShape =
                        new RoundRectangle
                        {
                            CornerRadius =
                                new CornerRadius(6)
                        },
                    ClassId =
                        index.ToString(),
                    Content = label
                };

            var tap =
                new TapGestureRecognizer();

            tap.Tapped += async (_, _) =>
            {
                await SelectAlphabetIndexAsync(
                    index,
                    true);
            };

            box.GestureRecognizers.Add(
                tap);

            var pan =
                new PanGestureRecognizer();

            pan.PanUpdated +=
                AlphabetPanUpdated;

            box.GestureRecognizers.Add(
                pan);

            AlphabetBar.Children.Add(
                box);
        }
    }

    private void AlphabetPanUpdated(
        object? sender,
        PanUpdatedEventArgs e)
    {
        if (sender is not Border box)
            return;

        if (!int.TryParse(
                box.ClassId,
                out var startIndex))
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:

                _alphabetDragging = true;
                _alphabetStartIndex =
                    startIndex;
                _lastAlphabetIndex =
                    startIndex;

                _ = SelectAlphabetIndexAsync(
                    startIndex,
                    true);

                break;

            case GestureStatus.Running:

                if (!_alphabetDragging)
                    return;

                const double step = 7;

                var offset =
                    (int)Math.Round(
                        e.TotalY / step);

                var index =
                    Math.Clamp(
                        _alphabetStartIndex +
                        offset,
                        0,
                        25);

                if (index ==
                    _lastAlphabetIndex)
                    return;

                _lastAlphabetIndex =
                    index;

                _ = SelectAlphabetIndexAsync(
                    index,
                    false);

                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:

                _alphabetDragging = false;
                _alphabetStartIndex = -1;
                _lastAlphabetIndex = -1;

                _ = HideAlphabetBubbleAsync();

                break;
        }
    }

    private async Task SelectAlphabetIndexAsync(
        int index,
        bool forceHaptic)
    {
        if (index < 0 ||
            index > 25)
            return;

        var letter =
            (char)('A' + index);

        if (_sortMode !=
            SortMode.Alphabetical)
        {
            _sortMode =
                SortMode.Alphabetical;

            SortCurrentLabel.Text =
                "A-Z";

            ApplySortButtonVisuals();
        }

        ShowAlphabetBubble(letter);

        var shouldHaptic =
            forceHaptic ||
            (_alphabetDragging &&
             (DateTime.UtcNow -
              _lastAlphabetHaptic)
                .TotalMilliseconds >= 45);

        if (shouldHaptic)
        {
            try
            {
                if (HapticFeedback.Default.IsSupported)
                {
                    HapticFeedback.Default.Perform(
                        HapticFeedbackType.Click);
                }
            }
            catch
            {
            }

            _lastAlphabetHaptic =
                DateTime.UtcNow;
        }

        if (!_letterTargets.TryGetValue(
                letter,
                out var target))
        {
            await EnsureLetterLoadedAsync(
                letter);

            _letterTargets.TryGetValue(
                letter,
                out target);
        }

        if (target == null)
            return;

        try
        {
            await ProductScrollView.ScrollToAsync(
                target,
                ScrollToPosition.Start,
                false);
        }
        catch
        {
        }
    }

    private async Task EnsureLetterLoadedAsync(
        char letter)
    {
        if (_loadedCount >= _filteredTotal)
            return;

        var companyId =
            await SessionService
                .GetActiveCompanyIdAsync();

        if (!companyId.HasValue)
            return;

        while (!_letterTargets.ContainsKey(letter) &&
               _loadedCount < _filteredTotal)
        {
            if (_isLoadingMore)
            {
                await Task.Delay(30);
                continue;
            }

            _isLoadingMore = true;

            try
            {
                var page =
                    await QueryProductsAsync(
                        companyId.Value,
                        _searchText,
                        _loadedCount,
                        PageSize);

                if (page.Count == 0)
                    break;

                _products.AddRange(page);

                AppendProductCards(page);
            }
            finally
            {
                _isLoadingMore = false;
            }
        }

        UpdateListState();
    }

    private void ShowAlphabetBubble(
        char letter)
    {
        AlphabetBubbleLabel.Text =
            letter.ToString();

        _alphabetBubbleCts?.Cancel();

        _alphabetBubbleCts =
            new CancellationTokenSource();

        AlphabetBubble.IsVisible = true;
        AlphabetBubble.Opacity = 1;
        AlphabetBubble.Scale = 1;

        _ = HideAlphabetBubbleDelayedAsync(
            _alphabetBubbleCts.Token);
    }

    private async Task HideAlphabetBubbleDelayedAsync(
        CancellationToken token)
    {
        try
        {
            await Task.Delay(
                650,
                token);

            await HideAlphabetBubbleAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HideAlphabetBubbleAsync()
    {
        if (!AlphabetBubble.IsVisible)
            return;

        await AlphabetBubble.FadeToAsync(
            0,
            130,
            Easing.CubicIn);

        AlphabetBubble.Scale = 0.75;
        AlphabetBubble.IsVisible = false;
    }

    // =========================================================
    // PRODUCT LIST
    // =========================================================

    private void AppendProductCards(
        IEnumerable<Product> products)
    {
        foreach (var product in products)
        {
            if (!_loadedProductIds.Add(product.Id))
                continue;

            var card =
                CreateProductCard(product);

            ProductListContainer.Children.Add(
                card);

            _products.Add(product);

            RegisterAlphabetTarget(
                product,
                card);

            _loadedCount =
                Math.Min(
                    _loadedProductIds.Count,
                    _filteredTotal);
        }
    }

    private void RegisterAlphabetTarget(
        Product product,
        View card)
    {
        if (string.IsNullOrWhiteSpace(
                product.Name))
            return;

        var first =
            char.ToUpperInvariant(
                product.Name.Trim()[0]);

        if (first >= 'A' &&
            first <= 'Z' &&
            !_letterTargets.ContainsKey(first))
        {
            _letterTargets[first] =
                card;
        }
    }

    private void UpdateListState()
    {
        EmptyState.IsVisible =
            _loadedCount == 0;

        ProductListContainer.IsVisible =
            _loadedCount > 0;

        _loadedCount =
            Math.Min(
                _loadedCount,
                _filteredTotal);

        ProductCountLabel.Text =
            _filteredTotal == 0
                ? "0 / 0"
                : $"{_loadedCount} / {_filteredTotal}";

        AlphabetIndexContainer.IsVisible =
            _loadedCount > 0 &&
            !SearchOverlay.IsVisible &&
            !SortMenu.IsVisible &&
            !SelectionMenu.IsVisible;

        UpdateSelectionUI();
    }

    private void RememberProductPosition(
        Product product)
    {
        _restoreListPosition = true;
        _restoreScrollY =
            Math.Max(
                0,
                ProductScrollView.ScrollY);

        _restoreProductIndex =
            _products.FindIndex(
                x => x.Id == product.Id);
    }

    private View CreateProductCard(
        Product product)
    {
        var dark =
            Application.Current?.RequestedTheme ==
            AppTheme.Dark;

        var primaryText =
            dark
                ? Color.FromArgb("#F4F6EF")
                : Color.FromArgb("#20251C");

        var secondaryText =
            dark
                ? Color.FromArgb("#A8AF9E")
                : Color.FromArgb("#737A6B");

        var cardBackground =
            dark
                ? Color.FromArgb("#1A2016")
                : Color.FromArgb("#FFFFFF");

        var cardBorder =
            dark
                ? Color.FromArgb("#30382B")
                : Color.FromArgb("#E3E7DC");

        var isLowStock =
            product.ReorderLevel > 0 &&
            product.CurrentStock <
            product.ReorderLevel;

        var stockColor =
            isLowStock
                ? Color.FromArgb("#D9943B")
                : Color.FromArgb("#4F9D5F");

        View productVisual;

        if (!string.IsNullOrWhiteSpace(
                product.ImagePath) &&
            File.Exists(product.ImagePath))
        {
            productVisual =
                new Image
                {
                    HeightRequest = 48,
                    WidthRequest = 48,
                    Aspect = Aspect.AspectFill,
                    Source =
                        ImageSource.FromFile(
                            product.ImagePath)
                };
        }
        else
        {
            productVisual =
                new Label
                {
                    Text = "📦",
                    FontSize = 30,
                    HorizontalOptions =
                        LayoutOptions.Center,
                    VerticalOptions =
                        LayoutOptions.Center
                };
        }

        var checkBox =
            new CheckBox
            {
                IsChecked =
                    _selectedProductIds.Contains(
                        product.Id),
                Color =
                    Color.FromArgb("#718238"),
                HorizontalOptions =
                    LayoutOptions.Center,
                VerticalOptions =
                    LayoutOptions.Center
            };

        checkBox.CheckedChanged += (
            _,
            args) =>
        {
            ToggleProductSelection(
                product,
                args.Value);
        };

        var imageBox =
            new Border
            {
                WidthRequest = 60,
                HeightRequest = 60,
                BackgroundColor =
                    dark
                        ? Color.FromArgb("#252D1F")
                        : Color.FromArgb("#F1F4EA"),
                Stroke = Colors.Transparent,
                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(15)
                    },
                Content = productVisual
            };

        var name =
            new Label
            {
                Text = product.Name,
                FontSize = 14,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor = primaryText,
                LineBreakMode =
                    LineBreakMode.TailTruncation
            };

        var detail =
            new Label
            {
                Text =
                    $"{product.ProductCode} • {product.Unit}",
                FontSize = 10,
                TextColor = secondaryText
            };

        var price =
            new Label
            {
                Text =
                    $"Sale ₹ {product.SaleRate:0.##}",
                FontSize = 11,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor =
                    Color.FromArgb("#718238")
            };

        var info =
            new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions =
                    LayoutOptions.Center
            };

        info.Children.Add(name);
        info.Children.Add(detail);
        info.Children.Add(price);

        var stock =
            new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions =
                    LayoutOptions.Center,
                HorizontalOptions =
                    LayoutOptions.End
            };

        stock.Children.Add(
            new Label
            {
                Text =
                    $"{FormatQty(product.CurrentStock, product.Unit)} left",
                FontSize = 12,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor = stockColor,
                HorizontalTextAlignment =
                    TextAlignment.End
            });

        stock.Children.Add(
            new Label
            {
                Text =
                    product.ReorderLevel > 0
                        ? $"Reorder {product.ReorderLevel:0.##}"
                        : "No reorder",
                FontSize = 9,
                TextColor = stockColor,
                HorizontalTextAlignment =
                    TextAlignment.End
            });

        var infoTap =
            new TapGestureRecognizer();

        infoTap.Tapped += (
            _,
            _) =>
        {
            RememberProductPosition(product);

            ProductClickedEvent?.Invoke(
                this,
                product.Id);
        };

        info.GestureRecognizers.Add(
            infoTap);

        var grid =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitionCollection
                    {
                        new ColumnDefinition
                        {
                            Width = 30
                        },
                        new ColumnDefinition
                        {
                            Width = 60
                        },
                        new ColumnDefinition
                        {
                            Width =
                                GridLength.Star
                        },
                        new ColumnDefinition
                        {
                            Width = 92
                        }
                    },
                ColumnSpacing = 8
            };

        grid.Children.Add(checkBox);

        grid.Children.Add(imageBox);
        Grid.SetColumn(
            imageBox,
            1);

        grid.Children.Add(info);
        Grid.SetColumn(
            info,
            2);

        grid.Children.Add(stock);
        Grid.SetColumn(
            stock,
            3);

        var card =
            new Border
            {
                BackgroundColor =
                    cardBackground,
                Stroke =
                    cardBorder,
                StrokeThickness = 1,
                StrokeShape =
                    new RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(17)
                    },
                Padding =
                    new Thickness(10),
                Content = grid
            };

        var cardTap =
            new TapGestureRecognizer();

        cardTap.Tapped += async (_, _) =>
        {
            RememberProductPosition(product);

            await AnimateProductTapAsync(card);

            ProductClickedEvent?.Invoke(
                this,
                product.Id);
        };

        card.GestureRecognizers.Add(cardTap);

        return card;
    }

    private static async Task AnimateProductTapAsync(
        View card)
    {
        try
        {
            await card.ScaleToAsync(
                0.97,
                65,
                Easing.CubicOut);

            await card.ScaleToAsync(
                1.0,
                95,
                Easing.CubicInOut);
        }
        catch
        {
        }
    }

    private static string FormatQty(
        decimal qty,
        string unit)
    {
        var unitText =
            unit;

        if (unit.Equals(
                "Piece",
                StringComparison.OrdinalIgnoreCase))
        {
            unitText =
                Math.Abs(qty - 1) <
                0.0001m
                    ? "Piece"
                    : "Pieces";
        }

        return $"{qty:0.##} {unitText}";
    }

#if ANDROID
    private void SetAndroidBlur(
        bool enabled)
    {
        if (StockContent.Handler?.PlatformView
            is not Android.Views.View native)
            return;

        if (Android.OS.Build.VERSION.SdkInt <
            Android.OS.BuildVersionCodes.S)
            return;

        native.SetRenderEffect(
            enabled
                ? Android.Graphics.RenderEffect
                    .CreateBlurEffect(
                        7f,
                        7f,
                        Android.Graphics.Shader.TileMode.Clamp)
                : null);
    }
#else
    private void SetAndroidBlur(
        bool enabled)
    {
    }
#endif

    // =========================================================
    // ADD PRODUCT
    // =========================================================

    private void AddProductClicked(
        object? sender,
        EventArgs e)
    {
        AddProductClickedEvent?.Invoke(
            this,
            EventArgs.Empty);
    }
}
