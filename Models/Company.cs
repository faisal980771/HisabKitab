using SQLite;

namespace HisabKitab.Models;

public class Company
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public string CompanyCode { get; set; } = string.Empty;

    [Indexed]
    public string OwnerGoogleId { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string? LegalName { get; set; }

    public string? OwnerName { get; set; }

    public string Mobile { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string IndustryType { get; set; } = string.Empty;

    public string? BusinessType { get; set; }

    public string? GSTIN { get; set; }

    public string? PAN { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Pincode { get; set; }

    /// <summary>Persistent local path to the company logo used in invoice templates.</summary>
    public string? LogoPath { get; set; }

    public string Country { get; set; } = "India";

    public string Currency { get; set; } = "INR";

    public int FinancialYearStartMonth { get; set; } = 4;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}