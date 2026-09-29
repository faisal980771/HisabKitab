using SQLite;

namespace HisabKitab.Models;

public class Product
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public int CompanyId { get; set; }
    [Indexed] public string ProductCode { get; set; } = string.Empty;
    [Indexed] public string Name { get; set; } = string.Empty;
    [Indexed] public string? Barcode { get; set; }
    public string Unit { get; set; } = "Piece";
    public string? Category { get; set; }
    public string? HSN { get; set; }
    public decimal GSTPercent { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SaleRate { get; set; }
    public decimal OpeningStock { get; set; }
    public decimal OpeningRate { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
