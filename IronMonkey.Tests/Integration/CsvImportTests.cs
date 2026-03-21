using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class CsvImportTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public CsvImportTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task UploadCsv_WhenValidFile_CreatesImportBatchAndEnqueuesJob() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task ProcessImport_WhenAllRowsValid_ImportsAllLeads() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task ProcessImport_WhenSomeBadRows_SkipsBadRowsImportsGoodOnes() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task ProcessImport_WhenDuplicateRowExists_FlagsDuplicateNotSilentlySkips() { await Task.CompletedTask; }

    [Fact(Skip = "Phase 03 — implement in 03-06-PLAN")]
    public async Task GetImportStatus_WhenBatchExists_ReturnsProgressCounts() { await Task.CompletedTask; }
}
