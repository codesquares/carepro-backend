using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CommitmentGate.Tests;

public class ContractConcurrencyTokenProbeTests
{
    [Fact]
    public async Task SecondWriterOfTheWindowOutcome_FailsInsteadOfOverwriting()
    {
        var name = Phase6ContractTests.NewDbName();
        string id;
        using (var seed = Phase6ContractTests.CreateDb(name))
        {
            var c = new Contract { PackageRequestId = "pr1", ContractSentAt = DateTime.UtcNow, CancellationWindowEndsAt = DateTime.UtcNow.AddHours(-1) };
            seed.Contracts.Add(c); await seed.SaveChangesAsync(); id = c.Id;
        }
        using var a = Phase6ContractTests.CreateDb(name);
        using var b = Phase6ContractTests.CreateDb(name);
        var ca = await a.Contracts.FirstAsync(x => x.Id == id);
        var cb = await b.Contracts.FirstAsync(x => x.Id == id);
        ca.CancelledAt = DateTime.UtcNow; await a.SaveChangesAsync();              // writer 1 wins
        cb.AcceptedByNonCancellationAt = DateTime.UtcNow;                          // writer 2 read stale state
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
        using var check = Phase6ContractTests.CreateDb(name);
        var final = await check.Contracts.AsNoTracking().FirstAsync(x => x.Id == id);
        Assert.NotNull(final.CancelledAt);
        Assert.Null(final.AcceptedByNonCancellationAt);
    }
}
