using SQLite;

namespace HisabKitab.Models;

public class BillItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int BillId { get; set; }
    [Indexed] public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string? ProductBarcode { get; set; }
    /// <summary>Snapshot of the product image path at invoice creation time.</summary>
    public string? ProductImagePath { get; set; }
    public string? ProductHSN { get; set; }
    public string Unit { get; set; } = "Piece";
    public decimal Quantity { get; set; } = 1;
    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
}
