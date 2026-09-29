namespace HisabKitab.Models;

public sealed class BillCreateRequestEventArgs : EventArgs
{
    public BillCreateRequestEventArgs(string documentType, IEnumerable<int> productIds)
    {
        DocumentType = documentType;
        ProductIds = productIds.Distinct().ToList();
    }

    public string DocumentType { get; }
    public IReadOnlyList<int> ProductIds { get; }
}
