using HisabKitab.Models;

namespace HisabKitab.Services;

public static class CompanyRepository
{
    public static async Task<bool> ExistsForUserAsync(
        string googleId)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db
            .Table<Company>()
            .Where(x => x.OwnerGoogleId == googleId)
            .CountAsync() > 0;
    }

    public static async Task<List<Company>> GetAllForUserAsync(
        string googleId)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db
            .Table<Company>()
            .Where(x => x.OwnerGoogleId == googleId)
            .OrderBy(x => x.CompanyName)
            .ToListAsync();
    }

    public static async Task<Company?> GetByIdAsync(int id)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db
            .Table<Company>()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    public static async Task<int> SaveAsync(Company company)
    {
        await DatabaseService.InitializeAsync();

        if (company.Id == 0)
        {
            company.CreatedAtUtc = DateTime.UtcNow;
            company.UpdatedAtUtc = DateTime.UtcNow;

            await DatabaseService.Db.InsertAsync(company);

            // Example: CMP000001
            company.CompanyCode =
                $"CMP{company.Id:D6}";

            await DatabaseService.Db.UpdateAsync(company);
        }
        else
        {
            company.UpdatedAtUtc = DateTime.UtcNow;

            await DatabaseService.Db.UpdateAsync(company);
        }

        return company.Id;
    }

    public static async Task<List<Company>> SearchAsync(
        string googleId,
        string search)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db
            .Table<Company>()
            .Where(x =>
                x.OwnerGoogleId == googleId &&
                x.CompanyName.Contains(search))
            .ToListAsync();
    }
}