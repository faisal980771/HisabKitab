using HisabKitab.Controls;
using HisabKitab.Services;
using HisabKitab.Views.Dashboard;
using HisabKitab.Models;

namespace HisabKitab.Views;

public partial class MainPage : ContentPage
{
    private readonly HomeView _homeView;
    private readonly SalesView _salesView;
    private readonly PartyView _partyView;
    private readonly StockView _stockView;
    private readonly MoreView _moreView;

    private readonly CompanyView _companyView;
    private readonly ProductView _productView;
    private readonly ProductDetailView _productDetailView;
    private readonly StockEntryView _stockEntryView;
    private readonly UnitMasterView _unitMasterView;

    private ContentView? _currentView;
    private string _currentTab = "Home";
    private int _currentProductId;

    private bool _firstLoad = true;
    private bool _navigationRunning;

    // Tracks whether StockView already has a loaded list.
    // Fresh navigation => load/skeleton.
    // Back navigation => show the already loaded view instantly.
    private bool _stockViewLoaded;

    // PartyView manages its own list/editor/detail cache.
    // Fresh entry loads the party list once; returning from an inner
    // PartyView screen reuses the already loaded PartyView instance.
    private bool _partyViewLoaded;

    public MainPage()
    {
        InitializeComponent();

        _homeView = new HomeView();
        _salesView = new SalesView();
        _partyView = new PartyView();
        _stockView = new StockView();
        _moreView = new MoreView();

        _companyView = new CompanyView();
        _productView = new ProductView();
        _productDetailView = new ProductDetailView();
        _stockEntryView = new StockEntryView();
        _unitMasterView = new UnitMasterView();

        ToastService.RegisterMainHost(GlobalToast);

        BottomNavigation.TabChanged += BottomNavigation_TabChanged;

        _homeView.CompanyClicked += HomeView_CompanyClicked;

        _stockView.AddProductClickedEvent += StockView_AddProductClicked;
        _stockView.ProductClickedEvent += StockView_ProductClickedEvent;

        _stockView.BillCreateRequested += StockView_BillCreateRequested;
        _salesView.BackClicked += SalesView_BackClicked;

        _productView.BackClicked += ProductView_BackClicked;
        _productView.ProductSaved += ProductView_ProductSaved;

        _productDetailView.BackClicked += ProductDetailView_BackClicked;
        _productDetailView.EditClickedEvent += ProductDetailView_EditClickedEvent;
        _productDetailView.AddStockClickedEvent += ProductDetailView_AddStockClickedEvent;
        _productDetailView.Deleted += ProductDetailView_Deleted;

        _stockEntryView.BackClicked += StockEntryView_BackClicked;
        _stockEntryView.StockAdded += StockEntryView_StockAdded;

        _moreView.UnitMasterClicked += MoreView_UnitMasterClicked;
        _unitMasterView.BackClicked += UnitMasterView_BackClicked;

        _companyView.AddCompanyClicked += CompanyView_AddCompanyClicked;
        _companyView.BackClicked += CompanyView_BackClicked;

        PageContainer.Content = _homeView;
        _currentView = _homeView;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        ToastService.RegisterMainHost(GlobalToast);

        if (!_firstLoad)
            return;

        _firstLoad = false;

        await NavigateWithSkeletonAsync(
            _homeView,
            "Home",
            () => _homeView.RefreshAsync(),
            true);
    }

    private async void BottomNavigation_TabChanged(
        object? sender,
        string tab)
    {
        if (_navigationRunning)
            return;

        ContentView nextView;

        switch (tab)
        {
            case "Bill":
                nextView = _salesView;
                break;

            case "Party":
                nextView = _partyView;
                break;

            case "Stock":
                nextView = _stockView;
                break;

            case "More":
                nextView = _moreView;
                break;

            default:
                tab = "Home";
                nextView = _homeView;
                break;
        }

        _currentTab = tab;

        var mode =
            tab == "Party" && _partyViewLoaded
                ? NavigationMode.Cached
                : NavigationMode.Fresh;

        await NavigateWithSkeletonAsync(
            nextView,
            tab,
            () => RefreshTabAsync(tab),
            true,
            mode);
    }

