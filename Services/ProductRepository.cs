using HisabKitab.Models;

namespace HisabKitab.Services;

public static class ProductRepository
{
    public static async Task<int> SaveAsync(Product product)
    {
        await DatabaseService.InitializeAsync();

        var companyId = product.CompanyId > 0
            ? product.CompanyId
            : await SessionService.GetActiveCompanyIdAsync() ?? 0;

        if (companyId <= 0)
            throw new InvalidOperationException("No active company selected.");

        product.CompanyId = companyId;
        product.Name = product.Name.Trim();
        product.ProductCode = product.ProductCode?.Trim() ?? string.Empty;
        product.Barcode = string.IsNullOrWhiteSpace(product.Barcode) ? null : product.Barcode.Trim();
        product.UpdatedAtUtc = DateTime.UtcNow;

        if (product.Id == 0)
        {
            product.CreatedAtUtc = DateTime.UtcNow;

            // CurrentStock starts at zero. Opening stock is posted through StockTransaction below.
            product.CurrentStock = 0;

            await DatabaseService.Db.InsertAsync(product);

            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                product.ProductCode = $"PRD{product.Id:D6}";
                await DatabaseService.Db.UpdateAsync(product);
            }

            if (product.OpeningStock > 0)
            {
                await StockRepository.AddAsync(new StockTransaction
                {
                    CompanyId = companyId,
                    ProductId = product.Id,
                    TransactionType = "OPENING",
                    Quantity = product.OpeningStock,
                    Rate = product.OpeningRate,
                    Amount = Math.Round(product.OpeningStock * product.OpeningRate, 2),
                    TransactionDate = product.CreatedAtUtc.ToLocalTime().Date,
                    Notes = "Opening stock"
                });
            }

            var saved = await GetByIdAsync(product.Id);
            await ChangeLogService.QueueAsync("Product", product.Id, companyId, "CREATE", saved ?? product);
        }
        else
        {
            await DatabaseService.Db.UpdateAsync(product);
            await ChangeLogService.QueueAsync("Product", product.Id, companyId, "UPDATE", product);
        }

        return product.Id;
    }

    public static async Task<List<Product>> GetAllAsync(int companyId)
    {
        await DatabaseService.InitializeAsync();

        return await DatabaseService.Db.Table<Product>()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public static async Task<Product?> GetByIdAsync(int id)
    {
        await DatabaseService.InitializeAsync();
        return await DatabaseService.Db.Table<Product>().Where(x => x.Id == id).FirstOrDefaultAsync();
    }

    public static async Task<bool> ExistsByNameAsync(int companyId, string name, int excludeId = 0)
    {
        await DatabaseService.InitializeAsync();

        var list = await DatabaseService.Db.Table<Product>()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Name == name)
            .ToListAsync();

        return list.Any(x => x.Id != excludeId);
    }

    public static async Task DeleteAsync(int productId)
    {
        await DatabaseService.InitializeAsync();

        var product = await GetByIdAsync(productId);
        if (product == null) return;

        product.IsActive = false;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await DatabaseService.Db.UpdateAsync(product);
        await ChangeLogService.QueueAsync("Product", product.Id, product.CompanyId, "DELETE", product);
    }
}
