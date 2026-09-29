using HisabKitab.Models;
using SQLite;

namespace HisabKitab.Services;

public static class PartyRepository
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaReady;

    public static async Task EnsureSchemaAsync()
    {
        if (_schemaReady) return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaReady) return;
            await DatabaseService.InitializeAsync();
            await DatabaseService.Db.CreateTableAsync<Party>();
            await DatabaseService.Db.CreateTableAsync<PartyAddress>();
            await DatabaseService.Db.CreateTableAsync<PartyActivity>();

            await EnsureColumnAsync("Party", "PartyCode", "TEXT");
            await EnsureColumnAsync("Party", "ContactPerson", "TEXT");
            await EnsureColumnAsync("Party", "Mobile2", "TEXT");
            await EnsureColumnAsync("Party", "PAN", "TEXT");
            await EnsureColumnAsync("Party", "CreditLimit", "NUMERIC");
            await EnsureColumnAsync("Party", "PaymentTermsDays", "INTEGER");
            await EnsureColumnAsync("Party", "Notes", "TEXT");
            await EnsureColumnAsync("Party", "ImagePath", "TEXT");
            await DatabaseService.Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Party_CompanyTypeName ON Party(CompanyId, PartyType, Name)");
            await DatabaseService.Db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS UX_Party_CompanyTypeCode ON Party(CompanyId, PartyType, PartyCode) WHERE PartyCode IS NOT NULL AND PartyCode <> ''");
            _schemaReady = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static async Task EnsureColumnAsync(string table, string column, string type)
    {
        var rows = await DatabaseService.Db.QueryAsync<TableInfoRow>($"PRAGMA table_info([{table}])");
        if (!rows.Any(x => string.Equals(x.name, column, StringComparison.OrdinalIgnoreCase)))
            await DatabaseService.Db.ExecuteAsync($"ALTER TABLE [{table}] ADD COLUMN [{column}] {type}");
    }

    private sealed class TableInfoRow
    {
        public int cid { get; set; }
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

    public static async Task<List<Party>> GetAllAsync(int companyId, string? partyType = null)
    {
        await EnsureSchemaAsync();

        if (string.IsNullOrWhiteSpace(partyType))
            return await DatabaseService.Db.QueryAsync<Party>("SELECT * FROM Party WHERE CompanyId = ? AND IsActive = 1 ORDER BY Name COLLATE NOCASE", companyId);

        return await DatabaseService.Db.QueryAsync<Party>("SELECT * FROM Party WHERE CompanyId = ? AND PartyType = ? AND IsActive = 1 ORDER BY Name COLLATE NOCASE", companyId, partyType);
    }

    public static async Task<List<Party>> SearchAsync(int companyId, string search, string partyType = "Supplier")
    {
        await EnsureSchemaAsync();
        if (string.IsNullOrWhiteSpace(search)) return new List<Party>();

        var term = $"%{search.Trim()}%";
        return await DatabaseService.Db.QueryAsync<Party>(
            "SELECT * FROM Party WHERE CompanyId = ? AND PartyType = ? AND IsActive = 1 AND (Name LIKE ? COLLATE NOCASE OR PartyCode LIKE ? COLLATE NOCASE OR Mobile LIKE ? COLLATE NOCASE OR Mobile2 LIKE ? COLLATE NOCASE OR Email LIKE ? COLLATE NOCASE OR GSTIN LIKE ? COLLATE NOCASE OR PAN LIKE ? COLLATE NOCASE) ORDER BY Name COLLATE NOCASE LIMIT 20",
            companyId, partyType, term, term, term, term, term, term, term);
    }

    public static async Task<List<Party>> SearchAllAsync(int companyId, string search)
    {
        await EnsureSchemaAsync();
        if (string.IsNullOrWhiteSpace(search)) return new List<Party>();

        var term = $"%{search.Trim()}%";
        return await DatabaseService.Db.QueryAsync<Party>(
            "SELECT * FROM Party WHERE CompanyId = ? AND IsActive = 1 AND (Name LIKE ? COLLATE NOCASE OR PartyCode LIKE ? COLLATE NOCASE OR Mobile LIKE ? COLLATE NOCASE OR Mobile2 LIKE ? COLLATE NOCASE OR Email LIKE ? COLLATE NOCASE OR GSTIN LIKE ? COLLATE NOCASE OR PAN LIKE ? COLLATE NOCASE) ORDER BY Name COLLATE NOCASE LIMIT 20",
            companyId, term, term, term, term, term, term, term);
    }

    public static async Task<Party?> GetByIdAsync(int id)
    {
        await EnsureSchemaAsync();
        return await DatabaseService.Db.Table<Party>().Where(x => x.Id == id).FirstOrDefaultAsync();
    }

    public static async Task<Party?> FindByNameAsync(int companyId, string name, string partyType)
    {
        await EnsureSchemaAsync();
        return await DatabaseService.Db.QueryAsync<Party>(
            "SELECT * FROM Party WHERE CompanyId = ? AND PartyType = ? AND IsActive = 1 AND lower(trim(Name)) = lower(trim(?)) LIMIT 1",
            companyId, partyType, name).ContinueWith(t => t.Result.FirstOrDefault());
    }

    public static async Task<bool> ExistsByNameAsync(int companyId, string name, string partyType, int excludeId = 0)
    {
        await EnsureSchemaAsync();
        var rows = await DatabaseService.Db.QueryAsync<Party>(
            "SELECT Id FROM Party WHERE CompanyId = ? AND PartyType = ? AND IsActive = 1 AND lower(trim(Name)) = lower(trim(?)) AND Id <> ? LIMIT 1",
            companyId, partyType, name, excludeId);
        return rows.Count > 0;
    }

    public static async Task<int> SaveAsync(Party party)
    {
        await EnsureSchemaAsync();
        party.Name = party.Name.Trim();
        party.PartyType = NormalizeType(party.PartyType);
        party.UpdatedAtUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(party.PartyCode)) party.PartyCode = await GeneratePartyCodeAsync(party.CompanyId, party.PartyType);

        if (party.Id == 0)
        {
            party.CreatedAtUtc = DateTime.UtcNow;
            await DatabaseService.Db.InsertAsync(party);
            await ChangeLogService.QueueAsync("Party", party.Id, party.CompanyId, "CREATE", party);
            await PartyActivityRepository.AddAsync(new PartyActivity
            {
                CompanyId = party.CompanyId, PartyId = party.Id,
                ActivityType = "CREATE", Title = "Party created",
                Description = $"{party.PartyType} account created.",
                ActivityDateUtc = DateTime.UtcNow
            });
        }
        else
        {
            await DatabaseService.Db.UpdateAsync(party);
            await ChangeLogService.QueueAsync("Party", party.Id, party.CompanyId, "UPDATE", party);
            await PartyActivityRepository.AddAsync(new PartyActivity
            {
                CompanyId = party.CompanyId, PartyId = party.Id,
                ActivityType = "UPDATE", Title = "Party updated",
                Description = "Party details were updated.",
                ActivityDateUtc = DateTime.UtcNow
            });
        }
        return party.Id;
    }

    public static async Task<(bool Allowed, string Reason)> CanDeleteAsync(int partyId)
    {
        await EnsureSchemaAsync();
        var party = await GetByIdAsync(partyId);
        if (party == null) return (false, "Party not found.");

        var relations = new[]
        {
            ("StockTransaction", "SupplierPartyId", "stock entries"),
            ("Sales", "CustomerPartyId", "sales invoices"),
            ("Sale", "CustomerPartyId", "sales invoices"),
            ("Purchase", "SupplierPartyId", "purchase entries"),
            ("PurchaseInvoice", "SupplierPartyId", "purchase invoices"),
            ("Receipt", "PartyId", "receipts"),
            ("Payment", "PartyId", "payments"),
            ("LedgerEntry", "PartyId", "ledger entries"),
            ("PartyLedger", "PartyId", "ledger entries")
        };

        foreach (var relation in relations)
        {
            if (!await TableExistsAsync(relation.Item1) || !await ColumnExistsAsync(relation.Item1, relation.Item2)) continue;
            var table = QuoteIdentifier(relation.Item1);
            var column = QuoteIdentifier(relation.Item2);
            var rows = await DatabaseService.Db.QueryScalarsAsync<int>(
                $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table} WHERE {column} = ? LIMIT 1) THEN 1 ELSE 0 END",
                partyId);
            if (rows.FirstOrDefault() == 1) return (false, $"This party is already linked with {relation.Item3}. It cannot be deleted.");
        }

        var tables = await DatabaseService.Db.QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");

        foreach (var tableName in tables)
        {
            if (string.Equals(tableName, "Party", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "PartyAddress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "PartyActivity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tableName, "SyncChange", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var columnName in new[] { "PartyId", "CustomerPartyId", "SupplierPartyId" })
            {
                if (!await ColumnExistsAsync(tableName, columnName)) continue;
                var rows = await DatabaseService.Db.QueryScalarsAsync<int>(
                    $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(columnName)} = ? LIMIT 1) THEN 1 ELSE 0 END",
                    partyId);
                if (rows.FirstOrDefault() == 1) return (false, "This party is linked with existing transactions or ledger data. It cannot be deleted.");
            }
        }

        return (true, string.Empty);
    }

    public static async Task<bool> DeleteAsync(int partyId)
    {
        await EnsureSchemaAsync();
        var party = await GetByIdAsync(partyId);
        if (party == null) return false;
        var deleteCheck = await CanDeleteAsync(partyId);
        if (!deleteCheck.Allowed) return false;

        party.IsActive = false;
        party.UpdatedAtUtc = DateTime.UtcNow;
        await DatabaseService.Db.UpdateAsync(party);
        await ChangeLogService.QueueAsync("Party", party.Id, party.CompanyId, "DELETE", party);
        await PartyActivityRepository.AddAsync(new PartyActivity
        {
            CompanyId = party.CompanyId, PartyId = party.Id,
            ActivityType = "DELETE", Title = "Party archived",
            Description = "Party was removed from the active party list.",
            ActivityDateUtc = DateTime.UtcNow
        });
        return true;
    }

    public static async Task<int> GetCountAsync(int companyId, string partyType)
    {
        await EnsureSchemaAsync();
        var rows = await DatabaseService.Db.QueryScalarsAsync<int>("SELECT COUNT(*) FROM Party WHERE CompanyId = ? AND PartyType = ? AND IsActive = 1", companyId, partyType);
        return rows.FirstOrDefault();
    }

    public static async Task<int> GetOrCreateSupplierAsync(int companyId, string name)
    {
        await EnsureSchemaAsync();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var existing = await FindByNameAsync(companyId, name, "Supplier");
        if (existing != null) return existing.Id;
        var party = new Party { CompanyId = companyId, PartyType = "Supplier", Name = name, IsActive = true };
        await SaveAsync(party);
        return party.Id;
    }

    public static async Task<int> GetOrCreateCustomerAsync(int companyId, string name)
    {
        await EnsureSchemaAsync();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var existing = await FindByNameAsync(companyId, name, "Customer");
        if (existing != null) return existing.Id;
        var party = new Party { CompanyId = companyId, PartyType = "Customer", Name = name, IsActive = true };
        await SaveAsync(party);
        return party.Id;
    }

    private static string NormalizeType(string? value) => string.Equals(value, "Supplier", StringComparison.OrdinalIgnoreCase) ? "Supplier" : "Customer";

    private static async Task<string> GeneratePartyCodeAsync(int companyId, string partyType)
    {
        var prefix = partyType == "Supplier" ? "SUP" : "CUS";
        var rows = await DatabaseService.Db.QueryScalarsAsync<int>($"SELECT COUNT(*) FROM Party WHERE CompanyId = ? AND PartyType = ?", companyId, partyType);
        return $"{prefix}{rows.FirstOrDefault() + 1:D6}";
    }

    private static async Task<bool> TableExistsAsync(string table)
    {
        var rows = await DatabaseService.Db.QueryScalarsAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name = ? LIMIT 1", table);
        return rows.Count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(string table, string column)
    {
        var rows = await DatabaseService.Db.QueryAsync<TableInfoRow>($"PRAGMA table_info([{table.Replace("]", "]]", StringComparison.Ordinal)}])");
        return rows.Any(x => string.Equals(x.name, column, StringComparison.OrdinalIgnoreCase));
    }

    private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
