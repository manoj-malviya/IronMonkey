using System.Net;
using System.Text;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;

namespace IronMonkey.ApiService.Features.Quotes;

/// <summary>
/// Renders a quote as a self-contained HTML document — the thing the customer reads, prints
/// or saves as PDF.
///
/// <para><b>Every figure comes from the quote's frozen lines.</b> Nothing is recomputed here,
/// and money is formatted by <see cref="TenantFormatting.Money"/>, the same formatter the
/// web UI uses — so the document, the CRM screen and the API agree to the penny.</para>
///
/// <para><b>Every interpolated value is HTML-encoded.</b> A recipient name can arrive from a
/// public web form and a line description from any user; the public variant of this page is
/// unauthenticated, attacker-reachable surface. Branding colours are validated as hex before
/// they reach a style attribute, and the logo is emitted only for an https URL.</para>
///
/// <para>Unit cost and margin are never rendered: the document is customer-facing.</para>
/// </summary>
public static class QuoteDocumentRenderer
{
    /// <param name="respondAction">For the public page: the form action for accept/reject.
    /// Null renders a read-only document (internal preview, or a quote no longer open).</param>
    public static string Render(Quote quote, TenantBranding? branding, string tenantName,
        TenantFormatting formatting, string? respondAction = null, string? notice = null)
    {
        var accent = SafeColor(branding?.PrimaryColor) ?? "#4f46e5";
        var displayName = string.IsNullOrWhiteSpace(branding?.DisplayName) ? tenantName : branding!.DisplayName!;
        string Money(decimal v) => E(formatting.Money(v, quote.CurrencyCode));

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<meta name=\"robots\" content=\"noindex, nofollow\">");
        html.Append("<title>").Append(E($"{quote.DisplayNumberWithVersion} — {displayName}")).Append("</title>");
        html.Append("<style>")
            .Append("body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#0f172a;background:#f8fafc;margin:0;padding:24px}")
            .Append(".doc{max-width:860px;margin:0 auto;background:#fff;border:1px solid #e2e8f0;border-radius:12px;padding:32px}")
            .Append("header{display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap;border-bottom:3px solid ").Append(accent).Append(";padding-bottom:16px}")
            .Append("h1{margin:0;font-size:22px}.muted{color:#64748b;font-size:13px}")
            .Append("table{width:100%;border-collapse:collapse;margin-top:24px;font-size:14px}")
            .Append("th{text-align:left;color:#475569;font-weight:600;border-bottom:1px solid #e2e8f0;padding:8px 6px}")
            .Append("td{border-bottom:1px solid #f1f5f9;padding:8px 6px;vertical-align:top}.num{text-align:right;white-space:nowrap}")
            .Append(".totals{margin-left:auto;margin-top:16px;width:320px}.totals td{border:none;padding:4px 6px}")
            .Append(".grand td{font-weight:700;font-size:16px;border-top:2px solid #0f172a}")
            .Append(".status{display:inline-block;padding:2px 10px;border-radius:999px;font-size:12px;font-weight:600;background:#e2e8f0}")
            .Append(".terms{white-space:pre-wrap;font-size:13px;color:#334155;margin-top:24px}")
            .Append(".respond{margin-top:24px;border-top:1px solid #e2e8f0;padding-top:16px}")
            .Append("input,textarea{width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:6px;box-sizing:border-box;margin:4px 0 12px}")
            .Append("button{padding:10px 18px;border-radius:8px;border:none;font-weight:600;cursor:pointer;margin-right:8px}")
            .Append(".accept{background:").Append(accent).Append(";color:#fff}.reject{background:#fee2e2;color:#991b1b}")
            .Append(".notice{background:#ecfdf5;border:1px solid #a7f3d0;padding:10px 14px;border-radius:8px;margin-bottom:16px}")
            .Append("@media print{body{background:#fff;padding:0}.doc{border:none}.respond{display:none}}")
            .Append("</style></head><body><main class=\"doc\">");

        if (!string.IsNullOrWhiteSpace(notice))
            html.Append("<div class=\"notice\">").Append(E(notice)).Append("</div>");

        html.Append("<header><div>");
        if (SafeLogo(branding?.LogoUrl) is { } logo)
            html.Append("<img src=\"").Append(E(logo)).Append("\" alt=\"\" style=\"max-height:48px;display:block;margin-bottom:8px\">");
        html.Append("<div style=\"font-weight:700\">").Append(E(displayName)).Append("</div>");
        html.Append("<div class=\"muted\">Prepared for ").Append(E(quote.RecipientName)).Append("</div>");
        html.Append("</div><div style=\"text-align:right\">");
        html.Append("<h1>Quote ").Append(E(quote.DisplayNumber)).Append("</h1>");
        html.Append("<div class=\"muted\">Version ").Append(quote.Version).Append("</div>");
        html.Append("<div class=\"muted\">Valid until ").Append(E(quote.ValidUntil.ToString("d MMM yyyy"))).Append("</div>");
        html.Append("<div style=\"margin-top:6px\"><span class=\"status\">").Append(E(quote.Status.ToString())).Append("</span></div>");
        html.Append("</div></header>");

