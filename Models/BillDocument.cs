using SQLite;

namespace HisabKitab.Models;

public class BillDocument
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string DocumentType { get; set; } = "Sales";
    [Indexed] public string DocumentNo { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    [Indexed] public int PartyId { get; set; }
    public string PartyType { get; set; } = "Customer";
    public string PartyName { get; set; } = string.Empty;
    public string? PartyMobile { get; set; }
    public string? PartyEmail { get; set; }
    public string? PartyGSTIN { get; set; }
    public string? PartyPAN { get; set; }
    public string? ContactPerson { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? PaymentMode { get; set; }
    public string? Notes { get; set; }
    public int TemplateId { get; set; }
    public string Status { get; set; } = "Confirmed";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
