namespace HisabKitab.Models;

public sealed class PartyBalance
{
    public decimal OpeningBalance { get; init; }
    public decimal SalesBalance { get; init; }
    public decimal PurchaseBalance { get; init; }
    public decimal NetBalance => Math.Round(OpeningBalance + SalesBalance + PurchaseBalance, 2);
}
