using Xunit;

namespace IronMonkey.Tests.Unit;

public class CsvValidationTests
{
    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ParseCsvRow_WhenRequiredColumnsPresent_ReturnsLeadRecord() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ParseCsvRow_WhenFirstNameMissing_ThrowsValidationException() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ParseCsvRow_WhenEmailMissing_ThrowsValidationException() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ValidateCsvHeaders_WhenColumnNamesMatch_ReturnsValid() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ValidateCsvHeaders_WhenRequiredColumnMissing_ReturnsInvalid() { }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public void ValidateCsvHeaders_WhenCaseDoesNotMatch_ReturnsInvalid() { }
}