        html.Append("<h2 style=\"font-size:16px;margin:20px 0 0\">").Append(E(quote.Title)).Append("</h2>");

        html.Append("<table><thead><tr><th>Item</th><th class=\"num\">Qty</th><th class=\"num\">Unit price</th>")
            .Append("<th class=\"num\">Discount</th><th class=\"num\">Tax</th><th class=\"num\">Total</th></tr></thead><tbody>");

        foreach (var line in quote.Lines.OrderBy(l => l.Position))
        {
            html.Append("<tr><td>").Append(E(line.Description));
            if (!string.IsNullOrWhiteSpace(line.ProductCode))
                html.Append(" <span class=\"muted\">(").Append(E(line.ProductCode)).Append(")</span>");
            if (line.ChargeType == ChargeType.Recurring)
                html.Append("<div class=\"muted\">").Append(E(RecurrenceLabel(line.BillingFrequency, line.Periods))).Append("</div>");
            html.Append("</td>");
            html.Append("<td class=\"num\">").Append(E(line.Quantity.ToString("0.####"))).Append("</td>");
            html.Append("<td class=\"num\">").Append(Money(line.UnitPrice)).Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.DiscountAmount == 0 ? "—" : $"{E(line.DiscountPercent.ToString("0.##"))}% (−{Money(line.DiscountAmount)})")
                .Append("</td>");
            html.Append("<td class=\"num\">")
                .Append(line.TaxAmount == 0 ? "—" : Money(line.TaxAmount))
                .Append("</td>");
            html.Append("<td class=\"num\">").Append(Money(line.TotalAmount)).Append("</td></tr>");
        }

        html.Append("</tbody></table>");

        html.Append("<table class=\"totals\">");
        Row(html, "Subtotal", Money(quote.Subtotal));
        if (quote.DiscountTotal != 0) Row(html, "Discount", "−" + Money(quote.DiscountTotal));
        if (quote.TaxTotal != 0) Row(html, "Tax", Money(quote.TaxTotal));
        html.Append("<tr class=\"grand\"><td>Total</td><td class=\"num\">").Append(Money(quote.Total)).Append("</td></tr>");
        if (quote.RecurringTotal != 0)
        {
            Row(html, "of which one-off", Money(quote.OneOffTotal));
            Row(html, "of which recurring", Money(quote.RecurringTotal));
        }
        html.Append("</table>");

        if (!string.IsNullOrWhiteSpace(quote.Terms))
            html.Append("<h3 style=\"font-size:14px;margin-top:24px\">Terms</h3><div class=\"terms\">").Append(E(quote.Terms)).Append("</div>");

        if (quote.Status is QuoteStatus.Accepted or QuoteStatus.Rejected && quote.RespondedByName is not null)
        {
            html.Append("<p class=\"muted\" style=\"margin-top:24px\">")
                .Append(E($"{quote.Status} by {quote.RespondedByName}"))
                .Append(quote.RespondedAt is { } at ? E($" on {formatting.DateTimeWithZone(at)}") : "")
                .Append("</p>");
        }

        if (respondAction is not null)
        {
            html.Append("<form class=\"respond\" method=\"post\" action=\"").Append(E(respondAction)).Append("\">");
            html.Append("<label>Your name<input name=\"name\" required maxlength=\"200\" autocomplete=\"name\"></label>");
            html.Append("<label>Note (optional)<textarea name=\"note\" rows=\"3\" maxlength=\"2000\"></textarea></label>");
            html.Append("<button class=\"accept\" name=\"decision\" value=\"accept\" type=\"submit\">Accept quote</button>");
            html.Append("<button class=\"reject\" name=\"decision\" value=\"reject\" type=\"submit\">Decline</button>");
            html.Append("</form>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    /// <summary>A minimal page for an invalid, expired or revoked link — deliberately
    /// identical for every case, so the page is not an oracle for which tokens exist.</summary>
    public static string RenderUnavailable() =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex\">" +
        "<title>Link unavailable</title></head><body style=\"font-family:system-ui,sans-serif;padding:40px;color:#334155\">" +
        "<h1 style=\"font-size:20px\">This link is no longer available</h1>" +
        "<p>It may have expired or been withdrawn. Please contact the sender for an up-to-date quote.</p></body></html>";

    public static string RecurrenceLabel(BillingFrequency frequency, int periods)
    {
        var unit = frequency switch
        {
            BillingFrequency.Monthly => "month",
            BillingFrequency.Quarterly => "quarter",
            BillingFrequency.PerTerm => "term",
            BillingFrequency.Annually => "year",
            _ => "period"
        };
        return periods == 1 ? $"Per {unit}, 1 {unit}" : $"Per {unit}, {periods} {unit}s";
    }

    private static void Row(StringBuilder html, string label, string encodedValue) =>
        html.Append("<tr><td>").Append(E(label)).Append("</td><td class=\"num\">").Append(encodedValue).Append("</td></tr>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string? SafeColor(string? color) =>
        color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$")
            ? color : null;

    private static string? SafeLogo(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;
}
