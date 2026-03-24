using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public class GetWebFormPageEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/forms/{token}", Handle)
        .WithSummary("Render the hosted web form HTML page")
        .WithTags("Web Forms")
        .AllowAnonymous();

    private static async Task<Results<ContentHttpResult, NotFound>> Handle(
        string token,
        IWebFormService webFormService,
        CancellationToken cancellationToken)
    {
        var form = await webFormService.GetByTokenAsync(token, cancellationToken);
        if (form == null)
            return TypedResults.NotFound();

        var fields = form.GetFieldNames();
        var fieldHtml = BuildFieldHtml(fields);
        var formName = System.Web.HttpUtility.HtmlEncode(form.FormName);

        var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{formName}}</title>
    <style>
        body { font-family: system-ui, sans-serif; max-width: 480px; margin: 40px auto; padding: 0 16px; }
        h1 { font-size: 1.5rem; margin-bottom: 24px; }
        label { display: block; margin-bottom: 4px; font-weight: 500; font-size: 0.9rem; }
        input, select { width: 100%; padding: 8px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; margin-bottom: 16px; font-size: 1rem; }
        button { background: #2563eb; color: white; border: none; padding: 10px 24px; border-radius: 4px; cursor: pointer; font-size: 1rem; }
        button:hover { background: #1d4ed8; }
    </style>
</head>
<body>
    <h1>{{formName}}</h1>
    <form method="POST" action="/forms/{{token}}/submit">
{{fieldHtml}}
        <!-- Honeypot: hidden from humans via CSS, visible to bots -->
        <div style="position:absolute;left:-9999px;top:-9999px;overflow:hidden;" aria-hidden="true">
            <label for="website">Leave this field empty</label>
            <input type="text" id="website" name="Website" tabindex="-1" autocomplete="one-time-code" />
        </div>
        <button type="submit">Submit</button>
    </form>
</body>
</html>
""";

        return TypedResults.Content(html, "text/html");
    }

    private static string BuildFieldHtml(List<string> fieldNames)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var fieldName in fieldNames)
        {
            var label = System.Web.HttpUtility.HtmlEncode(SplitCamelCase(fieldName));
            var name = System.Web.HttpUtility.HtmlEncode(fieldName);
            var inputType = fieldName.ToLower().Contains("email") ? "email" : "text";
            sb.AppendLine($"""        <label for="{name}">{label}</label>""");
            sb.AppendLine($"""        <input type="{inputType}" id="{name}" name="{name}" />""");
        }
        return sb.ToString();
    }

    private static string SplitCamelCase(string input)
        => System.Text.RegularExpressions.Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
}
