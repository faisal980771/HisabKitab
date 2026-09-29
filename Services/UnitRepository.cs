using HisabKitab.Models;

namespace HisabKitab.Services;

public static class UnitRepository
{
    private static readonly string[] DefaultUnits =
    {
        "Piece", "Kg", "Gram", "Litre", "Meter", "Box", "Pack", "Dozen", "Set"
    };

    public static async Task<List<Unit>> GetAllAsync(int companyId)
    {
        await DatabaseService.InitializeAsync();

        var units = await DatabaseService.Db.Table<Unit>()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        if (units.Count == 0)
        {
            foreach (var name in DefaultUnits)
                await DatabaseService.Db.InsertAsync(new Unit { CompanyId = companyId, Name = name, IsActive = true });

            units = await DatabaseService.Db.Table<Unit>()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        return units;
    }

    public static async Task<int> AddAsync(int companyId, string name, string? shortName = null)
    {
        await DatabaseService.InitializeAsync();

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Unit name is required.");

        var exists = await DatabaseService.Db.Table<Unit>()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Name == name)
            .CountAsync() > 0;

        if (exists)
            throw new InvalidOperationException("This unit already exists.");

        var unit = new Unit
        {
            CompanyId = companyId,
            Name = name,
            ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await DatabaseService.Db.InsertAsync(unit);
        await ChangeLogService.QueueAsync("Unit", unit.Id, companyId, "CREATE", unit);
        return unit.Id;
    }

    public static async Task UpdateAsync(Unit unit)
    {
        await DatabaseService.InitializeAsync();

        var old = await DatabaseService.Db.Table<Unit>()
            .Where(x => x.Id == unit.Id)
            .FirstOrDefaultAsync();

        if (old == null)
            throw new InvalidOperationException("Unit not found.");

        var newName = unit.Name.Trim();

        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("Unit name is required.");

        var duplicate = await DatabaseService.Db.Table<Unit>()
            .Where(x => x.CompanyId == unit.CompanyId && x.IsActive && x.Name == newName)
            .ToListAsync();

        if (duplicate.Any(x => x.Id != unit.Id))
            throw new InvalidOperationException("This unit already exists.");

        var oldName = old.Name;

        unit.Name = newName;
        unit.ShortName = string.IsNullOrWhiteSpace(unit.ShortName) ? null : unit.ShortName.Trim();
        unit.UpdatedAtUtc = DateTime.UtcNow;

        await DatabaseService.Db.UpdateAsync(unit);

        // Keep existing products aligned with the renamed master unit.
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            var products = await DatabaseService.Db.Table<Product>()
                .Where(x => x.CompanyId == unit.CompanyId && x.IsActive && x.Unit == oldName)
                .ToListAsync();

            foreach (var product in products)
            {
                product.Unit = newName;
                product.UpdatedAtUtc = DateTime.UtcNow;
                await DatabaseService.Db.UpdateAsync(product);
                await ChangeLogService.QueueAsync("Product", product.Id, product.CompanyId, "UPDATE_UNIT", product);
            }
        }

        await ChangeLogService.QueueAsync("Unit", unit.Id, unit.CompanyId, "UPDATE", unit);
    }

    public static async Task DeleteAsync(int unitId)
    {
        await DatabaseService.InitializeAsync();

        var unit = await DatabaseService.Db.Table<Unit>()
            .Where(x => x.Id == unitId && x.IsActive)
            .FirstOrDefaultAsync();

        if (unit == null)
            return;

        var productCount = await DatabaseService.Db.Table<Product>()
            .Where(x => x.CompanyId == unit.CompanyId && x.IsActive && x.Unit == unit.Name)
            .CountAsync();

        if (productCount > 0)
            throw new InvalidOperationException($"Unit '{unit.Name}' is used by {productCount} product(s). Change those products to another unit before deleting it.");

        unit.IsActive = false;
        await DatabaseService.Db.UpdateAsync(unit);
        await ChangeLogService.QueueAsync("Unit", unit.Id, unit.CompanyId, "DELETE", unit);
    }
}
