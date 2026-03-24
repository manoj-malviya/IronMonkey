using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.Csv;

/// <summary>
/// Validates CSV headers and parses rows to Lead entities.
/// Uses CsvHelper 33.1.0 — strict column name matching, no index-based mapping.
/// </summary>
public class CsvImportService
{
    // Required CSV column names — case-sensitive, exact match per D-05
    private static readonly string[] RequiredColumns = ["FirstName", "LastName", "Email"];
    private static readonly string[] OptionalColumns = ["Mobile", "PipelineStageId"];

    /// <summary>
    /// Validates CSV file headers. Returns error message if invalid, null if valid.
    /// Called BEFORE enqueueing job — reject file immediately if headers are wrong.
    /// </summary>
    public string? ValidateHeaders(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim
        };

        using var csv = new CsvReader(reader, config);
        csv.Read();
        csv.ReadHeader();

        var headers = csv.HeaderRecord ?? [];
        var missingRequired = RequiredColumns.Except(headers, StringComparer.Ordinal).ToList();

        if (missingRequired.Any())
            return $"Missing required columns: {string.Join(", ", missingRequired)}. " +
                   $"Required: {string.Join(", ", RequiredColumns)}";

        // Reset stream position for subsequent use
        csvStream.Position = 0;
        return null;
    }

    /// <summary>
    /// Parses a single CSV row dictionary (from CsvHelper dynamic record) into a Lead.
    /// Throws InvalidOperationException with descriptive message if row is invalid.
    /// </summary>
    public Lead ParseRow(
        IDictionary<string, object> row,
        Guid tenantId,
        Guid defaultPipelineStageId)
    {
        var firstName = GetRequired(row, "FirstName");
        var lastName = GetRequired(row, "LastName");
        var email = GetRequired(row, "Email");

        // Validate email format minimally — not full RFC, just has @
        if (!email.Contains('@'))
            throw new InvalidOperationException($"Email '{email}' is not a valid email address");

        var mobile = GetOptional(row, "Mobile") ?? string.Empty;

        // PipelineStageId: use from row if valid Guid, else use default
        Guid pipelineStageId = defaultPipelineStageId;
        var stageIdStr = GetOptional(row, "PipelineStageId");
        if (!string.IsNullOrWhiteSpace(stageIdStr))
        {
            if (!Guid.TryParse(stageIdStr, out pipelineStageId))
                throw new InvalidOperationException($"PipelineStageId '{stageIdStr}' is not a valid GUID");
        }

        return Lead.Create(tenantId, firstName, lastName, mobile, email, LeadSource.Import, pipelineStageId);
    }

    /// <summary>
    /// Serializes import row errors to CSV format for the error download file.
    /// Columns: RowNumber, ErrorMessage
    /// </summary>
    public string SerializeErrorsToCsv(List<ImportRowError> errors)
    {
        using var writer = new StringWriter();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, config);

        csv.WriteHeader<ImportRowError>();
        csv.NextRecord();
        foreach (var error in errors)
        {
            csv.WriteRecord(error);
            csv.NextRecord();
        }

        return writer.ToString();
    }

    private static string GetRequired(IDictionary<string, object> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value == null || string.IsNullOrWhiteSpace(value.ToString()))
            throw new InvalidOperationException($"Required column '{column}' is missing or empty");
        return value.ToString()!.Trim();
    }

    private static string? GetOptional(IDictionary<string, object> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value == null)
            return null;
        var str = value.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }
}
