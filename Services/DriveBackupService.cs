using System.Net.Http.Headers;

namespace HisabKitab.Services;

public static class DriveBackupService
{
    private static readonly HttpClient _http = new();

    public static async Task<bool> UploadPackageAsync(byte[] data, string fileName, string accessToken)
    {
        if (data.Length == 0 || string.IsNullOrWhiteSpace(accessToken)) return false;

        var metadata = $"{{\"name\":\"{fileName}\",\"mimeType\":\"application/gzip\"}}";

        using var form = new MultipartContent("related", Guid.NewGuid().ToString("N"));
        var meta = new StringContent(metadata, System.Text.Encoding.UTF8, "application/json; charset=UTF-8");
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        form.Add(meta);
        form.Add(content);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = form;

        using var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
}
