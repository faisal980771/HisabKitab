using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Devices;

namespace HisabKitab.Views.Dashboard;

public partial class PartyView : ContentView
{
    private readonly ObservableCollection<PartyListItem> _items = new();
    private readonly HashSet<int> _selectedIds = new();
    private CancellationTokenSource? _searchCts;
    private List<Party> _currentParties = new();
    private string _currentType = "Customer";
    private string _searchText = string.Empty;
    private bool _loaded;
    private PartyEditorView? _editorView;
    private PartyDetailView? _detailView;

    private readonly Dictionary<char, int> _alphabetTargets = new();
    private CancellationTokenSource? _alphabetBubbleCts;
    private int _activeAlphabetIndex = -1;

    public PartyView()
    {
        InitializeComponent();
        PartyCollectionView.ItemsSource = _items;
        Loaded += async (_, _) => await EnsureLoadedAsync();
        _ = AnimateSkeletonWaveAsync();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded && _items.Count > 0) return;
        await RefreshAsync(true);
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (!force && _loaded) return;
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        PartySkeleton.IsVisible = true;
        try
        {
            await PartyRepository.EnsureSchemaAsync();
            var customersTask = PartyRepository.GetAllAsync(companyId.Value, "Customer");
            var suppliersTask = PartyRepository.GetAllAsync(companyId.Value, "Supplier");
            await Task.WhenAll(customersTask, suppliersTask);
            CustomerCountTabLabel.Text = customersTask.Result.Count.ToString();
            SupplierCountTabLabel.Text = suppliersTask.Result.Count.ToString();
            await LoadCurrentTypeAsync();
            _loaded = true;
        }
        finally
        {
            PartySkeleton.IsVisible = false;
        }
    }

    private async Task LoadCurrentTypeAsync()
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;
        var list = string.IsNullOrWhiteSpace(_searchText)
            ? await PartyRepository.GetAllAsync(companyId.Value, _currentType)
            : await PartyRepository.SearchAsync(companyId.Value, _searchText, _currentType);
        _currentParties = list;
        var balances = await BillRepository.GetPartyBalancesAsync(companyId.Value, list.Select(x => x.Id));
        Render(list, balances);
    }

    private void Render(IEnumerable<Party> parties, IReadOnlyDictionary<int, decimal>? balances = null)
    {
        _currentParties = parties.ToList();

        _items.Clear();

        foreach (var party in _currentParties)
            _items.Add(new PartyListItem(
                party,
                _selectedIds.Contains(party.Id),
                balances != null && balances.TryGetValue(party.Id, out var balance) ? balance : party.OpeningBalance));

        EmptyTitleLabel.Text =
            _currentType == "Supplier"
                ? "No suppliers yet"
                : "No customers yet";

        BuildAlphabetBar();

        AlphabetIndexContainer.IsVisible =
            _currentParties.Count > 0;
    }

    private async void PartyRefreshView_Refreshing(object? sender, EventArgs e)
    {
        _loaded = false;
        await RefreshAsync(true);
        PartyRefreshView.IsRefreshing = false;
    }

    private async void CustomerTabTapped(object? sender, TappedEventArgs e) => await SwitchTypeAsync("Customer");
    private async void SupplierTabTapped(object? sender, TappedEventArgs e) => await SwitchTypeAsync("Supplier");

    private async Task SwitchTypeAsync(string type)
    {
        if (_currentType == type) return;
        _currentType = type;
        SetTabVisuals();
        PartySkeleton.IsVisible = true;
        try { _searchText = string.Empty; SearchFilterPill.IsVisible = false; await LoadCurrentTypeAsync(); }
        finally { PartySkeleton.IsVisible = false; }
    }

    private void SetTabVisuals()
    {
        var requestedTheme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        var inactiveBg = requestedTheme == AppTheme.Dark ? Color.FromArgb("#1A2016") : Color.FromArgb("#FFFFFF");
        var inactiveText = requestedTheme == AppTheme.Dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
        var inactiveSecondary = requestedTheme == AppTheme.Dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");

        CustomerTab.BackgroundColor = _currentType == "Customer" ? Color.FromArgb("#718238") : inactiveBg;
        SupplierTab.BackgroundColor = _currentType == "Supplier" ? Color.FromArgb("#718238") : inactiveBg;
        CustomerTabTextLabel.TextColor = _currentType == "Customer" ? Colors.White : inactiveText;
        SupplierTabTextLabel.TextColor = _currentType == "Supplier" ? Colors.White : inactiveText;
        CustomerCountTabLabel.TextColor = _currentType == "Customer" ? Colors.White : inactiveSecondary;
        SupplierCountTabLabel.TextColor = _currentType == "Supplier" ? Colors.White : inactiveSecondary;
    }

    private void AddButtonTapped(object? sender, TappedEventArgs e)
    {
        SearchPanel.IsVisible = false;
        SearchBackdrop.IsVisible = false;
        AddMenuBackdrop.IsVisible = true;
        AddMenu.IsVisible = true;
        AddMenu.Opacity = 0;
        AddMenu.Scale = 0.97;
        _ = Task.WhenAll(AddMenu.FadeToAsync(1, 120, Easing.CubicOut), AddMenu.ScaleToAsync(1, 140, Easing.CubicOut));
    }

    private void AddMenuBackdropTapped(object? sender, TappedEventArgs e) => HideAddMenu();
    private void HideAddMenu() { AddMenuBackdrop.IsVisible = false; AddMenu.IsVisible = false; AddMenu.Opacity = 1; AddMenu.Scale = 1; }

    private async void AddCustomerClicked(object? sender, EventArgs e) { HideAddMenu(); await OpenEditorAsync("Customer", 0); }
    private async void AddSupplierClicked(object? sender, EventArgs e) { HideAddMenu(); await OpenEditorAsync("Supplier", 0); }

    private async Task OpenEditorAsync(string type, int id)
    {
        _editorView ??= new PartyEditorView();
        _editorView.BackClicked -= EditorBackClicked;
        _editorView.PartySaved -= EditorPartySaved;
        _editorView.BackClicked += EditorBackClicked;
        _editorView.PartySaved += EditorPartySaved;
        if (id == 0) _editorView.StartNew(type); else await _editorView.LoadForEditAsync(id);
        PartyListPage.IsVisible = false;
        _editorView.IsVisible = true;
        if (RootChild(_editorView) == null) RootLayout.Children.Add(_editorView);
        await ShowChildAsync(_editorView);
    }

    private async Task OpenDetailAsync(int id)
    {
        _detailView ??= new PartyDetailView();
        _detailView.BackClicked -= DetailBackClicked;
        _detailView.EditClicked -= DetailEditClicked;
        _detailView.Deleted -= DetailDeleted;
        _detailView.BackClicked += DetailBackClicked;
        _detailView.EditClicked += DetailEditClicked;
        _detailView.Deleted += DetailDeleted;
        await _detailView.LoadAsync(id);
        PartyListPage.IsVisible = false;
        _detailView.IsVisible = true;
        if (RootChild(_detailView) == null) RootLayout.Children.Add(_detailView);
        await ShowChildAsync(_detailView);
    }

    private View? RootChild(View view) => RootLayout.Children.Contains(view) ? view : null;

    private async Task ShowChildAsync(View view)
    {
        view.Opacity = 0;
        view.TranslationX = 28;
        view.ZIndex = 200;
        await Task.WhenAll(view.FadeToAsync(1, 180, Easing.CubicOut), view.TranslateToAsync(0, 0, 240, Easing.CubicOut));
    }

    private async void EditorBackClicked(object? sender, EventArgs e) => await ReturnToListAsync();
    private async void DetailBackClicked(object? sender, EventArgs e) => await ReturnToListAsync();

    private async void EditorPartySaved(object? sender, EventArgs e)
    {
        _loaded = false;
        await ReturnToListAsync();
        await RefreshAsync(true);
    }

    private async void DetailEditClicked(
        object? sender,
        int partyId)
    {
        var party =
            await PartyRepository.GetByIdAsync(
                partyId);

        if (party != null)
            await OpenEditorAsync(
                party.PartyType,
                partyId);
    }

    private async void DetailDeleted(object? sender, EventArgs e)
    {
        _loaded = false;
        await ReturnToListAsync();
        await RefreshAsync(true);
    }

    public async Task<bool> HandleBackAsync()
    {
        if (_detailView?.IsVisible == true || _editorView?.IsVisible == true)
        {
            await ReturnToListAsync();
            return true;
        }
        return false;
    }

    private async Task ReturnToListAsync()
    {
        View? child = null;
        if (_detailView?.IsVisible == true)
            child = _detailView;
        else if (_editorView?.IsVisible == true)
            child = _editorView;

        if (child != null)
        {
            await Task.WhenAll(child.FadeToAsync(0, 140, Easing.CubicIn), child.TranslateToAsync(28, 0, 180, Easing.CubicIn));
            child.IsVisible = false;
        }
        PartyListPage.IsVisible = true;
    }

    private async void PartyCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject b && b.BindingContext is PartyListItem item)
            await OpenDetailAsync(item.Party.Id);
    }

    private async void PartyListImageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject b && b.BindingContext is PartyListItem item && item.HasImage)
        {
            await PartyListImagePreview.ShowAsync(item.Party.ImagePath!, item.Name);
        }
    }

    private void BuildAlphabetBar()
    {
        AlphabetBar.Children.Clear();
        _alphabetTargets.Clear();

        const string letters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        for (var i = 0; i < letters.Length; i++)
        {
            var index = i;
            var letter = letters[i];

            var targetIndex =
                _currentParties.FindIndex(x =>
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    char.ToUpperInvariant(
                        x.Name.Trim()[0]) == letter);

            if (targetIndex >= 0)
                _alphabetTargets[letter] =
                    targetIndex;

            var label =
                new Label
                {
                    Text = letter.ToString(),
                    FontSize = 8,
                    FontAttributes =
                        FontAttributes.Bold,
                    TextColor =
                        Color.FromArgb("#718238"),
                    HorizontalOptions =
                        LayoutOptions.Center,
                    VerticalOptions =
                        LayoutOptions.Center,
                    HorizontalTextAlignment =
                        TextAlignment.Center,
                    HeightRequest = 13
                };

            var box =
                new Border
                {
                    HeightRequest = 13,
                    WidthRequest = 20,
                    Padding = 0,
                    BackgroundColor =
                        Colors.Transparent,
                    Stroke =
                        Colors.Transparent,
                    StrokeThickness = 0,
                    Opacity =
                        targetIndex >= 0
                            ? 1
                            : 0.45,
                    Content = label
                };

            var tap =
                new TapGestureRecognizer();

            tap.Tapped += async (_, _) =>
                await SelectAlphabetIndexAsync(
                    index,
                    true);

            box.GestureRecognizers.Add(tap);

            var pan =
                new PanGestureRecognizer();

            pan.PanUpdated += (_, e) =>
                AlphabetItemPanUpdated(
                    e,
                    index);

            box.GestureRecognizers.Add(pan);

            AlphabetBar.Children.Add(box);
        }
    }

    private void AlphabetPanUpdated(
        object? sender,
        PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _activeAlphabetIndex = 0;
            ShowAlphabetBubble('A');
            return;
        }

        if (e.StatusType == GestureStatus.Completed ||
            e.StatusType == GestureStatus.Canceled)
        {
            _activeAlphabetIndex = -1;
            ScheduleAlphabetBubbleHide();
            return;
        }

        if (e.StatusType != GestureStatus.Running)
            return;

        const double step = 13d;

        var index =
            Math.Clamp(
                (int)Math.Round(e.TotalY / step),
                0,
                25);

        if (index == _activeAlphabetIndex)
            return;

        _activeAlphabetIndex = index;

        _ = SelectAlphabetIndexAsync(
            index,
            true);
    }

    private void AlphabetItemPanUpdated(
        PanUpdatedEventArgs e,
        int startIndex)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _activeAlphabetIndex =
                startIndex;

            ShowAlphabetBubble(
                (char)('A' + startIndex));

            return;
        }

        if (e.StatusType == GestureStatus.Completed ||
            e.StatusType == GestureStatus.Canceled)
        {
            _activeAlphabetIndex = -1;
            ScheduleAlphabetBubbleHide();
            return;
        }

        if (e.StatusType != GestureStatus.Running)
            return;

        const double step = 13d;

        var offset =
            (int)Math.Round(
                e.TotalY / step);

        var index =
            Math.Clamp(
                startIndex + offset,
                0,
                25);

        if (index == _activeAlphabetIndex)
            return;

        _activeAlphabetIndex = index;

        _ = SelectAlphabetIndexAsync(
            index,
            true);
    }

    private async Task SelectAlphabetIndexAsync(
        int index,
        bool haptic)
    {
        if (_currentParties.Count == 0)
            return;

        index =
            Math.Clamp(index, 0, 25);

        var letter =
            (char)('A' + index);

        if (!_alphabetTargets.TryGetValue(
                letter,
                out var target))
        {
            target =
                FindNearestAlphabetTarget(
                    index);

            if (target < 0)
            {
                ShowAlphabetBubble(letter);
                return;
            }
        }

        ShowAlphabetBubble(letter);

        if (haptic &&
            HapticFeedback.Default.IsSupported)
        {
            try
            {
                HapticFeedback.Default.Perform(
                    HapticFeedbackType.Click);
            }
            catch
            {
            }
        }

        PartyCollectionView.ScrollTo(target, -1, ScrollToPosition.Start, false);
    }

    private int FindNearestAlphabetTarget(int index)
    {
        for (var i = index; i < 26; i++)
        {
            if (_alphabetTargets.TryGetValue(
                    (char)('A' + i),
                    out var target))
                return target;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (_alphabetTargets.TryGetValue(
                    (char)('A' + i),
                    out var target))
                return target;
        }

        return -1;
    }

    private void ShowAlphabetBubble(char letter)
    {
        AlphabetBubbleLabel.Text =
            letter.ToString();

        _alphabetBubbleCts?.Cancel();
        _alphabetBubbleCts =
            new CancellationTokenSource();

        AlphabetBubble.IsVisible = true;
        AlphabetBubble.Opacity = 1;
        AlphabetBubble.Scale = 1;
    }

    private void ScheduleAlphabetBubbleHide()
    {
        _alphabetBubbleCts?.Cancel();
        _alphabetBubbleCts =
            new CancellationTokenSource();

        _ = HideAlphabetBubbleLaterAsync(
            _alphabetBubbleCts.Token);
    }

    private async Task HideAlphabetBubbleLaterAsync(
        CancellationToken token)
    {
        try
        {
            await Task.Delay(
                500,
                token);

            await AlphabetBubble.FadeToAsync(
                0,
                120,
                Easing.CubicIn);

            AlphabetBubble.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async void SearchTapped(object? sender, TappedEventArgs e)
    {
        HideAddMenu();
        SearchBackdrop.IsVisible = true;
        SearchPanel.IsVisible = true;
        SearchPanel.Opacity = 0;
        SearchPanel.TranslationY = -18;
        await Task.WhenAll(SearchPanel.FadeToAsync(1, 160, Easing.CubicOut), SearchPanel.TranslateToAsync(0, 0, 220, Easing.CubicOut));
        PartySearchBar.Focus();
    }

    private async void PartySearchBar_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue?.Trim() ?? string.Empty;
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            SearchSpinner.IsVisible = false; SearchSpinner.IsRunning = false; PartySearchSuggestions.ItemsSource = null; SearchStatusLabel.Text = "Type to search"; return;
        }
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(350, token);
            SearchSpinner.IsVisible = true; SearchSpinner.IsRunning = true; SearchStatusLabel.Text = "Searching...";
            var companyId = await SessionService.GetActiveCompanyIdAsync();
            if (!companyId.HasValue) return;
            var suggestions = await PartyRepository.SearchAsync(companyId.Value, _searchText, _currentType);
            PartySearchSuggestions.ItemsSource = suggestions.Take(8).Select(x => new PartyListItem(x, false)).ToList();
            SearchStatusLabel.Text = suggestions.Count == 0 ? "No matching parties" : "Suggestions";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SearchStatusLabel.Text = ex.Message; }
        finally { SearchSpinner.IsVisible = false; SearchSpinner.IsRunning = false; }
    }

    private async void PartySearchBar_Completed(object? sender, EventArgs e)
    {
        _searchCts?.Cancel();
        SearchSpinner.IsVisible = true; SearchSpinner.IsRunning = true; SearchStatusLabel.Text = "Searching result";
        try
        {
            await LoadCurrentTypeAsync();
            SearchFilterLabel.Text = $"⌕ {_searchText}"; SearchFilterPill.IsVisible = !string.IsNullOrWhiteSpace(_searchText);
            SearchBackdrop.IsVisible = false; SearchPanel.IsVisible = false;
        }
        finally { SearchSpinner.IsVisible = false; SearchSpinner.IsRunning = false; }
    }

    private async void PartySuggestionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject b && b.BindingContext is PartyListItem item)
        {
            _searchText = string.Empty; PartySearchBar.Text = string.Empty; SearchFilterPill.IsVisible = false; SearchBackdrop.IsVisible = false; SearchPanel.IsVisible = false; PartySearchSuggestions.ItemsSource = null;
            await OpenDetailAsync(item.Party.Id);
        }
    }

    private async void SearchBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PartySearchBar.Text))
        {
            _searchText = string.Empty; SearchFilterPill.IsVisible = false; PartySearchBar.Text = string.Empty; await LoadCurrentTypeAsync();
        }
        SearchBackdrop.IsVisible = false; SearchPanel.IsVisible = false; PartySearchSuggestions.ItemsSource = null;
    }

    private void CloseSearchTapped(object? sender, TappedEventArgs e) { SearchBackdrop.IsVisible = false; SearchPanel.IsVisible = false; PartySearchSuggestions.ItemsSource = null; }
    private async void ClearSearchTapped(object? sender, TappedEventArgs e) { _searchText = string.Empty; PartySearchBar.Text = string.Empty; SearchFilterPill.IsVisible = false; await LoadCurrentTypeAsync(); }
    private async Task AnimateSkeletonWaveAsync() { try { while (true) { if (PartySkeleton.IsVisible) { var width = Math.Max(PartySkeleton.Width, 360); SkeletonWave.TranslationX = -130; await SkeletonWave.TranslateToAsync(width + 130, 0, 850, Easing.Linear); } await Task.Delay(100); } } catch { } }
    private void PartyCollectionView_Scrolled(object? sender, ItemsViewScrolledEventArgs e) { }

    public sealed class PartyListItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public Party Party { get; }
        public string Initials => GetInitials(Party.Name);
        public string ContactSummary => string.Join(" • ", new[] { Party.Mobile, Party.Email }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        public decimal Balance { get; }
        public string BalanceText => $"Balance  {InvoiceFormatService.Money(Balance)}";
        public string Name => Party.Name;
        public string PartyCode => Party.PartyCode ?? $"#{Party.Id:D6}";
        public string PartyType => Party.PartyType;
        public string? ImagePath => Party.ImagePath;
        public bool HasImage => !string.IsNullOrWhiteSpace(Party.ImagePath) && File.Exists(Party.ImagePath);
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
        public PartyListItem(Party party, bool selected, decimal balance = 0) { Party = party; _isSelected = selected; Balance = balance; }
        private static string GetInitials(string name) { var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 0) return "P"; return parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(); }
    }
}