    private async Task RefreshTabAsync(string tab)
    {
        switch (tab)
        {
            case "Home":
                await _homeView.RefreshAsync();
                break;

            case "Bill":
                await _salesView.RefreshAsync();
                break;

            case "Party":
                await RefreshPartyAsync();
                break;

            case "Stock":
                await _stockView.RefreshAsync();
                _stockViewLoaded = true;
                break;

            case "More":
                await _moreView.RefreshAsync();
                break;
        }
    }

    private SkeletonType GetSkeletonType(
        string tab,
        ContentView view)
    {
        if (view == _productDetailView)
            return SkeletonType.Detail;

        if (view == _productView ||
            view == _stockEntryView ||
            view == _companyView)
            return SkeletonType.Form;

        if (view == _unitMasterView)
            return SkeletonType.More;

        return tab switch
        {
            "Home" => SkeletonType.Home,
            "Stock" => SkeletonType.Stock,
            "More" => SkeletonType.More,
            _ => SkeletonType.List
        };
    }

    private enum NavigationMode
    {
        Fresh,
        Cached
    }

    private async Task NavigateWithSkeletonAsync(
        ContentView nextView,
        string tab,
        Func<Task> loadAsync,
        bool showNavbar,
        NavigationMode mode = NavigationMode.Fresh)
    {
        if (_navigationRunning)
            return;

        if (mode == NavigationMode.Cached)
        {
            await NavigateCachedAsync(
                nextView,
                showNavbar);

            return;
        }

        _navigationRunning = true;

        try
        {
            BottomNavigation.IsVisible = showNavbar;
            BottomNavigation.IsEnabled = showNavbar;
            BottomNavigation.InputTransparent = !showNavbar;

            if (_currentView != null &&
                _currentView != nextView)
            {
                _currentView.IsVisible = false;
            }

            PageContainer.Content = nextView;
            _currentView = nextView;
            nextView.IsVisible = true;

            await PageSkeleton.ShowAsync(
                GetSkeletonType(tab, nextView));

            await loadAsync();

            await Task.Delay(120);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"PAGE LOAD ERROR: {ex}");
        }
        finally
        {
            await PageSkeleton.HideAsync();

            if (_currentView != null)
            {
                _currentView.IsVisible = true;
                _currentView.Opacity = 1;
                _currentView.TranslationX = 0;
                _currentView.TranslationY = 0;
            }

            BottomNavigation.IsVisible = showNavbar;
            BottomNavigation.IsEnabled = showNavbar;
            BottomNavigation.InputTransparent = !showNavbar;

            if (showNavbar)
                BottomNavigation.ResetInteraction();

            _navigationRunning = false;
        }
    }

    private async Task NavigateCachedAsync(
        ContentView nextView,
        bool showNavbar)
    {
        if (_navigationRunning)
            return;

        _navigationRunning = true;

        try
        {
            BottomNavigation.IsVisible = showNavbar;
            BottomNavigation.IsEnabled = showNavbar;
            BottomNavigation.InputTransparent = !showNavbar;

            if (_currentView != null &&
                _currentView != nextView)
            {
                _currentView.IsVisible = false;
            }

            PageContainer.Content = nextView;
            _currentView = nextView;

            nextView.IsVisible = true;
            nextView.Opacity = 0;
            nextView.TranslationX = -35;
            nextView.TranslationY = 0;

            // Cached return: NO skeleton and NO RefreshAsync.
            await Task.WhenAll(
                nextView.FadeToAsync(
                    1,
                    170,
                    Easing.CubicOut),
                nextView.TranslateToAsync(
                    0,
                    0,
                    220,
                    Easing.CubicOut));
        }
        finally
        {
            if (_currentView != null)
            {
                _currentView.IsVisible = true;
                _currentView.Opacity = 1;
                _currentView.TranslationX = 0;
                _currentView.TranslationY = 0;
            }

            BottomNavigation.IsVisible = showNavbar;
            BottomNavigation.IsEnabled = showNavbar;
            BottomNavigation.InputTransparent = !showNavbar;

            if (showNavbar)
                BottomNavigation.ResetInteraction();

            _navigationRunning = false;
        }
    }

    // =========================================================
    // STOCK
    // =========================================================

    private async void StockView_AddProductClicked(
        object? sender,
        EventArgs e)
    {
        _productView.StartNew();

        await NavigateWithSkeletonAsync(
            _productView,
            "Stock",
            () => Task.CompletedTask,
            false);
    }

    private async void StockView_ProductClickedEvent(
        object? sender,
        int productId)
    {
        _currentProductId = productId;

        await NavigateWithSkeletonAsync(
            _productDetailView,
            "Stock",
            () => _productDetailView.LoadAsync(productId),
            false);
    }

    private async void StockView_BillCreateRequested(
    object? sender,
    BillCreateRequestEventArgs e)
    {
        _currentTab = "Bill";
        BottomNavigation.CurrentTab = "Bill";

        await NavigateWithSkeletonAsync(
            _salesView,
            "Bill",
            () => _salesView.OpenNewAsync(
                e.DocumentType,
                e.ProductIds),
            false);
    }

    private async void SalesView_BackClicked(
        object? sender,
        EventArgs e)
    {
        _currentTab = "Bill";
        BottomNavigation.CurrentTab = "Bill";

        await NavigateWithSkeletonAsync(
            _salesView,
            "Bill",
            () => Task.CompletedTask,
            true,
            NavigationMode.Cached);
    }


    // =========================================================
    // PRODUCT
    // =========================================================

    private async void ProductView_BackClicked(
        object? sender,
        EventArgs e)
    {
        await ReturnToStockAsync();
    }

    private async void ProductView_ProductSaved(
        object? sender,
        EventArgs e)
    {
        await ReturnToStockAsync(true);

        //await ToastService.Success(
        //    "Product saved successfully.");
    }

    // =========================================================
    // PRODUCT DETAIL
    // =========================================================

    private async void ProductDetailView_BackClicked(
        object? sender,
        EventArgs e)
    {
        await ReturnToStockAsync();
    }

    private async void ProductDetailView_EditClickedEvent(
        object? sender,
        int productId)
    {
        _currentProductId = productId;

        await NavigateWithSkeletonAsync(
            _productView,
            "Stock",
            () => _productView.LoadForEditAsync(productId),
            false);
    }

    private async void ProductDetailView_AddStockClickedEvent(
        object? sender,
        int productId)
    {
        _currentProductId = productId;

        await NavigateWithSkeletonAsync(
            _stockEntryView,
            "Stock",
            () => _stockEntryView.LoadAsync(productId),
            false);
    }

    private async void ProductDetailView_Deleted(
        object? sender,
        EventArgs e)
    {
        await ReturnToStockAsync(true);

        await ToastService.Success(
            "Product deleted successfully.");
    }

    // =========================================================
    // STOCK ENTRY
    // =========================================================

    private async void StockEntryView_BackClicked(
        object? sender,
        EventArgs e)
    {
        await NavigateWithSkeletonAsync(
            _productDetailView,
            "Stock",
            () => _productDetailView.LoadAsync(
                _currentProductId),
            false);
    }

    private async void StockEntryView_StockAdded(
        object? sender,
        EventArgs e)
    {
        await NavigateWithSkeletonAsync(
            _productDetailView,
            "Stock",
            () => _productDetailView.LoadAsync(
                _currentProductId),
            false);

        await ToastService.Success(
            "Stock added successfully.");
    }

    // =========================================================
    // RETURN TO STOCK
    // =========================================================

    private async Task ReturnToStockAsync(
        bool refreshStock = false)
    {
        _currentTab = "Stock";
        BottomNavigation.CurrentTab = "Stock";

        // BACK without data changes:
        // use the already loaded StockView instance directly.
        if (!refreshStock && _stockViewLoaded)
        {
            await NavigateWithSkeletonAsync(
             _stockView,
             "Stock",
             () => Task.CompletedTask,
             true,
             NavigationMode.Cached);
        }
        else
        {
            // Fresh entry / data changed:
            // load from SQLite and show skeleton.
            await NavigateWithSkeletonAsync(
                _stockView,
                "Stock",
                async () =>
                {
                    await _stockView.RefreshAsync();
                    _stockViewLoaded = true;
                },
                true,
                NavigationMode.Fresh);
        }

        BottomNavigation.IsVisible = true;
        BottomNavigation.IsEnabled = true;
        BottomNavigation.InputTransparent = false;
        BottomNavigation.ResetInteraction();
    }

    // =========================================================
    // PARTY REFRESH
    // =========================================================

    private async Task RefreshPartyAsync(bool force = false)
    {
        if (force)
            await _partyView.RefreshAsync(true);
        else
            await _partyView.EnsureLoadedAsync();

        _partyViewLoaded = true;
    }

    // =========================================================
    // COMPANY
    // =========================================================

    private async void HomeView_CompanyClicked(
        object? sender,
        EventArgs e)
    {
        await NavigateWithSkeletonAsync(
            _companyView,
            "Home",
            () => _companyView.RefreshAsync(),
            true);
    }

    private void CompanyView_AddCompanyClicked(
        object? sender,
        EventArgs e)
    {
        var window =
            Application.Current?.Windows.FirstOrDefault();

        if (window != null)
            window.Page = new CompanySetupPage();
    }

    private async void CompanyView_BackClicked(
        object? sender,
        EventArgs e)
    {
        _currentTab = "Home";
        BottomNavigation.CurrentTab = "Home";

        await NavigateWithSkeletonAsync(
            _homeView,
            "Home",
            () => _homeView.RefreshAsync(),
            true);
    }

    // =========================================================
    // UNIT MASTER
    // =========================================================

    private async void MoreView_UnitMasterClicked(
        object? sender,
        EventArgs e)
    {
        await NavigateWithSkeletonAsync(
            _unitMasterView,
            "More",
            () => _unitMasterView.RefreshAsync(),
            true);
    }

    private async void UnitMasterView_BackClicked(
        object? sender,
        EventArgs e)
    {
        await NavigateWithSkeletonAsync(
            _moreView,
            "More",
            () => _moreView.RefreshAsync(),
            true);
    }

    // =========================================================
    // ANDROID BACK
    // =========================================================

    protected override bool OnBackButtonPressed()
    {
        if (_navigationRunning)
            return true;

        if (_currentView == _salesView && _salesView.IsInnerPageOpen)
        {
            _ = _salesView.HandleBackAsync();
            return true;
        }

        if (_currentView == _stockEntryView)
        {
            _ = NavigateWithSkeletonAsync(
                _productDetailView,
                "Stock",
                () => _productDetailView.LoadAsync(
                    _currentProductId),
                false);

            return true;
        }

        if (_currentView == _partyView)
        {
            var partyBackTask = _partyView.HandleBackAsync();

            // If PartyView has a Detail/Editor open, consume Android Back
            // immediately and let PartyView finish its slide-back animation.
            if (!partyBackTask.IsCompletedSuccessfully)
            {
                _ = partyBackTask;
                return true;
            }

            if (partyBackTask.Result)
                return true;

            // No nested PartyView screen is open, so continue with the
            // normal Android Back behavior.
        }

        if (_currentView == _productView ||
            _currentView == _productDetailView)
        {
            _ = ReturnToStockAsync();
            return true;
        }

        if (_currentView == _unitMasterView)
        {
            _ = NavigateWithSkeletonAsync(
                _moreView,
                "More",
                () => _moreView.RefreshAsync(),
                true);

            return true;
        }

        if (_currentView == _companyView)
        {
            _ = NavigateWithSkeletonAsync(
                _homeView,
                "Home",
                () => _homeView.RefreshAsync(),
                true);

            return true;
        }

        return base.OnBackButtonPressed();
    }

    protected override void OnDisappearing()
    {
        ToastService.UnregisterMainHost(GlobalToast);
        base.OnDisappearing();
    }
}