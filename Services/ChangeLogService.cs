using System.Text.Json;
using HisabKitab.Models;

namespace HisabKitab.Services;

public static class ChangeLogService
{
    public static async Task QueueAsync(string entityType, int entityId, int companyId, string operation, object payload)
    {
        await DatabaseService.InitializeAsync();

        var item = new SyncChange
        {
            CompanyId = companyId,
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(payload),
            IsSynced = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        await DatabaseService.Db.InsertAsync(item);
    }

    public static async Task<List<SyncChange>> GetPendingAsync(int companyId)
    {
        await DatabaseService.InitializeAsync();
        return await DatabaseService.Db.Table<SyncChange>()
            .Where(x => x.CompanyId == companyId && !x.IsSynced)
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    public static async Task MarkSyncedAsync(IEnumerable<int> ids)
    {
        await DatabaseService.InitializeAsync();

        foreach (var id in ids)
        {
            var item = await DatabaseService.Db.Table<SyncChange>().Where(x => x.Id == id).FirstOrDefaultAsync();
            if (item == null) continue;
            item.IsSynced = true;
            item.SyncedAtUtc = DateTime.UtcNow;
            await DatabaseService.Db.UpdateAsync(item);
        }
    }
}
