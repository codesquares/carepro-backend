using Application.DTOs;
using Application.Interfaces.Content;
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
/// Phase 8, Step 3 — Review re-keyed from Gig to Assignment (Gig-based direct hire no longer
/// exists under the Package/Assignment model). Confirms both the read/write path keys correctly
/// on AssignmentId, and that the one real rating-aggregate consumer left in the codebase
/// (CareRequestMatchingService's caregiver rating score, which aggregates Reviews by
/// CaregiverId — untouched by the rekey) still computes the correct average post-rekey.
/// </summary>
public class ReviewServiceRekeyTests
{
    private static CareProDbContext CreateDb()
    {
        var databaseName = $"carepro_review_rekey_tests_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static ReviewService CreateReviewService(
        CareProDbContext db, Mock<ICareGiverService> careGiverService, Mock<IClientService> clientService)
        => new(db, careGiverService.Object, clientService.Object, Mock.Of<IMediator>(), Mock.Of<ILogger<ReviewService>>());

    [Fact]
    public async Task CreateAndFetch_KeysOnAssignmentId_NotGigId()
    {
        using var db = CreateDb();
        var caregiverId = ObjectId.GenerateNewId().ToString();
        var clientId = ObjectId.GenerateNewId().ToString();
        var assignmentId = ObjectId.GenerateNewId().ToString();

        var careGiverServiceMock = new Mock<ICareGiverService>();
        careGiverServiceMock
            .Setup(x => x.GetCaregiverUserAsync(caregiverId))
            .ReturnsAsync(new CaregiverResponse { Id = caregiverId, FirstName = "Cara", LastName = "Giver" });

        var clientServiceMock = new Mock<IClientService>();
        clientServiceMock
            .Setup(x => x.GetClientUserAsync(clientId))
            .ReturnsAsync(new ClientResponse { Id = clientId, FirstName = "Cli", LastName = "Ent" });

        var service = CreateReviewService(db, careGiverServiceMock, clientServiceMock);

        var reviewId = await service.CreateReviewAsync(new AddReviewRequest
        {
            ClientId = clientId,
            CaregiverId = caregiverId,
            AssignmentId = assignmentId,
            Message = "Great care",
            Rating = 5
        });

        // Persisted with AssignmentId, not any Gig concept.
        var stored = await db.Reviews.FirstAsync(r => r.ReviewId.ToString() == reviewId);
        Assert.Equal(assignmentId, stored.AssignmentId);

        // Queries by AssignmentId find it.
        var byAssignment = await service.GetReviewsByAssignmentAsync(assignmentId);
        var reviewDto = Assert.Single(byAssignment);
        Assert.Equal(assignmentId, reviewDto.AssignmentId);
        Assert.Equal("Cara Giver", reviewDto.CaregiverName);
        Assert.Equal(1, await service.GetReviewCountAsync(assignmentId));

        // A different assignment sees nothing.
        Assert.Empty(await service.GetReviewsByAssignmentAsync(ObjectId.GenerateNewId().ToString()));
        Assert.Equal(0, await service.GetReviewCountAsync(ObjectId.GenerateNewId().ToString()));
    }

    [Fact]
    public async Task CaregiverRatingAggregate_StillComputesCorrectly_PostRekey()
    {
        using var db = CreateDb();

        var caregiver = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Rated", LastName = "Caregiver",
            Email = $"cg-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = true,
            CaregiverType = CaregiverType.RegisteredNurse,
            CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(caregiver);
        db.Gigs.Add(new Gig
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverId = caregiver.Id.ToString(),
            Title = "General care", Category = "General", SubCategory = "General",
            Status = "Active", Price = 15000, CreatedAt = DateTime.UtcNow
        });

        // Three reviews, each keyed by a distinct AssignmentId (no Gig link at all) —
        // ratings 5, 4, 3 → average 4.0.
        int[] ratings = { 5, 4, 3 };
        foreach (var rating in ratings)
        {
            db.Reviews.Add(new Review
            {
                ReviewId = ObjectId.GenerateNewId(),
                ClientId = ObjectId.GenerateNewId().ToString(),
                CaregiverId = caregiver.Id.ToString(),
                AssignmentId = ObjectId.GenerateNewId().ToString(),
                Rating = rating,
                Message = "ok",
                ReviewedOn = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var matchingService = new CareRequestMatchingService(
            db,
            Mock.Of<IGeocodingService>(),
            new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()),
            Mock.Of<ILogger<CareRequestMatchingService>>());

        var matches = await matchingService.FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = "General",
            RequiredCaregiverType = "RegisteredNurse"
        });

        var match = Assert.Single(matches);
        Assert.Equal(caregiver.Id.ToString(), match.CaregiverId);
        Assert.Equal(3, match.ReviewCount);
        Assert.Equal(4.0, match.AverageRating);
    }
}
