using HisabKitab.Models;

namespace HisabKitab.Services;

public static class StockRepository
{
    public static async Task<int> AddAsync(StockTransaction transaction)
    {
        await DatabaseService.InitializeAsync();

        if (transaction.CompanyId <= 0)
            transaction.CompanyId = await SessionService.GetActiveCompanyIdAsync() ?? 0;

        if (transaction.CompanyId <= 0)
            throw new InvalidOperationException("No active company selected.");

        if (transaction.ProductId <= 0)
            throw new InvalidOperationException("Invalid product.");

        if (transaction.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        var product = await ProductRepository.GetByIdAsync(transaction.ProductId);
        if (product == null || !product.IsActive)
            throw new InvalidOperationException("Product not found.");

        transaction.CreatedAtUtc = DateTime.UtcNow;
        transaction.TransactionType = string.IsNullOrWhiteSpace(transaction.TransactionType) ? "IN" : transaction.TransactionType;
        transaction.Amount = Math.Round(transaction.Quantity * transaction.Rate, 2);

        await DatabaseService.Db.InsertAsync(transaction);

        var sign = transaction.TransactionType is "OUT" or "SALE" ? -1 : 1;
        product.CurrentStock += transaction.Quantity * sign;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await DatabaseService.Db.UpdateAsync(product);

        await ChangeLogService.QueueAsync("StockTransaction", transaction.Id, transaction.CompanyId, "CREATE", transaction);
        await ChangeLogService.QueueAsync("Product", product.Id, product.CompanyId, "STOCK_UPDATE", product);

        return transaction.Id;
    }

    public static async Task<List<StockTransaction>> GetForProductAsync(int productId)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db.Table<StockTransaction>()
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }
}
