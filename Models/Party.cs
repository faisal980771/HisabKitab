using SQLite;

namespace HisabKitab.Models;

public class Party
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string PartyType { get; set; } = "Customer";
    [Indexed] public string Name { get; set; } = string.Empty;
    public string? PartyCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? Mobile { get; set; }
    public string? Mobile2 { get; set; }
    public string? Email { get; set; }
    /// <summary>Persistent local path to the party photo/logo shown in party and invoice views.</summary>
    public string? ImagePath { get; set; }
    public string? GSTIN { get; set; }
    public string? PAN { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
