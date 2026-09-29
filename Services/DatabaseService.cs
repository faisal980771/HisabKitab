using SQLite;
using HisabKitab.Models;

namespace HisabKitab.Services;

public static class DatabaseService
{
    private static SQLiteAsyncConnection? _database;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task InitializeAsync()
    {
        if (_database != null) return;

        await _lock.WaitAsync();
        try
        {
            if (_database != null) return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "HisabKitab.db3");
            _database = new SQLiteAsyncConnection(dbPath);

            await _database.CreateTableAsync<AppUser>();
            await _database.CreateTableAsync<Company>();
            await _database.CreateTableAsync<Product>();
            await _database.CreateTableAsync<SyncChange>();
            await _database.CreateTableAsync<StockTransaction>();
            await _database.CreateTableAsync<Party>();
            await _database.CreateTableAsync<Unit>();

            await EnsureColumnAsync("Product", "ImagePath", "TEXT");
            await EnsureColumnAsync("Company", "LogoPath", "TEXT");
            await EnsureColumnAsync("Party", "ImagePath", "TEXT");
            await EnsureColumnAsync("Product", "CurrentStock", "NUMERIC");
            await EnsureColumnAsync("StockTransaction", "SupplierPartyId", "INTEGER");
            await EnsureColumnAsync("BillItem", "ProductImagePath", "TEXT");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task EnsureColumnAsync(string table, string column, string type)
    {
        try
        {
            var rows = await _database!.QueryAsync<TableInfoRow>($"PRAGMA table_info({table})");
            if (!rows.Any(x => string.Equals(x.name, column, StringComparison.OrdinalIgnoreCase)))
                await _database.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DB MIGRATION ERROR [{table}.{column}]: {ex}");
        }
    }

    private class TableInfoRow
    {
        public int cid { get; set; }
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public int notnull { get; set; }
        public string? dflt_value { get; set; }
        public int pk { get; set; }
    }

    public static SQLiteAsyncConnection Db =>
        _database ?? throw new InvalidOperationException("Database is not initialized.");
}
