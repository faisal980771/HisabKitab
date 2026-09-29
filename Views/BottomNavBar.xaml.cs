using Microsoft.Maui.Devices;
namespace HisabKitab.Views;
public partial class BottomNavBar : ContentView
{
    private bool _initialized;
    private CancellationTokenSource? _pillCts;
    public event EventHandler<string>? TabChanged;
    public static readonly BindableProperty CurrentTabProperty=BindableProperty.Create(nameof(CurrentTab),typeof(string),typeof(BottomNavBar),"Home",propertyChanged:(b,o,n)=>{if(b is BottomNavBar bar&&n is string tab)bar.ApplyTabState(tab);});
    public string CurrentTab{get=>(string)GetValue(CurrentTabProperty);set=>SetValue(CurrentTabProperty,value);}
    public BottomNavBar(){InitializeComponent();Loaded+=OnLoaded;SizeChanged+=OnSizeChanged;}
    private void OnLoaded(object? s,EventArgs e){_initialized=true;ResetInteraction();}
    private void OnSizeChanged(object? s,EventArgs e){if(_initialized)SetPillPosition(GetTabIndex(CurrentTab));}
    public void ResetInteraction(){_pillCts?.Cancel();IsVisible=true;IsEnabled=true;InputTransparent=false;ApplyTabState(CurrentTab);}
    private void NavTapped(object? s,TappedEventArgs e){var tab=e.Parameter?.ToString();if(string.IsNullOrWhiteSpace(tab)||tab==CurrentTab)return;var oldIndex=GetTabIndex(CurrentTab);var newIndex=GetTabIndex(tab);PerformHaptic();CurrentTab=tab;ApplyTabState(tab);TabChanged?.Invoke(this,tab);_=_AnimatePillAsync(oldIndex,newIndex);}
    private async Task _AnimatePillAsync(int oldIndex,int newIndex){_pillCts?.Cancel();_pillCts=new CancellationTokenSource();var token=_pillCts.Token;try{var w=GetTabWidth();if(w<=0)return;await ActivePill.ScaleToAsync(1.02,60,Easing.CubicOut);if(token.IsCancellationRequested)return;await ActivePill.TranslateToAsync(newIndex*w,0,280,Easing.CubicInOut);if(token.IsCancellationRequested)return;await ActivePill.ScaleToAsync(1,90,Easing.CubicOut);}catch{}finally{SetPillPosition(GetTabIndex(CurrentTab));}}
    private void ApplyTabState(string tab){if(!_initialized)return;SetLabelsAndIcons(tab);SetPillPosition(GetTabIndex(tab));}
    private void SetPillPosition(int index){var w=GetTabWidth();if(w<=0)return;ActivePill.WidthRequest=Math.Max(0,w-4);ActivePill.TranslationX=index*w;}
    private double GetTabWidth()=>NavGrid.Width>0?NavGrid.Width/5:0;
    private static int GetTabIndex(string tab)=>tab switch{"Bill"=>1,"Party"=>2,"Stock"=>3,"More"=>4,_=>0};
    private void SetLabelsAndIcons(string tab){var inactive=App.Current?.RequestedTheme==AppTheme.Dark?Color.FromArgb("#DCE4D0"):Color.FromArgb("#737A6B");var active=Color.FromArgb("#718238");HomeLabel.TextColor=tab=="Home"?active:inactive;BillLabel.TextColor=tab=="Bill"?active:inactive;PartyLabel.TextColor=tab=="Party"?active:inactive;StockLabel.TextColor=tab=="Stock"?active:inactive;MoreLabel.TextColor=tab=="More"?active:inactive;}
    private static void PerformHaptic(){try{if(HapticFeedback.Default.IsSupported)HapticFeedback.Default.Perform(HapticFeedbackType.Click);}catch{}}
}
