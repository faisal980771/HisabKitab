namespace HisabKitab.Services;

public static class ThemeService
{
    public enum ThemeMode
    {
        System,
        Light,
        Dark
    }

    public static ThemeMode CurrentMode { get; private set; }
        = ThemeMode.System;

    public static void ApplyTheme(ThemeMode mode)
    {
        CurrentMode = mode;

        if (Application.Current == null)
            return;

        switch (mode)
        {
            case ThemeMode.Light:

                Application.Current.UserAppTheme = AppTheme.Light;
                break;

            case ThemeMode.Dark:

                Application.Current.UserAppTheme = AppTheme.Dark;
                break;

            default:

                Application.Current.UserAppTheme = AppTheme.Unspecified;
                break;
        }
    }

    public static bool IsDarkMode()
    {
        if (Application.Current == null)
            return false;

        return Application.Current.RequestedTheme == AppTheme.Dark;
    }
}