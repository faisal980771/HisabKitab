using SQLite;

namespace HisabKitab.Models;

public class BillTemplate
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string DocumentType { get; set; } = "Sales";
    public string Name { get; set; } = string.Empty;
    public string LayoutKey { get; set; } = "Modern";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
