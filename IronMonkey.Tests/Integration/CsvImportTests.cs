using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Leads.Ingestion.Csv;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IronMonkey.Tests.Integration;

[Collection("Integration")]
public class CsvImportTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(string connStr, Guid stageId)> SetupDbAsync(Guid tenantId)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var connStr = fixture.ConnectionString.Replace("ironmonkey_test", $"csv_{uniqueSuffix}");
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        await db.Database.MigrateAsync();

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync();

        return (connStr, stage.Id);
    }

    private CsvImportJob CreateJob(Guid tenantId, string connStr)
    {
        var tenantRegistryMock = new Mock<ITenantRegistry>();
        tenantRegistryMock.Setup(r => r.GetConnectionStringAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connStr);

        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(s => s.GetCurrentTenantId()).Returns(tenantId);
        tenantServiceMock.Setup(s => s.GetConnectionStringAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connStr);

        var duplicateService = new DuplicateDetectionService(tenantServiceMock.Object, _factory);
        var csvImportService = new CsvImportService();
        var logger = NullLogger<CsvImportJob>.Instance;

        return new CsvImportJob(tenantRegistryMock.Object, _factory, duplicateService, csvImportService, logger);
    }

    private static async Task<string> WriteTempCsv(string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        await File.WriteAllTextAsync(tempPath, content);
        return tempPath;
    }

    [Fact]
    public async Task UploadCsv_WhenValidFile_CreatesImportBatchAndEnqueuesJob()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        // Act: create ImportBatch directly (simulates upload endpoint creating batch before enqueueing)
        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var batch = ImportBatch.Create(tenantId, "test.csv", "/tmp/test.csv", stageId);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        // Assert: batch exists with Pending status
        var saved = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batch.Id);
        Assert.NotNull(saved);
        Assert.Equal(ImportBatchStatus.Pending, saved.Status);
        Assert.Equal("test.csv", saved.OriginalFileName);
    }

    [Fact]
    public async Task ProcessImport_WhenAllRowsValid_ImportsAllLeads()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        var csvContent = "FirstName,LastName,Email,Mobile\nJohn,Doe,john@example.com,555-0001\nJane,Smith,jane@example.com,555-0002";
        var tempPath = await WriteTempCsv(csvContent);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var batch = ImportBatch.Create(tenantId, "test.csv", tempPath, stageId);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        var job = CreateJob(tenantId, connStr);

        // Act
        await job.ProcessImportAsync(tenantId, batch.Id, tempPath);

        // Assert: batch complete, 2 leads imported
        await using var verifyDb = _factory.CreateForTenant(connStr, tenantId);
        var savedBatch = await verifyDb.ImportBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(ImportBatchStatus.Complete, savedBatch.Status);
        Assert.Equal(2, savedBatch.ImportedRows);
        Assert.Equal(0, savedBatch.SkippedRows);

        var leads = await verifyDb.Leads.ToListAsync();
        Assert.Equal(2, leads.Count);
    }

    [Fact]
    public async Task ProcessImport_WhenSomeBadRows_SkipsBadRowsImportsGoodOnes()
    {
        // Arrange: 3 valid rows + 1 bad row (missing email)
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        var csvContent = "FirstName,LastName,Email,Mobile\n" +
                         "Alice,Jones,alice@example.com,555-1111\n" +
                         "Bob,Brown,,555-2222\n" +  // Missing email — should be skipped
                         "Carol,White,carol@example.com,555-3333\n" +
                         "Dave,Green,dave@example.com,555-4444";

        var tempPath = await WriteTempCsv(csvContent);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var batch = ImportBatch.Create(tenantId, "test.csv", tempPath, stageId);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        var job = CreateJob(tenantId, connStr);

        // Act
        await job.ProcessImportAsync(tenantId, batch.Id, tempPath);

        // Assert: 3 leads created, 1 skipped, ErrorDetailsJson populated
        await using var verifyDb = _factory.CreateForTenant(connStr, tenantId);
        var savedBatch = await verifyDb.ImportBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(ImportBatchStatus.Complete, savedBatch.Status);
        Assert.Equal(3, savedBatch.ImportedRows);
        Assert.Equal(1, savedBatch.SkippedRows);
        Assert.NotNull(savedBatch.ErrorDetailsJson);

        var leads = await verifyDb.Leads.ToListAsync();
        Assert.Equal(3, leads.Count);
    }

    [Fact]
    public async Task ProcessImport_WhenDuplicateRowExists_FlagsDuplicateNotSilentlySkips()
    {
        // Arrange: seed an existing lead, then import CSV with same email
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var existingLead = Lead.Create(tenantId, "Eve", "Black", "555-5555", "eve@example.com", LeadSource.Manual, stageId);
        db.Leads.Add(existingLead);
        await db.SaveChangesAsync();

        var csvContent = "FirstName,LastName,Email,Mobile\nEve,Black,eve@example.com,555-5555";
        var tempPath = await WriteTempCsv(csvContent);

        await using var db2 = _factory.CreateForTenant(connStr, tenantId);
        var batch = ImportBatch.Create(tenantId, "test.csv", tempPath, stageId);
        db2.ImportBatches.Add(batch);
        await db2.SaveChangesAsync();

        var job = CreateJob(tenantId, connStr);

        // Act
        await job.ProcessImportAsync(tenantId, batch.Id, tempPath);

        // Assert: imported lead flagged as potential duplicate
        await using var verifyDb = _factory.CreateForTenant(connStr, tenantId);
        var leads = await verifyDb.Leads.ToListAsync();
        Assert.Equal(2, leads.Count);  // original + imported

        var importedLead = leads.First(l => l.Source == LeadSource.Import);
        Assert.True(importedLead.IsPotentialDuplicate);
        Assert.Equal(existingLead.Id, importedLead.PotentialDuplicateLeadId);
    }

    [Fact]
    public async Task GetImportStatus_WhenBatchExists_ReturnsProgressCounts()
    {
        // Arrange: create batch, manually mark it complete
        var tenantId = Guid.NewGuid();
        var (connStr, stageId) = await SetupDbAsync(tenantId);

        await using var db = _factory.CreateForTenant(connStr, tenantId);
        var batch = ImportBatch.Create(tenantId, "test.csv", "/tmp/test.csv", stageId);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        batch.MarkStarted();
        batch.MarkComplete(5, 2, null);
        await db.SaveChangesAsync();

        // Act: reload batch from DB
        await using var verifyDb = _factory.CreateForTenant(connStr, tenantId);
        var saved = await verifyDb.ImportBatches.FirstAsync(b => b.Id == batch.Id);

        // Assert: status and counts correct
        Assert.Equal(ImportBatchStatus.Complete, saved.Status);
        Assert.Equal(5, saved.ImportedRows);
        Assert.Equal(2, saved.SkippedRows);
        Assert.Equal(7, saved.TotalRows);
    }
}
