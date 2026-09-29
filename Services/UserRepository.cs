using HisabKitab.Models;

namespace HisabKitab.Services;

public static class UserRepository
{
    public static async Task<AppUser?> GetByGoogleIdAsync(string googleId)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db
            .Table<AppUser>()
            .Where(x => x.GoogleId == googleId)
            .FirstOrDefaultAsync();
    }

    public static async Task SaveGoogleUserAsync(
        string googleId,
        string name,
        string email)
    {
        await DatabaseService.InitializeAsync();

        var existing = await GetByGoogleIdAsync(googleId);

        if (existing == null)
        {
            var user = new AppUser
            {
                GoogleId = googleId,
                Name = name,
                Email = email,
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            };

            await DatabaseService.Db.InsertAsync(user);
        }
        else
        {
            existing.Name = name;
            existing.Email = email;
            existing.LastLoginAtUtc = DateTime.UtcNow;

            await DatabaseService.Db.UpdateAsync(existing);
        }
    }
}