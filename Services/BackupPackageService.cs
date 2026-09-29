using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace HisabKitab.Services;

public static class BackupPackageService
{
    public static async Task<byte[]> BuildPendingPackageAsync(int companyId)
    {
        var changes = await ChangeLogService.GetPendingAsync(companyId);
        if (changes.Count == 0) return Array.Empty<byte>();

        var json = JsonSerializer.Serialize(new
        {
            app = "HisabKitab",
            version = 1,
            companyId,
            generatedAtUtc = DateTime.UtcNow,
            changes
        });

        var raw = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
            await gzip.WriteAsync(raw);

        return output.ToArray();
    }
}
