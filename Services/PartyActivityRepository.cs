using HisabKitab.Models;

namespace HisabKitab.Services;

public static class PartyActivityRepository
{
    public static async Task AddAsync(PartyActivity activity)
    {
        await PartyRepository.EnsureSchemaAsync();
        activity.CreatedAtUtc = DateTime.UtcNow;
        if (activity.ActivityDateUtc == default) activity.ActivityDateUtc = DateTime.UtcNow;
        await DatabaseService.Db.InsertAsync(activity);
    }

    public static async Task<List<PartyActivity>> GetAllAsync(int partyId)
    {
        await PartyRepository.EnsureSchemaAsync();
        return await DatabaseService.Db.Table<PartyActivity>().Where(x => x.PartyId == partyId).OrderByDescending(x => x.ActivityDateUtc).ToListAsync();
    }
}
