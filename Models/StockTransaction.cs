using SQLite;

namespace HisabKitab.Models;

public class StockTransaction
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public int ProductId { get; set; }
    [Indexed] public int? SupplierPartyId { get; set; }
    public string TransactionType { get; set; } = "IN";
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.Today;
    public string? SupplierName { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
