namespace HisabKitab.Services;

public static class SessionService
{
    private const string IsLoggedInKey = "IsLoggedIn";
    private const string GoogleIdKey = "GoogleId";
    private const string EmailKey = "Email";
    private const string NameKey = "Name";
    private const string ActiveCompanyIdKey = "ActiveCompanyId";

    public static async Task SaveSessionAsync(
        string googleId,
        string email,
        string name)
    {
        await SecureStorage.Default.SetAsync(
            IsLoggedInKey,
            "true");

        await SecureStorage.Default.SetAsync(
            GoogleIdKey,
            googleId);

        await SecureStorage.Default.SetAsync(
            EmailKey,
            email);

        await SecureStorage.Default.SetAsync(
            NameKey,
            name);
    }

    public static async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var value =
                await SecureStorage.Default.GetAsync(
                    IsLoggedInKey);

            return value == "true";
        }
        catch
        {
            return false;
        }
    }

    public static Task<string?> GetGoogleIdAsync()
        => SecureStorage.Default.GetAsync(GoogleIdKey);

    public static Task<string?> GetEmailAsync()
        => SecureStorage.Default.GetAsync(EmailKey);

    public static Task<string?> GetNameAsync()
        => SecureStorage.Default.GetAsync(NameKey);

    public static async Task SetActiveCompanyIdAsync(
        int companyId)
    {
        await SecureStorage.Default.SetAsync(
            ActiveCompanyIdKey,
            companyId.ToString());
    }

    public static async Task<int?> GetActiveCompanyIdAsync()
    {
        var value =
            await SecureStorage.Default.GetAsync(
                ActiveCompanyIdKey);

        if (int.TryParse(value, out var id))
            return id;

        return null;
    }

    public static void Logout()
    {
        SecureStorage.Default.Remove(IsLoggedInKey);
        SecureStorage.Default.Remove(GoogleIdKey);
        SecureStorage.Default.Remove(EmailKey);
        SecureStorage.Default.Remove(NameKey);
        SecureStorage.Default.Remove(ActiveCompanyIdKey);
    }
}