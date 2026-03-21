using System.Text;
using IronMonkey.ApiService.Features.Leads.Ingestion.Csv;
using IronMonkey.Data.Entities;
using Xunit;

namespace IronMonkey.Tests.Unit;

public class CsvValidationTests
{
    private readonly CsvImportService _service = new();

    private static Stream ToCsvStream(string csv)
        => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public void ValidateCsvHeaders_WhenColumnNamesMatch_ReturnsValid()
    {
        using var stream = ToCsvStream("FirstName,LastName,Email\nJohn,Doe,john@test.com");
        var error = _service.ValidateHeaders(stream);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateCsvHeaders_WhenRequiredColumnMissing_ReturnsInvalid()
    {
        using var stream = ToCsvStream("FirstName,LastName\nJohn,Doe");
        var error = _service.ValidateHeaders(stream);
        Assert.NotNull(error);
        Assert.Contains("Email", error);
    }

    [Fact]
    public void ValidateCsvHeaders_WhenCaseDoesNotMatch_ReturnsInvalid()
    {
        // "firstname" (lowercase) does not match required "FirstName" (exact match per D-05)
        using var stream = ToCsvStream("firstname,lastname,email\njohn,doe,j@test.com");
        var error = _service.ValidateHeaders(stream);
        Assert.NotNull(error);
    }

    [Fact]
    public void ParseCsvRow_WhenRequiredColumnsPresent_ReturnsLeadRecord()
    {
        var row = new Dictionary<string, object>
        {
            ["FirstName"] = "Jane",
            ["LastName"] = "Smith",
            ["Email"] = "jane@test.com",
            ["Mobile"] = "555-9999"
        };
        var tenantId = Guid.NewGuid();
        var stageId = Guid.NewGuid();

        var lead = _service.ParseRow(row, tenantId, stageId);

        Assert.Equal("Jane", lead.FirstName);
        Assert.Equal("jane@test.com", lead.Email);
        Assert.Equal(LeadSource.Import, lead.Source);
    }

    [Fact]
    public void ParseCsvRow_WhenFirstNameMissing_ThrowsValidationException()
    {
        var row = new Dictionary<string, object>
        {
            ["FirstName"] = "",
            ["LastName"] = "Smith",
            ["Email"] = "jane@test.com"
        };
        Assert.Throws<InvalidOperationException>(() =>
            _service.ParseRow(row, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void ParseCsvRow_WhenEmailMissing_ThrowsValidationException()
    {
        var row = new Dictionary<string, object>
        {
            ["FirstName"] = "Jane",
            ["LastName"] = "Smith",
            ["Email"] = ""
        };
        Assert.Throws<InvalidOperationException>(() =>
            _service.ParseRow(row, Guid.NewGuid(), Guid.NewGuid()));
    }
}
