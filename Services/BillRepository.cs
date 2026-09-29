using HisabKitab.Models;

namespace HisabKitab.Services;

public static class BillRepository
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

            await DatabaseService.Db.CreateTableAsync<BillDocument>();
            await DatabaseService.Db.CreateTableAsync<BillItem>();
            await DatabaseService.Db.CreateTableAsync<BillPayment>();
            await DatabaseService.Db.CreateTableAsync<BillTemplate>();
            await EnsureColumnAsync("BillDocument", "PartyGSTIN", "TEXT");
            await EnsureColumnAsync("BillDocument", "PartyPAN", "TEXT");
            await EnsureColumnAsync("BillDocument", "ContactPerson", "TEXT");
            await EnsureColumnAsync("BillItem", "ProductBarcode", "TEXT");
            await EnsureColumnAsync("BillItem", "ProductHSN", "TEXT");
            await EnsureColumnAsync("BillItem", "ProductImagePath", "TEXT");

            await DatabaseService.Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_Bill_CompanyTypeDate ON BillDocument(CompanyId, DocumentType, DocumentDate DESC)");
            await DatabaseService.Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_BillItem_Bill ON BillItem(BillId)");
            await DatabaseService.Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_BillPayment_Bill ON BillPayment(BillId)");
            await DatabaseService.Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_BillTemplate_CompanyType ON BillTemplate(CompanyId, DocumentType)");

            _schemaReady = true;
        }
        finally { SchemaLock.Release(); }
    }

    public static async Task<List<BillDocument>> GetAllAsync(int companyId, string documentType)
    {
        await EnsureSchemaAsync();
        return await DatabaseService.Db.QueryAsync<BillDocument>(
            "SELECT * FROM BillDocument WHERE CompanyId = ? AND DocumentType = ? AND Status <> 'Deleted' ORDER BY DocumentDate DESC, Id DESC",
            companyId, documentType);
    }


    public static async Task<List<BillDocument>> SearchAsync(int companyId, string documentType, string search)
    {
        await EnsureSchemaAsync();
        if (string.IsNullOrWhiteSpace(search)) return await GetAllAsync(companyId, documentType);
        var term = $"%{search.Trim()}%";
        const string sql = @"SELECT * FROM BillDocument b
WHERE b.CompanyId = ? AND b.DocumentType = ? AND b.Status <> 'Deleted'
AND (b.DocumentNo LIKE ? COLLATE NOCASE OR b.PartyName LIKE ? COLLATE NOCASE OR b.PartyMobile LIKE ? COLLATE NOCASE OR b.PartyEmail LIKE ? COLLATE NOCASE OR b.PartyGSTIN LIKE ? COLLATE NOCASE OR b.PartyPAN LIKE ? COLLATE NOCASE OR b.ContactPerson LIKE ? COLLATE NOCASE OR b.BillingAddress LIKE ? COLLATE NOCASE OR b.ShippingAddress LIKE ? COLLATE NOCASE OR b.Notes LIKE ? COLLATE NOCASE OR EXISTS (SELECT 1 FROM BillItem bi WHERE bi.BillId = b.Id AND (bi.ProductName LIKE ? COLLATE NOCASE OR bi.ProductCode LIKE ? COLLATE NOCASE OR bi.ProductBarcode LIKE ? COLLATE NOCASE OR bi.ProductHSN LIKE ? COLLATE NOCASE)))
ORDER BY b.DocumentDate DESC, b.Id DESC";
        return await DatabaseService.Db.QueryAsync<BillDocument>(sql, companyId, documentType, term, term, term, term, term, term, term, term, term, term, term, term, term, term);
    }

    public static async Task<(bool Allowed, string Reason)> DeleteAsync(int billId)
    {
        await EnsureSchemaAsync();
        var bill = await GetByIdAsync(billId);
        if (bill == null) return (false, "Bill not found.");
        var payments = await GetPaymentsAsync(billId);
        if (payments.Count > 0) return (false, "This bill has payment entries. Reverse/remove the payments before deleting the bill.");

        if (bill.DocumentType is "Sales" or "Purchase" && await TableExistsAsync("StockTransaction") && await ColumnExistsAsync("StockTransaction", "Notes"))
        {
            var linked = await DatabaseService.Db.QueryScalarsAsync<int>("SELECT COUNT(*) FROM StockTransaction WHERE Notes = ? OR Notes = ?", bill.DocumentNo, $"Correction {bill.DocumentNo}");
            if (linked.FirstOrDefault() > 0) return (false, "This bill has linked stock movements and cannot be deleted.");
        }

        bill.Status = "Deleted";
        bill.UpdatedAtUtc = DateTime.UtcNow;
        await DatabaseService.Db.UpdateAsync(bill);
        return (true, string.Empty);
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

    private sealed class TableInfoRow { public int cid { get; set; } public string name { get; set; } = string.Empty; public string type { get; set; } = string.Empty; }

    private static async Task EnsureColumnAsync(string table, string column, string type)
    {
        var rows = await DatabaseService.Db.QueryAsync<TableInfoRow>($"PRAGMA table_info([{table.Replace("]", "]]", StringComparison.Ordinal)}])");
        if (!rows.Any(x => string.Equals(x.name, column, StringComparison.OrdinalIgnoreCase))) await DatabaseService.Db.ExecuteAsync($"ALTER TABLE [{table.Replace("]", "]]", StringComparison.Ordinal)}] ADD COLUMN [{column}] {type}");
    }

    public static async Task<BillDocument?> GetByIdAsync(int id)
    {
        await EnsureSchemaAsync();
        return (await DatabaseService.Db.QueryAsync<BillDocument>("SELECT * FROM BillDocument WHERE Id = ? LIMIT 1", id)).FirstOrDefault();
    }

    public static async Task<List<BillItem>> GetItemsAsync(int billId)
    {
        await EnsureSchemaAsync();
        return await DatabaseService.Db.QueryAsync<BillItem>("SELECT * FROM BillItem WHERE BillId = ? ORDER BY Id", billId);
    }

    public static async Task<List<BillPayment>> GetPaymentsAsync(int billId)
    {
        await EnsureSchemaAsync();
        return await DatabaseService.Db.QueryAsync<BillPayment>("SELECT * FROM BillPayment WHERE BillId = ? ORDER BY PaymentDate DESC, Id DESC", billId);
    }

    public static async Task<int> SaveAsync(BillDocument document, IEnumerable<BillItem> items, decimal paymentAmount = 0, string? paymentMode = null, string? paymentReference = null)
    {
        await EnsureSchemaAsync();
        if (document.CompanyId <= 0) throw new InvalidOperationException("No active company selected.");
        if (string.IsNullOrWhiteSpace(document.DocumentType)) throw new InvalidOperationException("Invalid document type.");
        if (document.PartyId <= 0) throw new InvalidOperationException("Please select or enter a party.");

        var itemList = items.Where(x => x.Quantity > 0).ToList();
        if (itemList.Count == 0) throw new InvalidOperationException("Please add at least one product.");

        document.UpdatedAtUtc = DateTime.UtcNow;
        document.DocumentType = NormalizeDocumentType(document.DocumentType);
        document.PaidAmount = Math.Min(Math.Max(0, document.PaidAmount), Math.Max(0, document.GrandTotal));
        document.PaymentStatus = GetPaymentStatus(document.PaidAmount, document.GrandTotal);
        document.PartyType = document.DocumentType == "Purchase" ? "Supplier" : "Customer";

        if (document.Id == 0)
        {
            document.CreatedAtUtc = DateTime.UtcNow;
            document.DocumentNo = string.Empty;
            await DatabaseService.Db.InsertAsync(document);
            document.DocumentNo = await GenerateDocumentNoAsync(document.CompanyId, document.DocumentType, document.Id);
            await DatabaseService.Db.UpdateAsync(document);

            foreach (var item in itemList)
            {
                item.Id = 0;
                item.BillId = document.Id;
                await DatabaseService.Db.InsertAsync(item);
            }
        }
        else
        {
            var existingItems = await GetItemsAsync(document.Id);
            foreach (var oldItem in existingItems) await DatabaseService.Db.DeleteAsync(oldItem);
            await DatabaseService.Db.UpdateAsync(document);
            foreach (var item in itemList)
            {
                item.Id = 0;
                item.BillId = document.Id;
                await DatabaseService.Db.InsertAsync(item);
            }
        }

        if (paymentAmount > 0)
        {
            if (paymentAmount > document.GrandTotal) paymentAmount = document.GrandTotal;
            await AddPaymentAsync(document, paymentAmount, paymentMode ?? "Cash", paymentReference, document.DocumentDate, null);
        }

        return document.Id;
    }

    public static async Task<int> AddPaymentAsync(BillDocument bill, decimal amount, string mode, string? reference, DateTime paymentDate, string? notes)
    {
        await EnsureSchemaAsync();
        if (amount <= 0) throw new InvalidOperationException("Payment amount must be greater than zero.");

        var remaining = Math.Max(0, bill.GrandTotal - bill.PaidAmount);
        if (remaining <= 0) throw new InvalidOperationException("This bill is already fully paid.");
        if (amount > remaining) amount = remaining;

        var payment = new BillPayment
        {
            CompanyId = bill.CompanyId,
            BillId = bill.Id,
            PartyId = bill.PartyId,
            Amount = Math.Round(amount, 2),
            PaymentMode = string.IsNullOrWhiteSpace(mode) ? "Cash" : mode.Trim(),
            ReferenceNo = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            PaymentDate = paymentDate.Date,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        await DatabaseService.Db.InsertAsync(payment);

        bill.PaidAmount = Math.Round(bill.PaidAmount + payment.Amount, 2);
        bill.PaymentStatus = GetPaymentStatus(bill.PaidAmount, bill.GrandTotal);
        bill.UpdatedAtUtc = DateTime.UtcNow;
        await DatabaseService.Db.UpdateAsync(bill);

        await PartyActivityRepository.AddAsync(new PartyActivity
        {
            CompanyId = bill.CompanyId,
            PartyId = bill.PartyId,
            ActivityType = "PAYMENT",
            RefType = "BillPayment",
            RefId = payment.Id,
            Title = bill.DocumentType == "Purchase" ? $"Payment made for {bill.DocumentNo}" : $"Payment received for {bill.DocumentNo}",
            Description = $"{payment.PaymentMode} payment received.",
            Amount = payment.Amount,
            ActivityDateUtc = payment.PaymentDate.ToUniversalTime()
        });

        return payment.Id;
    }

    public static async Task<List<BillTemplate>> GetTemplatesAsync(int companyId, string documentType)
    {
        await EnsureSchemaAsync();
        await SeedTemplatesAsync(companyId, documentType);

        await EnsureInvoiceTemplateCatalogAsync(companyId, documentType);

        return await DatabaseService.Db.QueryAsync<BillTemplate>(
            "SELECT * FROM BillTemplate WHERE CompanyId = ? AND DocumentType = ? AND IsActive = 1 ORDER BY Id",
            companyId, documentType);
    }

    public static async Task<BillTemplate> GetDefaultTemplateAsync(int companyId, string documentType)
    {
        await EnsureSchemaAsync();
        await SeedTemplatesAsync(companyId, documentType);
        await EnsureInvoiceTemplateCatalogAsync(companyId, documentType);
        var template = (await DatabaseService.Db.QueryAsync<BillTemplate>(
            "SELECT * FROM BillTemplate WHERE CompanyId = ? AND DocumentType = ? AND IsActive = 1 ORDER BY IsDefault DESC, UpdatedAtUtc DESC, Id DESC LIMIT 1",
            companyId, documentType)).FirstOrDefault();
        return template ?? new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Classic Professional", LayoutKey = "ClassicProfessional", IsDefault = true };
    }

    public static async Task SetDefaultTemplateAsync(int companyId, string documentType, int templateId)
    {
        await EnsureSchemaAsync();
        await DatabaseService.Db.ExecuteAsync("UPDATE BillTemplate SET IsDefault = 0, UpdatedAtUtc = ? WHERE CompanyId = ? AND DocumentType = ?", DateTime.UtcNow, companyId, documentType);
        await DatabaseService.Db.ExecuteAsync("UPDATE BillTemplate SET IsDefault = 1, UpdatedAtUtc = ? WHERE Id = ? AND CompanyId = ?", DateTime.UtcNow, templateId, companyId);
    }

    public static async Task<Party?> ResolvePartyAsync(int companyId, string partyType, string? nameOrMobile)
    {
        await PartyRepository.EnsureSchemaAsync();
        if (string.IsNullOrWhiteSpace(nameOrMobile)) return null;
        var search = nameOrMobile.Trim();
        var rows = await PartyRepository.SearchAsync(companyId, search, partyType);
        return rows.FirstOrDefault();
    }

    public static async Task<Party> ResolveOrCreatePartyAsync(int companyId, string partyType, string name, string? mobile, string? email)
    {
        await PartyRepository.EnsureSchemaAsync();
        partyType = partyType.Equals("Supplier", StringComparison.OrdinalIgnoreCase) ? "Supplier" : "Customer";
        name = string.IsNullOrWhiteSpace(name) ? mobile?.Trim() ?? string.Empty : name.Trim();
        mobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Please enter party name.");

        Party? existing = null;
        var all = await PartyRepository.GetAllAsync(companyId, partyType);
        var mobileKey = NormalizeMobile(mobile);

        if (!string.IsNullOrWhiteSpace(mobileKey))
            existing = all.FirstOrDefault(x => NormalizeMobile(x.Mobile) == mobileKey || NormalizeMobile(x.Mobile2) == mobileKey);

        existing ??= all.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (string.IsNullOrWhiteSpace(existing.Mobile) && !string.IsNullOrWhiteSpace(mobile)) existing.Mobile = mobile;
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(email)) existing.Email = email;
            await PartyRepository.SaveAsync(existing);
            return existing;
        }

        var party = new Party
        {
            CompanyId = companyId,
            PartyType = partyType,
            Name = name,
            Mobile = mobile,
            Email = email,
            IsActive = true
        };
        await PartyRepository.SaveAsync(party);
        return party;
    }

    public static async Task<decimal> GetBillBalanceAsync(BillDocument bill)
    {
        await EnsureSchemaAsync();
        return Math.Max(0, Math.Round(bill.GrandTotal - bill.PaidAmount, 2));
    }

    public static async Task<decimal> GetPartyBalanceAsync(int companyId, int partyId)
    {
        await EnsureSchemaAsync();
        var partyRows = await DatabaseService.Db.QueryAsync<Party>(
            "SELECT * FROM Party WHERE CompanyId = ? AND Id = ? LIMIT 1", companyId, partyId);
        var party = partyRows.FirstOrDefault();
        if (party == null) return 0;

        var bills = await DatabaseService.Db.QueryAsync<BillDocument>(
            "SELECT * FROM BillDocument WHERE CompanyId = ? AND PartyId = ? AND Status <> 'Deleted' AND DocumentType IN ('Sales','Purchase')",
            companyId, partyId);

        // Opening balance is treated as an existing outstanding balance.
        // Sales and purchase invoices are both surfaced as positive outstanding amounts
        // because the UI is showing the amount still due on the party account.
        var outstanding = bills.Sum(x => Math.Max(0, x.GrandTotal - x.PaidAmount));
        return Math.Round(party.OpeningBalance + outstanding, 2);
    }

    public static async Task<Dictionary<int, decimal>> GetPartyBalancesAsync(int companyId, IEnumerable<int> partyIds)
    {
        await EnsureSchemaAsync();
        var ids = partyIds.Distinct().Where(x => x > 0).ToList();
        var result = ids.ToDictionary(x => x, _ => 0m);
        if (ids.Count == 0) return result;

        var bills = await DatabaseService.Db.QueryAsync<BillDocument>(
            "SELECT * FROM BillDocument WHERE CompanyId = ? AND Status <> 'Deleted' AND DocumentType IN ('Sales','Purchase')",
            companyId);

        foreach (var id in ids)
        {
            var partyRows = await DatabaseService.Db.QueryAsync<Party>(
                "SELECT OpeningBalance FROM Party WHERE CompanyId = ? AND Id = ? LIMIT 1", companyId, id);
            var opening = partyRows.FirstOrDefault()?.OpeningBalance ?? 0m;
            var outstanding = bills.Where(x => x.PartyId == id).Sum(x => Math.Max(0, x.GrandTotal - x.PaidAmount));
            result[id] = Math.Round(opening + outstanding, 2);
        }
        return result;
    }

    public static string GetPaymentStatus(decimal paid, decimal total)
    {
        if (paid <= 0) return "Unpaid";
        if (paid >= total) return "Paid";
        return "Partial";
    }

    private static async Task EnsureInvoiceTemplateCatalogAsync(int companyId, string documentType)
    {
        // First migrate legacy layout keys so an old default row becomes one of the new templates.
        await DatabaseService.Db.ExecuteAsync(
            "UPDATE BillTemplate SET LayoutKey = 'ClassicProfessional', Name = 'Classic Professional', UpdatedAtUtc = ? WHERE CompanyId = ? AND DocumentType = ? AND LayoutKey = 'Modern'",
            DateTime.UtcNow, companyId, documentType);
        await DatabaseService.Db.ExecuteAsync(
            "UPDATE BillTemplate SET LayoutKey = 'ClassicProfessional', Name = 'Classic Professional', UpdatedAtUtc = ? WHERE CompanyId = ? AND DocumentType = ? AND LayoutKey = 'Classic'",
            DateTime.UtcNow, companyId, documentType);
        await DatabaseService.Db.ExecuteAsync(
            "UPDATE BillTemplate SET LayoutKey = 'BluePanel', Name = 'Classic Blue Panel', UpdatedAtUtc = ? WHERE CompanyId = ? AND DocumentType = ? AND LayoutKey = 'Compact'",
            DateTime.UtcNow, companyId, documentType);

        var expected = new[]
        {
            ("Classic Professional", "ClassicProfessional"),
            ("Purple Modern", "PurpleModern"),
            ("Teal Professional", "TealProfessional"),
            ("Curved Teal", "CurveTeal"),
            ("Curved Navy", "CurveNavy"),
            ("Classic Blue Panel", "BluePanel")
        };

        foreach (var (name, layoutKey) in expected)
        {
            var exists = await DatabaseService.Db.QueryScalarsAsync<int>(
                "SELECT COUNT(*) FROM BillTemplate WHERE CompanyId = ? AND DocumentType = ? AND LayoutKey = ?",
                companyId, documentType, layoutKey);
            if (exists.FirstOrDefault() > 0) continue;

            await DatabaseService.Db.InsertAsync(new BillTemplate
            {
                CompanyId = companyId,
                DocumentType = documentType,
                Name = name,
                LayoutKey = layoutKey,
                IsDefault = layoutKey == "ClassicProfessional",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        // Keep exactly one default if old data had none or multiple defaults.
        var defaults = await DatabaseService.Db.QueryAsync<BillTemplate>(
            "SELECT * FROM BillTemplate WHERE CompanyId = ? AND DocumentType = ? AND IsActive = 1 AND IsDefault = 1 ORDER BY Id",
            companyId, documentType);
        if (defaults.Count == 0)
            await DatabaseService.Db.ExecuteAsync("UPDATE BillTemplate SET IsDefault = CASE WHEN LayoutKey = 'ClassicProfessional' THEN 1 ELSE 0 END WHERE CompanyId = ? AND DocumentType = ?", companyId, documentType);
        else if (defaults.Count > 1)
        {
            var keep = defaults.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id).First().Id;
            await DatabaseService.Db.ExecuteAsync("UPDATE BillTemplate SET IsDefault = 0 WHERE CompanyId = ? AND DocumentType = ?", companyId, documentType);
            await DatabaseService.Db.ExecuteAsync("UPDATE BillTemplate SET IsDefault = 1 WHERE Id = ?", keep);
        }
    }

    private static async Task SeedTemplatesAsync(int companyId, string documentType)
    {
        var count = (await DatabaseService.Db.QueryScalarsAsync<int>("SELECT COUNT(*) FROM BillTemplate WHERE CompanyId = ? AND DocumentType = ? AND IsActive = 1", companyId, documentType)).FirstOrDefault();
        if (count > 0) return;

        var templates = new[]
        {
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Classic Professional", LayoutKey = "ClassicProfessional", IsDefault = true },
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Purple Modern", LayoutKey = "PurpleModern" },
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Teal Professional", LayoutKey = "TealProfessional" },
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Curved Teal", LayoutKey = "CurveTeal" },
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Curved Navy", LayoutKey = "CurveNavy" },
            new BillTemplate { CompanyId = companyId, DocumentType = documentType, Name = "Classic Blue Panel", LayoutKey = "BluePanel" }
        };

        foreach (var template in templates)
        {
            template.CreatedAtUtc = DateTime.UtcNow;
            template.UpdatedAtUtc = DateTime.UtcNow;
            await DatabaseService.Db.InsertAsync(template);
        }
    }

    private static async Task<string> GenerateDocumentNoAsync(int companyId, string documentType, int id)
    {
        var prefix = documentType switch
        {
            "Quotation" => "QT",
            "Purchase" => "PUR",
            _ => "INV"
        };

        return $"{prefix}{id:D6}";
    }

    private static string NormalizeDocumentType(string? type)
    {
        if (string.Equals(type, "Quotation", StringComparison.OrdinalIgnoreCase)) return "Quotation";
        if (string.Equals(type, "Purchase", StringComparison.OrdinalIgnoreCase)) return "Purchase";
        return "Sales";
    }

    private static string NormalizeMobile(string? value) =>
        new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
