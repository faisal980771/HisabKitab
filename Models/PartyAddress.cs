using SQLite;

namespace HisabKitab.Models;

public class PartyAddress
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public int PartyId { get; set; }
    [Indexed] public string AddressType { get; set; } = "Billing";
    public string? Label { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Area { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string Country { get; set; } = "India";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
