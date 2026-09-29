namespace HisabKitab.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
    }

    private async void InvoiceTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert(
            "New Invoice",
            "Invoice module will be connected here.",
            "OK");
    }

    private async void CustomerTapped(object sender, TappedEventArgs e)
    {
        await DisplayAlert(
            "Customer",
            "Customer module will be connected here.",
            "OK");
    }
}