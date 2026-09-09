using System.Text;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 6 — auto-generated package contract. A PackageRequest reaching "confirmed"
/// (via the Phase 4 flow) auto-creates a Contract from Package + CarePro standard
/// terms, with a real PDF from the reused ContractPdfService, and no negotiation.
/// </summary>
public class Phase6ContractTests
{
    private static CareProDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", name).Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_phase6_{Guid.NewGuid():N}";

    // Real ContractTemplateService + real ContractPdfService — used unmodified.
    private static PackageContractService CreateContractService(CareProDbContext db, Mock<IMediator>? mediator = null)
        => new(
            db,
            new ContractTemplateService(),
            new ContractPdfService(),
            (mediator ?? new Mock<IMediator>()).Object,
            Mock.Of<ILogger<PackageContractService>>());

    private static AssignmentService CreateAssignmentService(
        CareProDbContext db, PackageContractService contractService, Mock<IMediator> mediator)
    {
        var readiness = new Mock<ICaregiverReadinessService>();
        readiness.Setup(r => r.GetReadinessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>()))
            .ReturnsAsync(new CaregiverReadinessResult { IsReady = true });
        return new AssignmentService(
            db, mediator.Object, Mock.Of<IEmailService>(), readiness.Object,
            contractService, Mock.Of<ILogger<AssignmentService>>());
    }

    private static (Client client, Package pkg, Caregiver cg) Seed(
        CareProDbContext db, decimal basePrice = 350000m, string category = "Post-Partum Care",
        string tier = "Premium", string? specialty = "Midwifery")
    {
        var client = new Client
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Amara", LastName = "Okafor",
            Email = $"client-{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Client", IsDeleted = false, Address = "14 Bourdillon Rd, Ikoyi", PreferredCity = "Lagos"
        };
        var pkg = new Package
        {
            Id = ObjectId.GenerateNewId(), Category = category, TierLabel = tier,
            RequiredCaregiverType = CaregiverType.RegisteredNurse, RequiredSpecialty = specialty,
            BasePrice = basePrice, AdditionalDayPrice = 20000m,
            Description = "Daily post-partum recovery support and newborn care by a registered nurse.",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = "Ngozi", LastName = "Balogun",
            Email = $"cg-{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = true,
            CaregiverType = CaregiverType.RegisteredNurse, Specialty = "Midwifery",
            PhoneNo = "+2348030000000", CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        db.Packages.Add(pkg);
        db.CareGivers.Add(cg);
        db.SaveChanges();
        return (client, pkg, cg);
    }

    private static PackageRequest AddConfirmedRequest(CareProDbContext db, Client client, Package pkg, Caregiver cg)
    {
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(),
            ClientId = client.Id.ToString(),
            PackageId = pkg.Id.ToString(),
            PackageCategory = pkg.Category,
            PackageTierLabel = pkg.TierLabel,
            RequiredCaregiverType = pkg.RequiredCaregiverType,
            RequiredSpecialty = pkg.RequiredSpecialty,
            ServiceCategory = pkg.Category,
            Location = "14 Bourdillon Rd, Ikoyi, Lagos",
            Status = PackageRequestStatuses.Confirmed,
            ConfirmedCaregiverId = cg.Id.ToString(),
            ConfirmedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        db.PackageRequests.Add(pr);
        db.SaveChanges();
        return pr;
    }

    // ═══════════════ 6.1 + 6.2 + 6.3 — trigger, content, PDF, no negotiation ═══════════════

    [Fact]
    public async Task Acceptance_AutoGeneratesContract_WithPackageAndCareProTerms_AndRealPdf_NoNegotiation()
    {
        var dbName = NewDbName();
        string prId, clientId, cgId;

        using (var db = CreateDb(dbName))
        {
            var (client, pkg, cg) = Seed(db);
            clientId = client.Id.ToString();
            cgId = cg.Id.ToString();

            var mediator = new Mock<IMediator>();
            var contractSvc = CreateContractService(db, mediator);
            var assignmentSvc = CreateAssignmentService(db, contractSvc, mediator);
            var requestSvc = new PackageRequestService(db, Mock.Of<ILogger<PackageRequestService>>());

            var pr = await requestSvc.CreateAsync(clientId, new Application.DTOs.CreatePackageRequestRequest
            {
                PackageId = pkg.Id.ToString(), ServiceCategory = pkg.Category, Location = "Ikoyi, Lagos"
            });
            prId = pr.Id;

            var assignment = await assignmentSvc.AssignAsync(prId, cgId, "admin-1", "ops@x", "staff", null);
            // Phase 4 acceptance — caregiver passes ONLY (assignmentId, caregiverId): no schedule,
            // no proposed tasks, no contract input of any kind.
            var accept = await assignmentSvc.AcceptAsync(assignment.Id, cgId);
            Assert.Equal("Accepted", accept.Status);
        }

        // Contract was auto-generated the moment the request hit "confirmed".
        using (var db = CreateDb(dbName))
        {
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.PackageRequestId == prId);
            Assert.NotNull(contract);
            Assert.Equal(clientId, contract!.ClientId);
            Assert.Equal(cgId, contract.CaregiverId);
            Assert.Equal(ContractStatus.Generated, contract.Status);
            Assert.Equal("System", contract.InitiatedByRole);
            Assert.Equal(350000m, contract.TotalAmount);

            // Decoupled from any order/gig/payment (6.1).
            Assert.Equal(string.Empty, contract.OrderId);
            Assert.Equal(string.Empty, contract.GigId);
            Assert.Null(contract.PaymentTransactionId);

            // Content pulled straight from the Package (6.2).
            var terms = contract.GeneratedTerms;
            Assert.Contains("CAREPRO CARE SERVICE AGREEMENT", terms);
            Assert.Contains("Post-Partum Care", terms);
            Assert.Contains("Premium", terms);
            Assert.Contains("Daily post-partum recovery support", terms);
            // Standard CarePro terms present (rendered by the reused, unmodified template).
            Assert.Contains("Section 10: Disputes", terms);
            Assert.Contains("Section 13: Limitation of Liability", terms);

            // Negotiation-specific fields left at defaults / empty.
            Assert.Empty(contract.Schedule);
            Assert.Empty(contract.ProposedTasks);
            Assert.Null(contract.ClientApprovedAt);

            // 6.3 — a real PDF via the reused ContractPdfService.
            var pdf = await CreateContractService(db).GeneratePdfAsync(contract.Id);
            Assert.True(pdf.Length > 3000, $"PDF unexpectedly small: {pdf.Length} bytes");
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        // Client can now see the contract (only after confirmation).
        using (var db = CreateDb(dbName))
        {
            var dto = await CreateContractService(db).GetByPackageRequestAsync(prId);
            Assert.NotNull(dto);
            Assert.Equal("Post-Partum Care", dto!.PackageCategory);
            Assert.Equal("Premium", dto.PackageTierLabel);
            Assert.False(dto.NewlyGenerated);
        }
    }

    [Fact]
    public async Task Generation_IsIdempotent()
    {
        using var db = CreateDb(NewDbName());
        var (client, pkg, cg) = Seed(db);
        var pr = AddConfirmedRequest(db, client, pkg, cg);
        var svc = CreateContractService(db);

        var first = await svc.GenerateForConfirmedRequestAsync(pr.Id.ToString());
        Assert.True(first.NewlyGenerated);

        var second = await svc.GenerateForConfirmedRequestAsync(pr.Id.ToString());
        Assert.False(second.NewlyGenerated);
        Assert.Equal(first.Id, second.Id);

        Assert.Equal(1, await db.Contracts.CountAsync(c => c.PackageRequestId == pr.Id.ToString()));
    }

    [Fact]
    public async Task Generation_BeforeConfirmed_Throws()
    {
        using var db = CreateDb(NewDbName());
        var (client, pkg, cg) = Seed(db);
        var pr = new PackageRequest
        {
            Id = ObjectId.GenerateNewId(), ClientId = client.Id.ToString(), PackageId = pkg.Id.ToString(),
            PackageCategory = pkg.Category, PackageTierLabel = pkg.TierLabel,
            RequiredCaregiverType = pkg.RequiredCaregiverType, ServiceCategory = pkg.Category,
            Status = PackageRequestStatuses.Assigned, CreatedAt = DateTime.UtcNow
        };
        db.PackageRequests.Add(pr);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateContractService(db).GenerateForConfirmedRequestAsync(pr.Id.ToString()));
    }

    [Fact]
    public async Task Generation_WorksIndependentOfHowRequestReachedConfirmed()
    {
        // Simulates a request that reached "confirmed" via some other (e.g. payment) path —
        // the contract trigger is on the state, not on AcceptAsync specifically (6.1).
        using var db = CreateDb(NewDbName());
        var (client, pkg, cg) = Seed(db, basePrice: 120000m, category: "Adult/Elder Care", tier: "Standard", specialty: null);
        cg.CaregiverType = CaregiverType.CHEW;
        cg.Specialty = null;
        await db.SaveChangesAsync();
        var pr = AddConfirmedRequest(db, client, pkg, cg);

        var result = await CreateContractService(db).GenerateForConfirmedRequestAsync(pr.Id.ToString());

        Assert.True(result.NewlyGenerated);
        Assert.Equal(120000m, result.TotalAmount);
        Assert.Contains("Adult/Elder Care", result.GeneratedTermsHtml);

        var pdf = await CreateContractService(db).GeneratePdfAsync(result.Id);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Generation_NotifiesBothParties()
    {
        using var db = CreateDb(NewDbName());
        var (client, pkg, cg) = Seed(db);
        var pr = AddConfirmedRequest(db, client, pkg, cg);

        var mediator = new Mock<IMediator>();
        await CreateContractService(db, mediator).GenerateForConfirmedRequestAsync(pr.Id.ToString());

        mediator.Verify(m => m.Send(
            It.Is<Application.Commands.SendNotificationCommand>(c =>
                c.RecipientId == client.Id.ToString() && c.Type == NotificationTypes.PackageContractGenerated),
            It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(
            It.Is<Application.Commands.SendNotificationCommand>(c =>
                c.RecipientId == cg.Id.ToString() && c.Type == NotificationTypes.PackageContractGenerated),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
