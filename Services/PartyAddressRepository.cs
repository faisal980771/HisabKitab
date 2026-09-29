using HisabKitab.Models;

namespace HisabKitab.Services;

public static class PartyAddressRepository
{
    public static async Task<List<PartyAddress>> GetAllAsync(int partyId, string? type = null)
    {
        await PartyRepository.EnsureSchemaAsync();
        if (string.IsNullOrWhiteSpace(type))
            return await DatabaseService.Db.Table<PartyAddress>().Where(x => x.PartyId == partyId && x.IsActive).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label).ToListAsync();
        return await DatabaseService.Db.Table<PartyAddress>().Where(x => x.PartyId == partyId && x.AddressType == type && x.IsActive).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label).ToListAsync();
    }

    public static async Task ReplaceAsync(int companyId, int partyId, IEnumerable<PartyAddress> addresses)
    {
        await PartyRepository.EnsureSchemaAsync();
        var existing = await DatabaseService.Db.Table<PartyAddress>().Where(x => x.PartyId == partyId).ToListAsync();
        foreach (var address in existing)
        {
            address.IsActive = false;
            address.UpdatedAtUtc = DateTime.UtcNow;
            await DatabaseService.Db.UpdateAsync(address);
        }

        foreach (var source in addresses.Where(x => !string.IsNullOrWhiteSpace(x.AddressLine1) || !string.IsNullOrWhiteSpace(x.City) || !string.IsNullOrWhiteSpace(x.State) || !string.IsNullOrWhiteSpace(x.Pincode)))
        {
            source.Id = 0;
            source.CompanyId = companyId;
            source.PartyId = partyId;
            source.AddressType = string.Equals(source.AddressType, "Shipping", StringComparison.OrdinalIgnoreCase) ? "Shipping" : "Billing";
            source.Country = string.IsNullOrWhiteSpace(source.Country) ? "India" : source.Country.Trim();
            source.IsActive = true;
            source.CreatedAtUtc = DateTime.UtcNow;
            source.UpdatedAtUtc = DateTime.UtcNow;
            await DatabaseService.Db.InsertAsync(source);
        }
    }

    public static async Task AddAsync(PartyAddress address)
    {
        await PartyRepository.EnsureSchemaAsync();
        address.CreatedAtUtc = DateTime.UtcNow;
        address.UpdatedAtUtc = DateTime.UtcNow;
        await DatabaseService.Db.InsertAsync(address);
    }
}
