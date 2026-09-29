using System.Globalization;
using System.Net;
using System.Text;
using HisabKitab.Models;

namespace HisabKitab.Services;

/// <summary>
/// Creates print-safe A4 HTML for Papiro.
/// The HTML deliberately uses a five-column item table (matching the supplied
/// invoice references) so long product names do not squeeze the amount column off the page.
/// </summary>
public static class BillTemplateService
{
    private static readonly CultureInfo IndiaCulture = new("en-IN");

    public static async Task<string> CreateHtmlAsync(
        BillDocument bill,
        List<BillItem> items,
        BillTemplate template,
        Company company)
    {
        var key = NormalizeLayoutKey(template.LayoutKey);
        var logo = await ImageDataUriAsync(company.LogoPath);
        var itemImagesEnabled = key is "TealProfessional" or "CurveTeal" or "PurpleModern";

        var rows = new StringBuilder();
        var index = 1;
        foreach (var item in items)
        {
            var image = itemImagesEnabled
                ? await ImageDataUriAsync(item.ProductImagePath)
                : null;

            var productCell = itemImagesEnabled
                ? $@"<div class='product-with-image'>
                        {ImageTag(image, item.ProductName)}
                        <div class='product-copy'>
                            <div class='product-name'>{E(item.ProductName)}</div>
                            {ProductMeta(item)}
                        </div>
                    </div>"
                : $@"<div class='product-copy'>
                        <div class='product-name'>{E(item.ProductName)}</div>
                        {ProductMeta(item)}
                    </div>";

            var discountMeta = item.DiscountAmount > 0
                ? $"<div class='product-meta'>Discount: {Money(item.DiscountAmount)}{(item.DiscountPercent > 0 ? $" ({item.DiscountPercent:0.##}%)" : string.Empty)}</div>"
                : string.Empty;

            rows.Append($@"
<tr class='item-row'>
  <td class='sr num'>{index++}</td>
  <td>{productCell}{discountMeta}</td>
  <td class='num qty'>{E(InvoiceFormatService.Quantity(item.Quantity))}<span class='unit'> {E(item.Unit)}</span></td>
  <td class='money'>{Money(item.Rate)}</td>
  <td class='money strong'>{Money(item.NetAmount)}</td>
</tr>");
        }

        if (items.Count == 0)
            rows.Append("<tr><td colspan='5' class='empty'>No items</td></tr>");

        var balance = Math.Max(0m, Math.Round(bill.GrandTotal - bill.PaidAmount, 2));
        var title = bill.DocumentType switch
        {
            "Purchase" => "PURCHASE BILL",
            "Quotation" => "QUOTATION",
            _ => "INVOICE"
        };
        var paidLabel = bill.DocumentType == "Purchase" ? "Paid" : "Received";
        var totalLabel = bill.DocumentType == "Purchase" ? "TOTAL PAYABLE" : "GRAND TOTAL";

        return $@"<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<style>
{BaseCss()}
{TemplateCss(key)}
</style>
</head>
<body>
<div class='invoice-sheet {CssKey(key)}'>
  {Header(key, company, bill, title, logo)}

  <section class='party-block keep-together'>
    <div class='party-col'>
      <div class='section-label'>BILL TO</div>
      <div class='party-name'>{E(bill.PartyName)}</div>
      {LineIf(bill.ContactPerson, "Contact")}
      {LineIf(bill.PartyMobile, "Mobile")}
      {LineIf(bill.PartyEmail, "Email")}
      {LineIf(bill.PartyGSTIN, "GSTIN")}
      {LineIf(bill.PartyPAN, "PAN")}
      <div class='address'><strong>Billing Address</strong><br>{Multiline(bill.BillingAddress)}</div>
    </div>
    <div class='party-col ship-col'>
      <div class='section-label'>SHIP TO</div>
      <div class='party-name'>{E(bill.PartyName)}</div>
      <div class='address'><strong>Shipping Address</strong><br>{Multiline(string.IsNullOrWhiteSpace(bill.ShippingAddress) ? bill.BillingAddress : bill.ShippingAddress)}</div>
      <div class='invoice-meta'>
        <div><span>Invoice No.</span><strong>{E(bill.DocumentNo)}</strong></div>
        <div><span>Date</span><strong>{bill.DocumentDate:dd/MM/yyyy}</strong></div>
        {(bill.DueDate.HasValue ? $"<div><span>Due Date</span><strong>{bill.DueDate:dd/MM/yyyy}</strong></div>" : string.Empty)}
        <div><span>Status</span><strong>{E(bill.DocumentType == "Quotation" ? "Quotation" : bill.PaymentStatus)}</strong></div>
      </div>
    </div>
  </section>

  <section class='items-section'>
    <table class='items'>
      <colgroup>
        <col class='col-sr'>
        <col class='col-product'>
        <col class='col-qty'>
        <col class='col-rate'>
        <col class='col-total'>
      </colgroup>
      <thead>
        <tr>
          <th class='sr num'>#</th>
          <th>ITEM DESCRIPTION</th>
          <th class='num'>QTY</th>
          <th class='money'>RATE</th>
          <th class='money'>TOTAL</th>
        </tr>
      </thead>
      <tbody>{rows}</tbody>
    </table>
  </section>

  <section class='summary-area'>
    <div class='summary-left'>
      <div class='words-box keep-together'>
        <div class='section-label'>AMOUNT IN WORDS</div>
        <div class='words'>{E(InvoiceFormatService.AmountInWords(bill.GrandTotal))}</div>
      </div>
      <div class='notes-box keep-together'>
        <div class='section-label'>NOTES / TERMS &amp; CONDITIONS</div>
        <div class='notes'>{Multiline(bill.Notes)}</div>
      </div>
      <div class='thank-you'>Thank you for your business!</div>
    </div>

    <div class='totals keep-together'>
      <div><span>Subtotal</span><strong>{Money(bill.SubTotal)}</strong></div>
      <div><span>Discount</span><strong>{Money(bill.DiscountTotal)}</strong></div>
      <div><span>Tax</span><strong>{Money(bill.TaxTotal)}</strong></div>
      <div><span>{E(paidLabel)}</span><strong>{Money(bill.PaidAmount)}</strong></div>
      <div><span>Balance</span><strong>{Money(balance)}</strong></div>
      <div class='grand'><span>{E(totalLabel)}</span><strong>{Money(bill.GrandTotal)}</strong></div>
    </div>
  </section>

  <section class='signature-row keep-together'>
    <div></div>
    <div class='signature'>
      <div>Authorized Signatory</div>
      <div class='sign-line'></div>
      <strong>{E(company.CompanyName)}</strong>
    </div>
  </section>

  {Footer(company)}
</div>
</body>
</html>";
    }

    private static string NormalizeLayoutKey(string? value) => value switch
    {
        "PurpleModern" => "PurpleModern",
        "TealProfessional" => "TealProfessional",
        "CurveTeal" => "CurveTeal",
        "CurveNavy" => "CurveNavy",
        "BluePanel" => "BluePanel",
        "ClassicProfessional" => "ClassicProfessional",
        "Classic" or "Modern" or "Compact" => "ClassicProfessional",
        _ => "ClassicProfessional"
    };

    private static string Header(string key, Company company, BillDocument bill, string title, string? logo)
    {
        var companyDetails = $@"
<div class='company-details'>
  {ImageTag(logo, company.CompanyName, "company-logo")}
  <div class='company-name'>{E(company.CompanyName)}</div>
  <div>{E(CompanyAddress(company))}</div>
  <div>{E(JoinNonEmpty(company.Mobile, company.Email))}</div>
  {LineIf(company.GSTIN, "GSTIN")}
</div>";

        return key switch
        {
            "PurpleModern" => $@"<header class='header purple-header'>{companyDetails}<div class='header-right'><div class='invoice-title'>{E(title)}</div><div>No: <strong>{E(bill.DocumentNo)}</strong></div><div>Date: {bill.DocumentDate:dd/MM/yyyy}</div></div></header>",
            "TealProfessional" => $@"<header class='header teal-header'>{companyDetails}<div class='header-right'><div class='invoice-title'>{E(title)}</div><div>No: <strong>{E(bill.DocumentNo)}</strong></div><div>Date: {bill.DocumentDate:dd/MM/yyyy}</div></div></header>",
            "CurveTeal" => $@"<header class='curve-header curve-teal'>{companyDetails}<div class='curve-title'>{E(title)}</div><div class='curve-meta'><strong>{E(bill.DocumentNo)}</strong><span>{bill.DocumentDate:dd/MM/yyyy}</span></div></header>",
            "CurveNavy" => $@"<header class='curve-header curve-navy'>{companyDetails}<div class='curve-title'>{E(title)}</div><div class='curve-meta'><strong>{E(bill.DocumentNo)}</strong><span>{bill.DocumentDate:dd/MM/yyyy}</span></div></header>",
            "BluePanel" => $@"<header class='blue-panel-header'><div class='blue-panel-company'>{companyDetails}</div><div class='blue-panel-title'><div class='invoice-title'>{E(title)}</div><div>No: <strong>{E(bill.DocumentNo)}</strong></div><div>Date: {bill.DocumentDate:dd/MM/yyyy}</div></div></header>",
            _ => $@"<header class='header classic-header'>{companyDetails}<div class='header-right'><div class='invoice-title'>{E(title)}</div><div>No: <strong>{E(bill.DocumentNo)}</strong></div><div>Date: {bill.DocumentDate:dd/MM/yyyy}</div>{(bill.DueDate.HasValue ? $"<div>Due: {bill.DueDate:dd/MM/yyyy}</div>" : string.Empty)}</div></header>"
        };
    }

    private static string Footer(Company company)
    {
        var contact = JoinNonEmpty(company.Mobile, company.Email, CompanyAddress(company));
        return $"<footer class='footer'><span>{E(contact)}</span><span>HisabKitab</span></footer>";
    }

    private static string TemplateCss(string key)
    {
        return key switch
        {
            "PurpleModern" => @"
.invoice-sheet{--accent:#5c41aa;--accent-dark:#463087;--soft:#f1eff8}
.purple-header{background:linear-gradient(135deg,#5c41aa,#765ac2);color:#fff}.purple-header .company-details,.purple-header .company-details>div{color:#f7f4ff}.purple-header .company-name{color:#fff!important}.purple-header .header-right{color:#eee8ff}.purple-header .invoice-title{color:#fff}.purple-modern .items thead th{background:#5c41aa}.purple-modern .grand{background:#5c41aa;color:#fff}.purple-modern .party-block{background:#f6f3fc}",
            "TealProfessional" => @"
.invoice-sheet{--accent:#0c898b;--accent-dark:#075d5f;--soft:#edf9f8}
.teal-header{background:#0c898b;color:#fff;border-radius:0 0 7px 7px}.teal-header .company-details,.teal-header .company-details>div{color:#ebffff}.teal-header .company-name{color:#fff!important}.teal-header .header-right{color:#efffff}.teal-header .invoice-title{color:#fff}.teal-professional .items thead th{background:#0c898b}.teal-professional .grand{background:#0c898b;color:#fff}.teal-professional .party-block{background:#effafa}",
            "CurveTeal" => @"
.invoice-sheet{--accent:#109191;--accent-dark:#0a6768;--soft:#edfafa}
.curve-header{position:relative;overflow:hidden;color:#fff;min-height:142px;padding:18px 20px}.curve-teal{background:#109191}.curve-teal:after{content:'';position:absolute;right:-85px;top:-55px;width:250px;height:215px;background:#fff;border-radius:50%;opacity:.20}.curve-header .company-details>div{color:#efffff}.curve-header .company-details .company-name{color:#fff!important}.curve-title{position:absolute;right:30px;top:30px;font-size:24pt;font-weight:800;z-index:2}.curve-meta{position:absolute;right:30px;top:72px;display:flex;gap:12px;z-index:2;font-size:9pt}.curve-teal .items thead th{background:#109191}.curve-teal .grand{background:#109191;color:#fff}.curve-teal .party-block{background:#f0fbfb}",
            "CurveNavy" => @"
.invoice-sheet{--accent:#174858;--accent-dark:#0e303d;--soft:#edf3f6}
.curve-header{position:relative;overflow:hidden;color:#fff;min-height:142px;padding:18px 20px}.curve-navy{background:#174858}.curve-navy:after{content:'';position:absolute;right:-85px;top:-55px;width:250px;height:215px;background:#fff;border-radius:50%;opacity:.18}.curve-header .company-details>div{color:#eef7fa}.curve-header .company-details .company-name{color:#fff!important}.curve-title{position:absolute;right:30px;top:30px;font-size:24pt;font-weight:800;z-index:2}.curve-meta{position:absolute;right:30px;top:72px;display:flex;gap:12px;z-index:2;font-size:9pt}.curve-navy .items thead th{background:#174858}.curve-navy .grand{background:#174858;color:#fff}.curve-navy .party-block{background:#f0f5f7}",
            "BluePanel" => @"
.invoice-sheet{--accent:#466aaf;--accent-dark:#2d4c84;--soft:#eef3fb}
.blue-panel-header{display:flex;min-height:146px;border-bottom:6px solid #466aaf}.blue-panel-company{width:29%;background:#dce6f7;padding:16px 13px}.blue-panel-title{flex:1;display:flex;flex-direction:column;align-items:flex-end;justify-content:center;padding:18px 24px}.blue-panel-title .invoice-title{font-size:24pt;color:#466aaf}.blue-panel .items thead th{background:#466aaf}.blue-panel .grand{background:#466aaf;color:#fff}.blue-panel .party-block{background:#f2f6fc}",
            _ => @"
.invoice-sheet{--accent:#718238;--accent-dark:#4f632a;--soft:#f3f6eb}
.classic-header{border-bottom:1px solid #dfe5d9;background:#fff}.classic-professional .items thead th{background:#eef2e5;color:#394329}.classic-professional .grand{background:#718238;color:#fff}.classic-professional .party-block{background:#f7f9f2}.classic-professional .company-logo{border-radius:10px}"
        };
    }

    private static string BaseCss() => @"
@page{size:A4;margin:0}
*{box-sizing:border-box;-webkit-print-color-adjust:exact;print-color-adjust:exact}
html,body{margin:0;padding:0;background:#fff;width:100%;min-height:100%}
body{font-family:Arial,'Helvetica Neue',sans-serif;color:#252c33;font-size:9pt;line-height:1.33}
.invoice-sheet{width:210mm;max-width:210mm;margin:0 auto;padding:11mm 12mm 8mm;background:#fff;position:relative;overflow:visible}
.header{display:flex;justify-content:space-between;align-items:flex-start;gap:12mm;padding:3mm 0 4mm;margin-bottom:4mm;min-height:30mm}
.company-details{width:61%;font-size:8pt;color:#697277;line-height:1.34;min-width:0}.company-details>div{margin-bottom:1.5px}.company-name{font-size:18pt;font-weight:800;color:#1b2228!important;margin-bottom:3px!important}.company-logo{display:block;max-width:58mm;max-height:18mm;width:auto;height:auto;object-fit:contain;margin-bottom:2.5mm}
.header-right{text-align:right;min-width:43mm;color:#5f6870;font-size:8pt;line-height:1.45}.invoice-title{font-size:22pt;font-weight:800;letter-spacing:.35px;color:var(--accent);margin-bottom:1mm;white-space:nowrap}
.party-block{display:flex;padding:4mm 4mm;margin:0 0 5mm;border-radius:5px;gap:0}.party-col{width:50%;padding-right:5mm;min-width:0}.ship-col{padding-left:5mm;border-left:1px solid #dfe5e8}.section-label{font-size:6.8pt;font-weight:800;letter-spacing:.9px;color:var(--accent-dark);margin-bottom:1.5mm}.party-name{font-size:12.5pt;font-weight:800;margin-bottom:2mm;overflow-wrap:anywhere}.address{color:#5f676d;margin-top:2mm;line-height:1.4;overflow-wrap:anywhere}.invoice-meta{margin-top:3mm;display:grid;grid-template-columns:1fr 1fr;gap:1.5mm 4mm;color:#646d73}.invoice-meta span{display:block;font-size:6.9pt}.invoice-meta strong{display:block;color:#222a30;font-size:8pt;overflow-wrap:anywhere}
.items-section{margin-top:2mm}.items{width:100%;max-width:100%;border-collapse:collapse;table-layout:fixed;page-break-inside:auto}.items col.col-sr{width:6%}.items col.col-product{width:50%}.items col.col-qty{width:13%}.items col.col-rate{width:15%}.items col.col-total{width:16%}.items thead{display:table-header-group}.items tr{page-break-inside:avoid;page-break-after:auto}.items th{font-size:7pt;letter-spacing:.2px;padding:2.5mm 2mm;text-align:left;color:#fff;overflow:hidden}.items td{padding:2.7mm 2mm;border-bottom:1px solid #e0e5e7;vertical-align:top;font-size:8pt;overflow-wrap:anywhere;word-break:break-word;min-width:0}.items .sr{width:6%;text-align:center}.num{text-align:center!important}.money{text-align:right!important;white-space:nowrap}.qty .unit{color:#727b80;font-size:6.7pt;white-space:nowrap}.strong,.product-name{font-weight:700;color:#23292e}.product-meta{margin-top:1mm;font-size:6.8pt;color:#7b848a;line-height:1.25}.product-with-image{display:flex;gap:2.5mm;align-items:flex-start;min-width:0}.product-copy{min-width:0;flex:1}.product-image{width:15mm;height:15mm;object-fit:cover;border-radius:2mm;border:1px solid #d8dde0;flex:none}.empty{text-align:center;padding:5mm;color:#858e93}
.summary-area{display:grid;grid-template-columns:minmax(0,1fr) 55mm;gap:7mm;margin-top:5mm;align-items:start}.summary-left{min-width:0}.words-box,.notes-box{padding:3mm 3.5mm;border-radius:3mm;margin-bottom:3mm;background:var(--soft)}.words{font-weight:700;font-size:8.2pt;color:#343d43;line-height:1.4;overflow-wrap:anywhere}.notes{color:#6e777d;white-space:pre-line;font-size:7.7pt;overflow-wrap:anywhere}.totals{border-top:1px solid #e0e5e7;padding-top:2mm}.totals>div{display:flex;justify-content:space-between;gap:3mm;padding:1.6mm 1.5mm;font-size:8pt}.totals .grand{margin-top:1.5mm;border-radius:1.5mm;padding:3mm 2.5mm;font-size:9.8pt}.totals .grand strong{font-size:10pt}.thank-you{margin-top:6mm;font-size:9.5pt;font-weight:800;color:var(--accent-dark)}
.signature-row{display:flex;justify-content:space-between;align-items:flex-end;margin-top:9mm;gap:8mm;min-height:16mm}.signature{text-align:center;min-width:45mm;color:#626a70;font-size:7pt}.sign-line{border-top:1px solid #aeb7bc;margin-top:8mm;padding-top:1.5mm}.footer{margin-top:6mm;padding-top:2.5mm;border-top:1px solid #e1e5e8;display:flex;justify-content:space-between;gap:5mm;font-size:6.5pt;color:#81898e;overflow-wrap:anywhere}
.keep-together{page-break-inside:avoid;break-inside:avoid}
@media print{body{background:#fff}.invoice-sheet{margin:0 auto}}
";

    private static string CssKey(string key) => key switch
    {
        "PurpleModern" => "purple-modern",
        "TealProfessional" => "teal-professional",
        "CurveTeal" => "curve-teal",
        "CurveNavy" => "curve-navy",
        "BluePanel" => "blue-panel",
        _ => "classic-professional"
    };

    private static string ProductMeta(BillItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.ProductCode)) parts.Add($"Code: {E(item.ProductCode)}");
        if (!string.IsNullOrWhiteSpace(item.ProductHSN)) parts.Add($"HSN: {E(item.ProductHSN)}");
        return parts.Count == 0 ? string.Empty : $"<div class='product-meta'>{string.Join(" &nbsp;•&nbsp; ", parts)}</div>";
    }

    private static string Money(decimal value) => InvoiceFormatService.Money(value);

    private static string ImageTag(string? dataUri, string alt, string cssClass = "product-image")
        => string.IsNullOrWhiteSpace(dataUri)
            ? string.Empty
            : $"<img class='{cssClass}' src='{dataUri}' alt='{E(alt)}' />";

    private static async Task<string?> ImageDataUriAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var normalized = path.Trim();
            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                normalized = new Uri(normalized).LocalPath;

            if (!File.Exists(normalized)) return null;
            var bytes = await File.ReadAllBytesAsync(normalized);
            if (bytes.Length == 0) return null;

            var mime = Path.GetExtension(normalized).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "image/jpeg"
            };

            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private static string CompanyAddress(Company company)
        => JoinNonEmpty(company.Address, company.City, company.State, company.Pincode, company.Country);

    private static string JoinNonEmpty(params string?[] values)
        => string.Join(" • ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

    private static string LineIf(string? value, string label)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $"<div><strong>{E(label)}:</strong> {E(value)}</div>";

    private static string Multiline(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : E(value).Replace("\r\n", "<br>").Replace("\n", "<br>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
