using HisabKitab.Models;
using HisabKitab.Services;
using Microsoft.Maui.Controls.Shapes;

namespace HisabKitab.Views.Dashboard;

public partial class UnitMasterView : ContentView
{
    public event EventHandler? BackClicked;

    public UnitMasterView()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        var companyId = await SessionService.GetActiveCompanyIdAsync();
        if (!companyId.HasValue) return;

        var units = await UnitRepository.GetAllAsync(companyId.Value);

        UnitListContainer.Children.Clear();

        foreach (var unit in units)
            UnitListContainer.Children.Add(CreateUnitCard(unit));
    }

    private View CreateUnitCard(Unit unit)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var primaryText = dark ? Color.FromArgb("#F4F6EF") : Color.FromArgb("#20251C");
        var secondaryText = dark ? Color.FromArgb("#A8AF9E") : Color.FromArgb("#737A6B");

        var name = new Label
        {
            Text = unit.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = primaryText
        };

        var shortName = new Label
        {
            Text = string.IsNullOrWhiteSpace(unit.ShortName) ? "No short name" : unit.ShortName,
            FontSize = 10,
            TextColor = secondaryText
        };

        var edit = new Button
        {
            Text = "Edit",
            FontSize = 11,
            HeightRequest = 34,
            WidthRequest = 58,
            CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#EEF2E3"),
            TextColor = Color.FromArgb("#718238"),
            FontAttributes = FontAttributes.Bold
        };

        var delete = new Button
        {
            Text = "Delete",
            FontSize = 11,
            HeightRequest = 34,
            WidthRequest = 62,
            CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#F9EDED"),
            TextColor = Color.FromArgb("#C95D5D"),
            FontAttributes = FontAttributes.Bold
        };

        edit.Clicked += async (_, _) => await EditUnitAsync(unit);
        delete.Clicked += async (_, _) => await DeleteUnitAsync(unit);

        var info = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        info.Children.Add(name);
        info.Children.Add(shortName);

        var actions = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        actions.Children.Add(edit);
        actions.Children.Add(delete);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 132 }
            },
            ColumnSpacing = 8
        };

        grid.Children.Add(info);
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 1);

        return new Border
        {
            BackgroundColor = dark ? Color.FromArgb("#1A2016") : Color.FromArgb("#FFFFFF"),
            Stroke = dark ? Color.FromArgb("#30382B") : Color.FromArgb("#E3E7DC"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = grid
        };
    }

    private async Task EditUnitAsync(Unit unit)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var newName = await page.DisplayPromptAsync(
            "Edit Unit",
            "Unit name",
            initialValue: unit.Name,
            accept: "Save",
            cancel: "Cancel");

        if (string.IsNullOrWhiteSpace(newName))
            return;

        var shortName = await page.DisplayPromptAsync(
            "Edit Unit",
            "Short name (optional)",
            initialValue: unit.ShortName ?? string.Empty,
            accept: "Save",
            cancel: "Cancel");

        try
        {
            unit.Name = newName.Trim();
            unit.ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim();

            await UnitRepository.UpdateAsync(unit);
            await RefreshAsync();
            await ToastService.Success("Unit updated successfully.");
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
    }

    private async Task DeleteUnitAsync(Unit unit)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var confirm = await page.DisplayAlertAsync(
            "Delete Unit",
            $"Delete '{unit.Name}'?",
            "Delete",
            "Cancel");

        if (!confirm) return;

        try
        {
            await UnitRepository.DeleteAsync(unit.Id);
            await RefreshAsync();
            await ToastService.Success("Unit deleted.");
        }
        catch (Exception ex)
        {
            await ToastService.Warning(ex.Message);
        }
    }

    private async void AddUnitTapped(object? sender, TappedEventArgs e)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var name = await page.DisplayPromptAsync(
            "Add Unit",
            "Unit name",
            accept: "Add",
            cancel: "Cancel");

        if (string.IsNullOrWhiteSpace(name))
            return;

        var shortName = await page.DisplayPromptAsync(
            "Add Unit",
            "Short name (optional)",
            accept: "Add",
            cancel: "Cancel");

        try
        {
            var companyId = await SessionService.GetActiveCompanyIdAsync();
            if (!companyId.HasValue) return;

            await UnitRepository.AddAsync(companyId.Value, name, shortName);
            await RefreshAsync();
            await ToastService.Success("Unit added successfully.");
        }
        catch (Exception ex)
        {
            await ToastService.Error(ex.Message);
        }
    }

    private void BackTapped(object? sender, TappedEventArgs e) =>
        BackClicked?.Invoke(this, EventArgs.Empty);

    private async void UnitRefreshView_Refreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        UnitRefreshView.IsRefreshing = false;
    }
}
