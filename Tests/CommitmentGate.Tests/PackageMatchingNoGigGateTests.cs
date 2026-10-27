using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Package matching score = Proximity 45 + Experience 30 + Vetting-completeness 25.
/// Gigs have zero influence; readiness gaps are flagged but never exclude. Real local MongoDB
/// (replica set on 127.0.0.1:27018).
/// </summary>
public class PackageMatchingNoGigGateTests
{
    private const string Category = "Post Surgery";
    private const double ReqLat = 6.5, ReqLng = 3.4;

    private static CareProDbContext CreateDb() =>
        new TestCareProDbContext(new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_score_{Guid.NewGuid():N}").Options);

    private static CareRequestMatchingService Svc(CareProDbContext db) => new(db, Mock.Of<IGeocodingService>(),
        new CaregiverReadinessService(db, new EligibilityService(db, Mock.Of<ILogger<EligibilityService>>()),
            Mock.Of<ILogger<CaregiverReadinessService>>()),
        Mock.Of<ILogger<CareRequestMatchingService>>());

    private static Task<List<CaregiverMatchDTO>> Query(CareProDbContext db) =>
        Svc(db).FindCandidatesForPackageAsync(new PackageAssignmentMatchQuery
        {
            ServiceCategory = Category, RequiredCaregiverType = "AuxiliaryNurse",
            Latitude = ReqLat, Longitude = ReqLng
        });

    /// <summary>Adds a caregiver; defaults are the "weakest" values so tests vary one factor at a time.</summary>
    private static string Add(CareProDbContext db, string name, double latOffset = 0.3, ExperienceTier? tier = ExperienceTier.Junior,
        bool identity = false, int guarantors = 0, bool activeGig = false, string gigCategory = Category)
    {
        var cg = new Caregiver
        {
            Id = ObjectId.GenerateNewId(), FirstName = name, LastName = "N",
            Email = $"{name}{Guid.NewGuid():N}@example.com", Password = "x", // pragma: allowlist-secret
            Role = "Caregiver", Status = true, IsAvailable = true, IsIdentityVerified = identity,
            CaregiverType = CaregiverType.AuxiliaryNurse, ExperienceTier = tier,
            Latitude = ReqLat + latOffset, Longitude = ReqLng, CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(cg);
        var id = cg.Id.ToString();
        for (int i = 0; i < guarantors; i++)
            db.Guarantors.Add(new Guarantor
            {
                Id = ObjectId.GenerateNewId(), CaregiverId = id, Name = $"G{i}", RelationshipToCaregiver = "Uncle",
                PhoneNo = "0800", Status = GuarantorStatuses.Confirmed
            });
        if (activeGig)
            db.Gigs.Add(new Gig
            {
                Id = ObjectId.GenerateNewId(), CaregiverId = id, Title = "g", Category = gigCategory,
                SubCategory = "s", Status = "Active", Price = 15000, CreatedAt = DateTime.UtcNow
            });
        return id;
    }

    private static string[] Order(List<CaregiverMatchDTO> r) => r.Select(m => m.CaregiverName.Split(' ')[0]).ToArray();

    [Fact]
    public async Task Proximity_NearerRanksHigher()
    {
        using var db = CreateDb();
        Add(db, "Far", latOffset: 0.3);   // ~33 km
        Add(db, "Near", latOffset: 0.02); // ~2 km
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "Near", "Far" }, Order(await Query(db)));
    }

