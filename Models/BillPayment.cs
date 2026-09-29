using SQLite;

namespace HisabKitab.Models;

public class BillPayment
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public int BillId { get; set; }
    [Indexed] public int PartyId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMode { get; set; } = "Cash";
    public string? ReferenceNo { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
