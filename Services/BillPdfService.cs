using System.Text;
using System.Text.RegularExpressions;
using CwSoftware.Papiro;
using HisabKitab.Models;
using Microsoft.Maui.ApplicationModel;

namespace HisabKitab.Services;

/// <summary>
/// PDF generation facade. HTML is produced by BillTemplateService and Papiro
/// performs the platform-native HTML -> PDF conversion.
/// </summary>
public static class BillPdfService
{
    private static IHtmlToPdfService? _papiro;

    public sealed record PdfResult(string FilePath, int PageCount);

    public static void Initialize(IHtmlToPdfService pdfService)
    {
        _papiro = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
    }

    public static async Task<PdfResult> CreateAsync(
        BillDocument bill,
        List<BillItem> items,
        BillTemplate template,
        Company company)
    {
        var pdfService = _papiro
            ?? throw new InvalidOperationException("Papiro PDF service is not initialized. Call BillPdfService.Initialize(...) from MauiProgram.");

        var html = await BillTemplateService.CreateHtmlAsync(bill, items, template, company);
        var fileName = $"{Sanitize(bill.DocumentNo)}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
        var result = await pdfService.ConvertAndSaveAsync(html, fileName);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.FilePath))
            throw new InvalidOperationException(result.ErrorMessage ?? "Papiro could not generate the PDF.");

        var pageCount = await CountPdfPagesAsync(result.FilePath);
        return new PdfResult(result.FilePath, Math.Max(1, pageCount));
    }

    public static async Task PrintAsync(string pdfPath, string jobName, int pageCount)
    {
#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var activity = Platform.CurrentActivity
                ?? throw new InvalidOperationException("Print activity is not available.");

            var printManager = activity.GetSystemService(Android.Content.Context.PrintService)
                as Android.Print.PrintManager;

            if (printManager == null)
                throw new InvalidOperationException("Android print service is not available.");

            printManager.Print(
                jobName,
                new PdfPrintAdapter(pdfPath, Math.Max(1, pageCount)),
                null);
        });
#else
        throw new PlatformNotSupportedException("Printing is currently supported on Android.");
#endif
    }

    private static async Task<int> CountPdfPagesAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return 1;

            // PDF page dictionaries contain `/Type /Page`, while the page tree
            // itself contains `/Type /Pages`. The word boundary prevents the
            // latter from being counted as a page.
            var latin1 = Encoding.Latin1.GetString(bytes);
            var count = Regex.Matches(latin1, @"/Type\s*/Page\b").Count;
            return count > 0 ? count : 1;
        }
        catch
        {
            // Page count is only used by the Android print adapter; a safe
            // fallback keeps sharing/printing usable even if counting fails.
            return 1;
        }
    }

    private static string Sanitize(string? value)
    {
        var result = value ?? "invoice";
        foreach (var ch in Path.GetInvalidFileNameChars())
            result = result.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(result) ? "invoice" : result.Trim();
    }

#if ANDROID
    private sealed class PdfPrintAdapter : Android.Print.PrintDocumentAdapter
    {
        private readonly string _path;
        private readonly int _pages;

        public PdfPrintAdapter(string path, int pages)
        {
            _path = path;
            _pages = Math.Max(1, pages);
        }

        public override void OnLayout(
            Android.Print.PrintAttributes? oldAttributes,
            Android.Print.PrintAttributes? newAttributes,
            Android.OS.CancellationSignal? cancellationSignal,
            Android.Print.PrintDocumentAdapter.LayoutResultCallback? callback,
            Android.OS.Bundle? extras)
        {
            if (cancellationSignal?.IsCanceled == true)
            {
                callback?.OnLayoutCancelled();
                return;
            }

            var info = new Android.Print.PrintDocumentInfo.Builder(Path.GetFileName(_path))
                .SetContentType(Android.Print.PrintContentType.Document)
                .SetPageCount(_pages)
                .Build();

            callback?.OnLayoutFinished(info, true);
        }

        public override async void OnWrite(
            Android.Print.PageRange[]? pages,
            Android.OS.ParcelFileDescriptor? destination,
            Android.OS.CancellationSignal? cancellationSignal,
            Android.Print.PrintDocumentAdapter.WriteResultCallback? callback)
        {
            try
            {
                if (cancellationSignal?.IsCanceled == true)
                {
                    callback?.OnWriteCancelled();
                    return;
                }

                if (destination == null)
                    throw new InvalidOperationException("Printer destination is unavailable.");

                var bytes = await File.ReadAllBytesAsync(_path);
                using var output = new Android.OS.ParcelFileDescriptor.AutoCloseOutputStream(destination);
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
                callback?.OnWriteFinished(new[] { new Android.Print.PageRange(0, _pages - 1) });
            }
            catch (Exception ex)
            {
                callback?.OnWriteFailed(ex.Message);
            }
        }
    }
#endif
}
