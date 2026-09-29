using SQLite;

namespace HisabKitab.Models;

public class Unit
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
