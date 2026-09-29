using SQLite;

namespace HisabKitab.Models;

public class SyncChange
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string EntityType { get; set; } = string.Empty;
    [Indexed] public int EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    [Indexed] public bool IsSynced { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAtUtc { get; set; }
}
