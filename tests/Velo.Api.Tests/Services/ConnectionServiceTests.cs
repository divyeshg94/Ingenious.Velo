using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Velo.Api.Services;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Tests.Services;

public class ConnectionServiceTests : IDisposable
{
    private readonly VeloDbContext _dbContext;
    private readonly ConnectionService _sut;

    public ConnectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options);
        _dbContext.CurrentOrgId = "testorg";
        _sut = new ConnectionService(_dbContext, NullLogger<ConnectionService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    // ── RegisterAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ThrowsInvalidOperation_WhenOrgContextNotSet()
    {
        _dbContext.CurrentOrgId = null;

        var act = async () => await _sut.RegisterAsync("https://dev.azure.com/testorg", "pat", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*Org context not set*");
    }

    [Fact]
    public async Task RegisterAsync_InsertsNewOrg_WhenNotExists()
    {
        await _sut.RegisterAsync("https://dev.azure.com/testorg", "pat", CancellationToken.None);

        var saved = await _dbContext.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        saved.Should().NotBeNull();
        saved!.OrgUrl.Should().Be("https://dev.azure.com/testorg");
    }

    [Fact]
    public async Task RegisterAsync_SetsDisplayName_FromLastUrlSegment()
    {
        await _sut.RegisterAsync("https://dev.azure.com/mycompany", "pat", CancellationToken.None);

        var saved = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        saved!.DisplayName.Should().Be("mycompany");
    }

    [Fact]
    public async Task RegisterAsync_UpdatesExistingOrg_WhenAlreadyExists()
    {
        _dbContext.Organizations.Add(new OrgContext
        {
            OrgId = "testorg",
            OrgUrl = "https://dev.azure.com/testorg",
            DisplayName = "Old Name",
            RegisteredAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _sut.RegisterAsync("https://dev.azure.com/testorg-updated", "pat", CancellationToken.None);

        var updated = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        updated!.OrgUrl.Should().Be("https://dev.azure.com/testorg-updated");
    }

    [Fact]
    public async Task RegisterAsync_TrimsTrailingSlash()
    {
        await _sut.RegisterAsync("https://dev.azure.com/testorg/", "pat", CancellationToken.None);

        var saved = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        saved!.OrgUrl.Should().NotEndWith("/");
        saved.OrgUrl.Should().Be("https://dev.azure.com/testorg");
    }

    [Fact]
    public async Task RegisterAsync_SetsDefaultBudget_ForNewOrg()
    {
        await _sut.RegisterAsync("https://dev.azure.com/testorg", "pat", CancellationToken.None);

        var saved = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        saved!.DailyTokenBudget.Should().Be(50_000);
    }

    [Fact]
    public async Task RegisterAsync_SetsIsPremiumFalse_ForNewOrg()
    {
        await _sut.RegisterAsync("https://dev.azure.com/testorg", "pat", CancellationToken.None);

        var saved = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        saved!.IsPremium.Should().BeFalse();
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_ThrowsInvalidOperation_WhenOrgContextNotSet()
    {
        _dbContext.CurrentOrgId = null;

        var act = async () => await _sut.RemoveAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*Org context not set*");
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenOrgNotFound()
    {
        var act = async () => await _sut.RemoveAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_RemovesOrg_WhenExists()
    {
        _dbContext.Organizations.Add(new OrgContext
        {
            OrgId = "testorg",
            OrgUrl = "https://dev.azure.com/testorg",
            DisplayName = "Test Org",
            RegisteredAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _sut.RemoveAsync(CancellationToken.None);

        var removed = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "testorg");
        removed.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesCurrentOrgId()
    {
        _dbContext.Organizations.Add(new OrgContext
        {
            OrgId = "testorg",
            OrgUrl = "https://dev.azure.com/testorg",
            DisplayName = "Test",
            RegisteredAt = DateTimeOffset.UtcNow
        });
        _dbContext.Organizations.Add(new OrgContext
        {
            OrgId = "otherorg",
            OrgUrl = "https://dev.azure.com/otherorg",
            DisplayName = "Other",
            RegisteredAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _sut.RemoveAsync(CancellationToken.None);

        var otherOrg = await _dbContext.Organizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.OrgId == "otherorg");
        otherOrg.Should().NotBeNull();
    }
}
