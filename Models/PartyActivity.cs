using SQLite;

namespace HisabKitab.Models;

public class PartyActivity
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public int PartyId { get; set; }
    public string ActivityType { get; set; } = "INFO";
    public string? RefType { get; set; }
    public int? RefId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    [Indexed] public DateTime ActivityDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