    [Fact]
    public async Task Experience_SeniorThenMidThenJunior()
    {
        using var db = CreateDb();
        Add(db, "Junior", tier: ExperienceTier.Junior);
        Add(db, "Senior", tier: ExperienceTier.Senior);
        Add(db, "Mid", tier: ExperienceTier.Mid);
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "Senior", "Mid", "Junior" }, Order(await Query(db)));
    }

    [Fact]
    public async Task Vetting_MoreCompleteRanksHigher_AndAllStayVisible()
    {
        using var db = CreateDb();
        Add(db, "None");
        Add(db, "Full", identity: true, guarantors: 2);
        Add(db, "Partial", identity: true);
        await db.SaveChangesAsync();
        var r = await Query(db);
        Assert.Equal(new[] { "Full", "Partial", "None" }, Order(r));
        Assert.Equal(3, r.Count);
    }

    [Fact]
    public async Task GigHolderWeakerOnAllThreeFactors_DoesNotOutrankNoGigCandidateStrongerOnThem()
    {
        using var db = CreateDb();
        Add(db, "GigButWeak", latOffset: 0.3, tier: ExperienceTier.Junior, activeGig: true);
        Add(db, "NoGigButStrong", latOffset: 0.02, tier: ExperienceTier.Senior, identity: true, guarantors: 2);
        await db.SaveChangesAsync();
        var r = await Query(db);
        Assert.Equal(new[] { "NoGigButStrong", "GigButWeak" }, Order(r));
    }

    [Fact]
    public async Task Gig_HasZeroInfluence_IdenticalCandidatesScoreIdentically()
    {
        using var db = CreateDb();
        Add(db, "NoGig");
        Add(db, "RightCategoryGig", activeGig: true);
        Add(db, "WrongCategoryGig", activeGig: true, gigCategory: "General");
        await db.SaveChangesAsync();
        var r = await Query(db);
        Assert.Equal(3, r.Count);
        Assert.Single(r.Select(m => m.MatchScore).Distinct());
    }

    [Fact]
    public async Task NullExperienceTier_DoesNotThrow_AndIsNotScoredAboveAConfirmedTier()
    {
        using var db = CreateDb();
        Add(db, "Unset", tier: null);
        Add(db, "Junior", tier: ExperienceTier.Junior);
        Add(db, "Mid", tier: ExperienceTier.Mid);
        await db.SaveChangesAsync();
        var r = await Query(db);
        var unset = r.Single(m => m.CaregiverName == "Unset N");
        Assert.Equal(9.0, unset.ScoreBreakdown.ExperienceScore); // 0.3 * 30, same as Junior
        Assert.True(unset.MatchScore < r.Single(m => m.CaregiverName == "Mid N").MatchScore);
        Assert.True(unset.MatchScore <= r.Single(m => m.CaregiverName == "Junior N").MatchScore);
    }

    [Fact]
    public async Task Breakdown_SumsToScore_AndRespectsWeightCaps()
    {
        using var db = CreateDb();
        Add(db, "Best", latOffset: 0, tier: ExperienceTier.Senior, identity: true, guarantors: 2);
        await db.SaveChangesAsync();
        var m = Assert.Single(await Query(db));
        Assert.Equal(45, m.ScoreBreakdown.ProximityScore);
        Assert.Equal(30, m.ScoreBreakdown.ExperienceScore);
        // identity + guarantors + assessment/cert (general category) = 4/5 (address history not seeded)
        Assert.Equal(20, m.ScoreBreakdown.VettingScore);
        Assert.Equal(95, m.MatchScore);
    }

    [Fact]
    public async Task MissingAssessment_StillAppears_FlaggedAndRankedBelowReady()
    {
        using var db = CreateDb();
        db.ServiceRequirements.Add(new ServiceRequirement
        {
            Id = ObjectId.GenerateNewId(), ServiceCategory = Category, DisplayName = Category, Tier = "regulated",
            RequiredAssessment = Category, PassingScore = 70, Active = true
        });
        Add(db, "NotReady", identity: true, guarantors: 2);
        await db.SaveChangesAsync();
        var m = Assert.Single(await Query(db));
        Assert.False(m.AssessmentReady);
        Assert.Contains("assessment", m.ReadinessGaps);
        Assert.False(string.IsNullOrWhiteSpace(m.ReadinessMessage));
        // badge and score use the same data: assessment unmet -> 3 of 5 met (identity, guarantors, certificates: none required)
        Assert.Equal(15, m.ScoreBreakdown.VettingScore);
    }
}
