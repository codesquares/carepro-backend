using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Helpers;
using Infrastructure.Content.Services;
using Infrastructure.Content.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Phase 2 caregiver-vetting model. Each numbered section below is verified
/// independently against a real local MongoDB (same infra as
/// CaregiverReadinessAndSafeguardsTests — replica set on 127.0.0.1:27018).
/// </summary>
public class CaregiverVettingTests
{
    private static CareProDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", databaseName)
            .Options;
        return new TestCareProDbContext(options);
    }

    private static string NewDbName() => $"carepro_vetting_tests_{Guid.NewGuid():N}";

    private static Caregiver SeedCaregiver(CareProDbContext db)
    {
        var caregiver = new Caregiver
        {
            Id = ObjectId.GenerateNewId(),
            FirstName = "Vetting",
            LastName = "Subject",
            Email = $"vetting-{Guid.NewGuid():N}@example.com",
            Password = "x", // pragma: allowlist-secret
            Role = "Caregiver",
            Status = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow
        };
        db.CareGivers.Add(caregiver);
        db.SaveChanges();
        return caregiver;
    }

    private static CaregiverVettingService CreateService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<CaregiverVettingService>>());

    // ─────────────────────────── 2.1  CLASSIFICATION ───────────────────────────

    [Fact]
    public async Task SetClassification_RegisteredNurseWithSpecialty_PersistsAndReadsBackViaFreshContext()
    {
        var dbName = NewDbName();
        ObjectId caregiverId;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            caregiverId = caregiver.Id;
            var service = CreateService(db);

            var written = await service.SetClassificationAsync(caregiverId.ToString(),
                new SetCaregiverClassificationRequest { CaregiverType = "RegisteredNurse", Specialty = "Midwifery" });

            Assert.Equal("RegisteredNurse", written.CaregiverType);
            Assert.Equal("Midwifery", written.Specialty);
        }

        // Fresh context + fresh service against the same database — proves it persisted.
        using (var db = CreateDb(dbName))
        {
            var reload = await CreateService(db).GetClassificationAsync(caregiverId.ToString());
            Assert.Equal("RegisteredNurse", reload.CaregiverType);
            Assert.Equal("Midwifery", reload.Specialty);

            var raw = await db.CareGivers.FirstAsync(c => c.Id == caregiverId);
            Assert.Equal(CaregiverType.RegisteredNurse, raw.CaregiverType);
            Assert.Equal("Midwifery", raw.Specialty);
        }
    }

    [Fact]
    public async Task SetClassification_AuxiliaryNurseBlankSpecialty_StoresNullSpecialty()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var service = CreateService(db);

        var written = await service.SetClassificationAsync(caregiver.Id.ToString(),
            new SetCaregiverClassificationRequest { CaregiverType = "AuxiliaryNurse", Specialty = "   " });

        Assert.Equal("AuxiliaryNurse", written.CaregiverType);
        Assert.Null(written.Specialty);
    }

    [Fact]
    public async Task GetClassification_BeforeSet_ReturnsNulls()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);

        var result = await CreateService(db).GetClassificationAsync(caregiver.Id.ToString());

        Assert.Null(result.CaregiverType);
        Assert.Null(result.Specialty);
    }

    [Fact]
    public async Task SetClassification_ExistingQualificationsUntouched()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        db.CaregiverQualifications.Add(new CaregiverQualification
        {
            Id = ObjectId.GenerateNewId(),
            CaregiverId = caregiver.Id.ToString(),
            CertificationName = "BLS",
            IssuingOrganisation = "Red Cross",
            IssueMonth = 5,
            IssueYear = 2023,
            DoesNotExpire = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await CreateService(db).SetClassificationAsync(caregiver.Id.ToString(),
            new SetCaregiverClassificationRequest { CaregiverType = "CHEW" });

        var quals = await db.CaregiverQualifications.Where(q => q.CaregiverId == caregiver.Id.ToString()).ToListAsync();
        Assert.Single(quals);
        Assert.Equal("BLS", quals[0].CertificationName);
    }

    [Fact]
    public async Task SetClassification_InvalidType_Throws()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).SetClassificationAsync(
            caregiver.Id.ToString(),
            new SetCaregiverClassificationRequest { CaregiverType = "Doctor" }));
    }

    // ─────────────────── 9.1  EXPERIENCE TIER (payroll) ───────────────────

    [Fact]
    public async Task SetExperienceTier_Senior_PersistsAndReadsBackViaFreshContext()
    {
        var dbName = NewDbName();
        ObjectId caregiverId;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            caregiverId = caregiver.Id;
            var service = CreateService(db);

            var written = await service.SetExperienceTierAsync(caregiverId.ToString(),
                new SetCaregiverExperienceTierRequest { ExperienceTier = "Senior" });

            Assert.Equal("Senior", written.ExperienceTier);
        }

        // Fresh context + fresh service against the same database — proves it persisted.
        using (var db = CreateDb(dbName))
        {
            var reload = await CreateService(db).GetExperienceTierAsync(caregiverId.ToString());
            Assert.Equal("Senior", reload.ExperienceTier);

            var raw = await db.CareGivers.FirstAsync(c => c.Id == caregiverId);
            Assert.Equal(ExperienceTier.Senior, raw.ExperienceTier);
        }
    }

    [Fact]
    public async Task GetExperienceTier_BeforeSet_ReturnsNull()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);

        var result = await CreateService(db).GetExperienceTierAsync(caregiver.Id.ToString());

        Assert.Null(result.ExperienceTier);
    }

    [Fact]
    public async Task SetExperienceTier_InvalidTier_Throws()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).SetExperienceTierAsync(
            caregiver.Id.ToString(),
            new SetCaregiverExperienceTierRequest { ExperienceTier = "Expert" }));
    }

    [Fact]
    public async Task SetExperienceTier_DoesNotTouchClassification()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var service = CreateService(db);

        await service.SetClassificationAsync(caregiver.Id.ToString(),
            new SetCaregiverClassificationRequest { CaregiverType = "RegisteredNurse", Specialty = "Midwifery" });
        await service.SetExperienceTierAsync(caregiver.Id.ToString(),
            new SetCaregiverExperienceTierRequest { ExperienceTier = "Mid" });

        var classification = await service.GetClassificationAsync(caregiver.Id.ToString());
        Assert.Equal("RegisteredNurse", classification.CaregiverType);
        Assert.Equal("Midwifery", classification.Specialty);
    }

    [Fact]
    public async Task SetClassification_UnknownCaregiver_ThrowsKeyNotFound()
    {
        using var db = CreateDb(NewDbName());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(db).SetClassificationAsync(
            ObjectId.GenerateNewId().ToString(),
            new SetCaregiverClassificationRequest { CaregiverType = "CHEW" }));
    }

    // ─────────────────────────── 2.2  GUARANTORS ───────────────────────────

    private static ITokenHandler RealTokenHandler()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "12345678901234567890123456789012", // pragma: allowlist-secret
            ["JwtSettings:Issuer"] = "carepro-tests",
            ["JwtSettings:Audience"] = "carepro-tests-audience"
        }).Build();
        return new TokenHandler(config);
    }

    private sealed class GuarantorHarness
    {
        public required GuarantorService Service { get; init; }
        public required List<string> SentLinks { get; init; }
        public required List<(string Type, string RelatedEntityId)> AdminAlerts { get; init; }
        public required Mock<MediatR.IMediator> Mediator { get; init; }
    }

    private static GuarantorHarness CreateGuarantorService(CareProDbContext db)
    {
        var sentLinks = new List<string>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendGuarantorConfirmationEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, _, link) => sentLinks.Add(link))
            .Returns(Task.CompletedTask);

        var origin = new Mock<IOriginValidationService>();
        origin.Setup(o => o.IsFrontendOrigin(It.IsAny<string>())).Returns(false);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FrontendUrl"] = "https://oncarepro.com"
        }).Build();

        var adminAlerts = new List<(string Type, string RelatedEntityId)>();
        var ops = new Mock<IOpsAlertService>();
        ops.Setup(o => o.NotifyAdminsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, string, string, bool>((type, _, _, related, _) => adminAlerts.Add((type, related)))
            .Returns(Task.CompletedTask);
        var mediator = new Mock<MediatR.IMediator>();

        var service = new GuarantorService(
            db, RealTokenHandler(), email.Object, config, origin.Object,
            Mock.Of<ILogger<GuarantorService>>(), ops.Object, mediator.Object);

        return new GuarantorHarness { Service = service, SentLinks = sentLinks, AdminAlerts = adminAlerts, Mediator = mediator };
    }

    private static string TokenFromLink(string link)
    {
        var q = new Uri(link).Query.TrimStart('?');
        var token = q.Split('&').First(p => p.StartsWith("token=")).Substring("token=".Length);
        return Uri.UnescapeDataString(token);
    }

    private static AddGuarantorRequest SampleGuarantor(string suffix) => new()
    {
        Name = $"Guarantor {suffix}",
        RelationshipToCaregiver = "Former employer",
        RelationshipCategory = "former_employer_or_client",
        YearsKnown = 5,
        PhoneNo = "+2348012345678",
        Email = $"guarantor-{suffix}-{Guid.NewGuid():N}@example.com",
        Address = "12 Marina Road, Lagos"
    };

    /// <summary>Answers that agree with <see cref="SampleGuarantor"/> and the seeded "Vetting Subject" caregiver.</summary>
    private static SubmitGuarantorResponsesRequest GoodAnswers() => new()
    {
        CaregiverName = "Vetting Subject",
        RelationshipCategory = "former_employer_or_client",
        YearsKnown = 5,
        LastContact = "this_week",
        CaregiverArea = "Yaba, Lagos",
        HowTheyKnow = string.Join(' ', Enumerable.Repeat("worked", 30)),
        TrustAnswer = "yes",
        Declaration = true,
    };

    [Fact]
    public async Task Guarantors_ExactlyTwoAllowed_ThirdRejected()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var harness = CreateGuarantorService(db);
        var cgId = caregiver.Id.ToString();

        var g1 = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));
        var g2 = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("B"));

        Assert.Equal("pending", g1.Status);
        Assert.Equal("pending", g2.Status);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("C")));
        Assert.Contains("at most 2", ex.Message);

        var all = (await harness.Service.GetGuarantorsAsync(cgId)).ToList();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Guarantor_ConfirmationLink_FlipsStatusToConfirmed()
    {
        var dbName = NewDbName();
        string cgId;
        string guarantorId;
        string token;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var harness = CreateGuarantorService(db);

            var g1 = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));
            await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("B"));
            guarantorId = g1.Id;

            var afterSend = await harness.Service.SendConfirmationLinkAsync(cgId, guarantorId, origin: null);
            Assert.Equal(1, afterSend.AttemptCount);
            Assert.NotNull(afterSend.LastAttemptAt);
            Assert.NotNull(afterSend.CooldownUntil);

            Assert.Single(harness.SentLinks);
            Assert.StartsWith("https://oncarepro.com/guarantor-confirmation?token=", harness.SentLinks[0]);
            token = TokenFromLink(harness.SentLinks[0]);
        }

        // Guarantor clicks the link — fresh context, no auth, token is the authorisation.
        using (var db = CreateDb(dbName))
        {
            var harness = CreateGuarantorService(db);
            var result = await harness.Service.SubmitResponsesAsync(token, GoodAnswers());

            Assert.True(result.Success);
            Assert.Equal(guarantorId, result.GuarantorId);
        }

        // Persisted?
        using (var db = CreateDb(dbName))
        {
            var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
            Assert.Equal("confirmed", raw.Status);
            Assert.NotNull(raw.VerifiedAt);
            Assert.Null(raw.CooldownUntil);

            var harness = CreateGuarantorService(db);
            // Second click is idempotent, not an error.
            var again = await harness.Service.SubmitResponsesAsync(token, GoodAnswers());
            Assert.True(again.Success);
        }
    }

    [Fact]
    public async Task Guarantor_SendConfirmation_RespectsCooldown()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var harness = CreateGuarantorService(db);
        var cgId = caregiver.Id.ToString();

        var g1 = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));

        await harness.Service.SendConfirmationLinkAsync(cgId, g1.Id, origin: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.SendConfirmationLinkAsync(cgId, g1.Id, origin: null));
        Assert.Contains("recently", ex.Message);
    }

    [Fact]
    public async Task Guarantor_ConfirmByToken_InvalidToken_ReturnsFailureNotThrow()
    {
        using var db = CreateDb(NewDbName());
        var harness = CreateGuarantorService(db);

        var result = await harness.Service.SubmitResponsesAsync("not-a-real-token", GoodAnswers());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Guarantor_Update_BlockedAfterConfirmed()
    {
        var dbName = NewDbName();
        string cgId, guarantorId, token;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var harness = CreateGuarantorService(db);
            var g1 = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));
            guarantorId = g1.Id;
            await harness.Service.SendConfirmationLinkAsync(cgId, guarantorId, origin: null);
            token = TokenFromLink(harness.SentLinks[0]);
        }

        using (var db = CreateDb(dbName))
        {
            await CreateGuarantorService(db).Service.SubmitResponsesAsync(token, GoodAnswers());
        }

        using (var db = CreateDb(dbName))
        {
            var harness = CreateGuarantorService(db);
            var req = new UpdateGuarantorRequest
            {
                Name = "Changed", RelationshipToCaregiver = "Friend",
                PhoneNo = "+2348011111111", Email = "changed@example.com", Address = "New address"
            };
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Service.UpdateGuarantorAsync(cgId, guarantorId, req));
        }
    }

    // ─────────────────────────── 2.3  ADDRESS HISTORY ───────────────────────────

    [Fact]
    public async Task AddressHistory_TwoRecords_StoredAndReadBackViaFreshContext()
    {
        var dbName = NewDbName();
        string cgId;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var service = CreateService(db);

            await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
            {
                Address = "10 Old Street, Ibadan",
                MovedIn = DateTime.UtcNow.AddYears(-6),
                MovedOut = DateTime.UtcNow.AddYears(-2)
            });
            await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
            {
                Address = "22 New Avenue, Lagos",
                MovedIn = DateTime.UtcNow.AddYears(-2),
                MovedOut = null
            });
        }

        using (var db = CreateDb(dbName))
        {
            var records = (await CreateService(db).GetAddressHistoryAsync(cgId)).ToList();
            Assert.Equal(2, records.Count);
            // Current address (null MovedOut) sorts first.
            Assert.Equal("22 New Avenue, Lagos", records[0].Address);
            Assert.Null(records[0].MovedOut);
            Assert.Equal("10 Old Street, Ibadan", records[1].Address);
            Assert.NotNull(records[1].MovedOut);
        }
    }

    [Fact]
    public async Task AddressHistory_Coverage_CompleteWhenTwoAddressesSpanFiveYears()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        // Bill enforcement already reached, so the approved-bill part of the rule applies.
        var service = new CaregiverVettingService(db, Mock.Of<ILogger<CaregiverVettingService>>(), null,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AddressProof:EnforcementDate"] = "2020-01-01" }).Build());

        var before = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.False(before.IsComplete);
        Assert.Equal(0, before.RecordCount);

        await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
        {
            Address = "10 Old Street",
            MovedIn = DateTime.UtcNow.AddYears(-6),
            MovedOut = DateTime.UtcNow.AddYears(-3)
        });

        var partial = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.False(partial.IsComplete); // only 1 record, and a gap to now

        await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
        {
            Address = "22 New Avenue",
            MovedIn = DateTime.UtcNow.AddYears(-3),
            MovedOut = null
        });

        // Dates now cover five years, but with no approved bill for the current address the step is NOT complete.
        var datesOnly = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.False(datesOnly.IsComplete);
        Assert.True(datesOnly.DatesCovered);
        Assert.Equal("incomplete", datesOnly.State);

        var current = await db.CaregiverAddressHistories.FirstAsync(a => a.CaregiverId == cgId && a.MovedOut == null);
        current.ProofStatus = AddressProofStatuses.Pending;
        await db.SaveChangesAsync();
        Assert.Equal("awaiting_review", (await service.GetAddressHistoryCoverageAsync(cgId)).State);

        current.ProofStatus = AddressProofStatuses.Approved;
        await db.SaveChangesAsync();
        var complete = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.True(complete.IsComplete);
        Assert.Equal("complete", complete.State);
        Assert.Equal(2, complete.RecordCount);
        Assert.Equal(5, complete.RequiredYears);
    }

    [Fact]
    public async Task AddressHistory_Coverage_IncompleteWhenGapTooLarge()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        var service = CreateService(db);

        await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
        {
            Address = "A", MovedIn = DateTime.UtcNow.AddYears(-6), MovedOut = DateTime.UtcNow.AddYears(-4)
        });
        await service.AddAddressHistoryAsync(cgId, new AddCaregiverAddressHistoryRequest
        {
            Address = "B", MovedIn = DateTime.UtcNow.AddYears(-1), MovedOut = null
        });

        var coverage = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.False(coverage.IsComplete); // 3-year gap between -4y and -1y
    }

    [Fact]
    public async Task AddressHistory_MovedOutBeforeMovedIn_Rejected()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAddressHistoryAsync(
            caregiver.Id.ToString(), new AddCaregiverAddressHistoryRequest
            {
                Address = "X",
                MovedIn = DateTime.UtcNow.AddYears(-1),
                MovedOut = DateTime.UtcNow.AddYears(-3)
            }));
    }

    // ─────────────────────────── 2.4  SOCIAL MEDIA HANDLES ───────────────────────────

    [Fact]
    public async Task SocialMedia_AddAndReadBackViaFreshContext()
    {
        var dbName = NewDbName();
        string cgId;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var service = CreateService(db);

            await service.AddSocialMediaHandleAsync(cgId,
                new AddCaregiverSocialMediaHandleRequest { Platform = "instagram", Handle = "@jane.care" });
            await service.AddSocialMediaHandleAsync(cgId,
                new AddCaregiverSocialMediaHandleRequest { Platform = "LinkedIn", Handle = "linkedin.com/in/jane" });
        }

        using (var db = CreateDb(dbName))
        {
            var handles = (await CreateService(db).GetSocialMediaHandlesAsync(cgId)).ToList();
            Assert.Equal(2, handles.Count);
            // Platform casing canonicalised to the allow-list entry.
            Assert.Contains(handles, h => h.Platform == "Instagram" && h.Handle == "@jane.care");
            Assert.Contains(handles, h => h.Platform == "LinkedIn");
        }
    }

    [Fact]
    public async Task SocialMedia_UnknownPlatform_Rejected()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(db).AddSocialMediaHandleAsync(
            caregiver.Id.ToString(),
            new AddCaregiverSocialMediaHandleRequest { Platform = "MySpace", Handle = "x" }));
    }

    [Fact]
    public async Task SocialMedia_UpdateAndDelete()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        var service = CreateService(db);

        var added = await service.AddSocialMediaHandleAsync(cgId,
            new AddCaregiverSocialMediaHandleRequest { Platform = "X", Handle = "@old" });

        var updated = await service.UpdateSocialMediaHandleAsync(cgId, added.Id,
            new UpdateCaregiverSocialMediaHandleRequest { Platform = "X", Handle = "@new" });
        Assert.Equal("@new", updated.Handle);

        await service.DeleteSocialMediaHandleAsync(cgId, added.Id);
        Assert.Empty(await service.GetSocialMediaHandlesAsync(cgId));
    }

    // ─────────────── FOLLOW-UP 1  STAFF GUARANTOR OVERRIDE ───────────────

    [Fact]
    public async Task AdminConfirmGuarantor_FlipsStatus_AttributesToAdmin_AndAuditLogs()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        var harness = CreateGuarantorService(db);

        var g = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));

        var result = await harness.Service.AdminConfirmGuarantorAsync(
            g.Id, adminId: "admin-42", adminEmail: "ops@carepro.com",
            reason: "Guarantor verified by phone; confirmation email bounced.");

        Assert.Equal("confirmed", result.Status);
        Assert.NotNull(result.VerifiedAt);
        Assert.Equal("staff_override", result.ConfirmationMethod);
        Assert.Equal("admin-42", result.ConfirmedByAdminId);
        Assert.Equal("ops@carepro.com", result.ConfirmedByAdminEmail);

        // Persisted
        var raw = await db.Guarantors.FirstAsync(x => x.Id == ObjectId.Parse(g.Id));
        Assert.Equal("confirmed", raw.Status);
        Assert.Equal("admin-42", raw.ConfirmedByAdminId);

        // Attributable audit trail
        var audit = await db.AdminAuditLogs
            .Where(a => a.TargetEntityId == g.Id && a.Action == "GuarantorManualConfirm")
            .ToListAsync();
        Assert.Single(audit);
        Assert.Equal("admin-42", audit[0].AdminId);
        Assert.Equal("ops@carepro.com", audit[0].AdminEmail);
        Assert.Equal(cgId, audit[0].TargetUserId);
        Assert.Contains("phone", audit[0].Reason);
        Assert.False(string.IsNullOrEmpty(audit[0].BeforeJson));
        Assert.False(string.IsNullOrEmpty(audit[0].AfterJson));
    }

    [Fact]
    public async Task AdminConfirmGuarantor_AlreadyConfirmedViaSelfServe_IsSafeNoOp()
    {
        var dbName = NewDbName();
        string cgId, guarantorId, token;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var harness = CreateGuarantorService(db);
            var g = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));
            guarantorId = g.Id;
            await harness.Service.SendConfirmationLinkAsync(cgId, guarantorId, origin: null);
            token = TokenFromLink(harness.SentLinks[0]);
        }
        using (var db = CreateDb(dbName))
        {
            await CreateGuarantorService(db).Service.SubmitResponsesAsync(token, GoodAnswers());
        }

        using (var db = CreateDb(dbName))
        {
            var harness = CreateGuarantorService(db);
            var result = await harness.Service.AdminConfirmGuarantorAsync(
                guarantorId, "admin-9", "ops@carepro.com", "Double-checking this one.");

            // No-op: original self-serve confirmation and (null) attribution left intact.
            Assert.Equal("confirmed", result.Status);
            Assert.Equal("self_serve", result.ConfirmationMethod);
            Assert.Null(result.ConfirmedByAdminId);

            var audit = await db.AdminAuditLogs
                .Where(a => a.TargetEntityId == guarantorId && a.Action == "GuarantorManualConfirm")
                .ToListAsync();
            Assert.Empty(audit);
        }
    }

    [Fact]
    public async Task SelfServeConfirm_AfterStaffOverride_IsSafeNoOp()
    {
        var dbName = NewDbName();
        string cgId, guarantorId, token;

        using (var db = CreateDb(dbName))
        {
            var caregiver = SeedCaregiver(db);
            cgId = caregiver.Id.ToString();
            var harness = CreateGuarantorService(db);
            var g = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));
            guarantorId = g.Id;
            await harness.Service.SendConfirmationLinkAsync(cgId, guarantorId, origin: null);
            token = TokenFromLink(harness.SentLinks[0]);
        }
        using (var db = CreateDb(dbName))
        {
            await CreateGuarantorService(db).Service.AdminConfirmGuarantorAsync(
                guarantorId, "admin-1", "ops@carepro.com", "Verified by phone.");
        }

        using (var db = CreateDb(dbName))
        {
            var harness = CreateGuarantorService(db);
            var result = await harness.Service.SubmitResponsesAsync(token, GoodAnswers());
            Assert.True(result.Success);

            var raw = await db.Guarantors.FirstAsync(x => x.Id == ObjectId.Parse(guarantorId));
            Assert.Equal("staff_override", raw.ConfirmationMethod); // override attribution preserved
            Assert.Equal("admin-1", raw.ConfirmedByAdminId);
        }
    }

    [Fact]
    public async Task AdminConfirmGuarantor_ShortReason_Throws()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var harness = CreateGuarantorService(db);
        var g = await harness.Service.AddGuarantorAsync(caregiver.Id.ToString(), SampleGuarantor("A"));

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.AdminConfirmGuarantorAsync(
            g.Id, "admin-1", "ops@carepro.com", "no"));
    }

    [Fact]
    public async Task AdminResendConfirmation_BypassesCooldown_AndAuditLogs()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        var harness = CreateGuarantorService(db);
        var g = await harness.Service.AddGuarantorAsync(cgId, SampleGuarantor("A"));

        await harness.Service.SendConfirmationLinkAsync(cgId, g.Id, origin: null);
        // Caregiver is now in cooldown.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.SendConfirmationLinkAsync(cgId, g.Id, origin: null));

        // Staff can still resend.
        var afterResend = await harness.Service.AdminResendConfirmationLinkAsync(
            g.Id, "admin-7", "ops@carepro.com", origin: null);
        Assert.Equal(2, afterResend.AttemptCount);
        Assert.Equal(2, harness.SentLinks.Count);

        var audit = await db.AdminAuditLogs
            .Where(a => a.TargetEntityId == g.Id && a.Action == "GuarantorLinkResendByStaff")
            .ToListAsync();
        Assert.Single(audit);
        Assert.Equal("admin-7", audit[0].AdminId);
    }

    // ─────────────── FOLLOW-UP 2  SNAPSHOT VETTING SIGNALS ───────────────

    private static CaregiverSnapshotService CreateSnapshotService(CareProDbContext db)
        => new(db, Mock.Of<ILogger<CaregiverSnapshotService>>());

    private static void SeedVettingComplete(CareProDbContext db, Caregiver caregiver)
    {
        caregiver.CaregiverType = CaregiverType.RegisteredNurse;
        caregiver.Specialty = "Midwifery";
        for (var i = 0; i < 2; i++)
        {
            db.Guarantors.Add(new Guarantor
            {
                Id = ObjectId.GenerateNewId(),
                CaregiverId = caregiver.Id.ToString(),
                Name = $"G{i}",
                RelationshipToCaregiver = "Colleague",
                PhoneNo = "+2348000000000",
                Email = $"g{i}-{Guid.NewGuid():N}@example.com",
                Address = "1 Street",
                Status = GuarantorStatuses.Confirmed,
                VerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory
        {
            Id = ObjectId.GenerateNewId(), CaregiverId = caregiver.Id.ToString(),
            Address = "Old", MovedIn = DateTime.UtcNow.AddYears(-6), MovedOut = DateTime.UtcNow.AddYears(-2),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.CaregiverAddressHistories.Add(new CaregiverAddressHistory
        {
            Id = ObjectId.GenerateNewId(), CaregiverId = caregiver.Id.ToString(),
            Address = "New", MovedIn = DateTime.UtcNow.AddYears(-2), MovedOut = null, ProofStatus = AddressProofStatuses.Approved,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task Snapshot_ReflectsVettingSignals_ForCompleteAndPartialCaregivers()
    {
        using var db = CreateDb(NewDbName());

        var complete = SeedCaregiver(db);
        SeedVettingComplete(db, complete);
        var partial = SeedCaregiver(db); // nothing vetting-related
        await db.SaveChangesAsync();

        await CreateSnapshotService(db).RebuildAllSnapshotsAsync();

        var completeSnap = await db.CaregiverJourneySnapshots.FirstAsync(s => s.CaregiverId == complete.Id.ToString());
        Assert.Equal(2, completeSnap.GuarantorCount.GetValueOrDefault());
        Assert.Equal(2, completeSnap.ConfirmedGuarantorCount.GetValueOrDefault());
        Assert.True(completeSnap.HasTwoConfirmedGuarantors.GetValueOrDefault());
        Assert.True(completeSnap.AddressHistoryComplete.GetValueOrDefault());
        Assert.True(completeSnap.CaregiverTypeSet.GetValueOrDefault());
        Assert.Equal("RegisteredNurse", completeSnap.CaregiverType);

        var partialSnap = await db.CaregiverJourneySnapshots.FirstAsync(s => s.CaregiverId == partial.Id.ToString());
        Assert.Equal(0, partialSnap.GuarantorCount.GetValueOrDefault());
        Assert.False(partialSnap.HasTwoConfirmedGuarantors.GetValueOrDefault());
        Assert.False(partialSnap.AddressHistoryComplete.GetValueOrDefault());
        Assert.False(partialSnap.CaregiverTypeSet.GetValueOrDefault());
        Assert.Null(partialSnap.CaregiverType);
    }

    [Fact]
    public async Task Snapshot_SecondRebuild_UpdatesVettingSignalsInPlace()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        await db.SaveChangesAsync();

        var snapshotService = CreateSnapshotService(db);
        await snapshotService.RebuildAllSnapshotsAsync();

        var first = await db.CaregiverJourneySnapshots.FirstAsync(s => s.CaregiverId == caregiver.Id.ToString());
        Assert.False(first.HasTwoConfirmedGuarantors.GetValueOrDefault());

        // Caregiver completes vetting, next rebuild picks it up (no cadence change).
        SeedVettingComplete(db, caregiver);
        await db.SaveChangesAsync();
        await snapshotService.RebuildAllSnapshotsAsync();

        var snaps = await db.CaregiverJourneySnapshots.Where(s => s.CaregiverId == caregiver.Id.ToString()).ToListAsync();
        Assert.Single(snaps); // updated in place, not duplicated
        Assert.True(snaps[0].HasTwoConfirmedGuarantors.GetValueOrDefault());
        Assert.True(snaps[0].AddressHistoryComplete.GetValueOrDefault());
        Assert.True(snaps[0].CaregiverTypeSet.GetValueOrDefault());
    }

    [Fact]
    public async Task Snapshot_QueryApi_ExposesVettingFieldsInDto()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        SeedVettingComplete(db, caregiver);
        await db.SaveChangesAsync();
        await CreateSnapshotService(db).RebuildAllSnapshotsAsync();

        var response = await CreateSnapshotService(db).GetSnapshotsAsync(new CaregiverSnapshotQuery { PageSize = 50 });
        var dto = response.Snapshots.First(s => s.CaregiverId == caregiver.Id.ToString());

        Assert.Equal(2, dto.ConfirmedGuarantorCount);
        Assert.True(dto.HasTwoConfirmedGuarantors);
        Assert.True(dto.AddressHistoryComplete);
        Assert.True(dto.CaregiverTypeSet);
        Assert.Equal("RegisteredNurse", dto.CaregiverType);
    }

    // ───────────────── Guarantor questionnaire (soft gate) ─────────────────

    /// <summary>Seeds a caregiver with one pending guarantor and returns what a submission needs.</summary>
    private static async Task<(string DbName, string CgId, string GuarantorId, string Token)> SeedPendingGuarantor(
        Func<AddGuarantorRequest, AddGuarantorRequest>? tweak = null)
    {
        var dbName = NewDbName();
        using var db = CreateDb(dbName);
        var caregiver = SeedCaregiver(db);
        var cgId = caregiver.Id.ToString();
        var harness = CreateGuarantorService(db);
        var req = SampleGuarantor("A");
        var g = await harness.Service.AddGuarantorAsync(cgId, tweak != null ? tweak(req) : req);
        await harness.Service.SendConfirmationLinkAsync(cgId, g.Id, origin: null);
        return (dbName, cgId, g.Id, TokenFromLink(harness.SentLinks[0]));
    }

    [Fact]
    public async Task Questionnaire_AllChecksPass_AutoConfirms_AndStoresAnswers()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);

        var result = await harness.Service.SubmitResponsesAsync(token, GoodAnswers());

        Assert.True(result.Success);
        var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
        Assert.Equal("confirmed", raw.Status);
        Assert.Equal("self_serve", raw.ConfirmationMethod);
        Assert.NotNull(raw.VerifiedAt);
        Assert.NotNull(raw.ResponsesSubmittedAt);
        Assert.Empty(raw.ReviewFlags!);
        Assert.Equal("Yaba, Lagos", raw.RespCaregiverArea); // stored for staff, never checked
        Assert.Empty(harness.AdminAlerts);
    }

    [Fact]
    public async Task Questionnaire_AreaIsNeverAutoChecked()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var answers = GoodAnswers();
        answers.CaregiverArea = "Somewhere nobody has ever heard of";

        await CreateGuarantorService(db).Service.SubmitResponsesAsync(token, answers);

        var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
        Assert.Equal("confirmed", raw.Status);
    }

    [Theory]
    [InlineData("name", "name_mismatch")]
    [InlineData("category", "relationship_mismatch")]
    [InlineData("years_far", "years_mismatch")]
    [InlineData("years_low", "years_below_minimum")]
    [InlineData("stale", "stale_contact")]
    [InlineData("trust", "trust_reservation")]
    [InlineData("phone", "phone_mismatch")]
    public async Task Questionnaire_AnyFlag_GoesToNeedsReview_WithSameResultWording(string scenario, string expectedFlag)
    {
        // years_low needs a caregiver claim that also sits low, so only the minimum check fires.
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor(
            scenario == "years_low" ? r => { r.YearsKnown = 3; return r; } : null);
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var answers = GoodAnswers();
        switch (scenario)
        {
            case "name": answers.CaregiverName = "Someone Else"; break;
            case "category": answers.RelationshipCategory = "neighbour"; break;
            case "years_far": answers.YearsKnown = 9; break;
            case "years_low": answers.YearsKnown = 1; break;
            case "stale": answers.LastContact = "over_year"; break;
            case "trust": answers.TrustAnswer = "yes_with_reservations"; answers.TrustExplanation = "Only met them twice."; break;
            case "phone": answers.PhoneNo = "+2349000000000"; break;
        }

        var result = await harness.Service.SubmitResponsesAsync(token, answers);

        Assert.True(result.Success);
        Assert.Equal("Thank you. Your responses have been recorded.", result.Message); // no outcome leak
        var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
        Assert.Equal("needs_review", raw.Status);
        Assert.Null(raw.VerifiedAt);
        Assert.Contains(expectedFlag, raw.ReviewFlags!);
        Assert.Contains(harness.AdminAlerts, a => a.Type == "guarantor_needs_review" && a.RelatedEntityId == guarantorId);
    }

    [Fact]
    public async Task Questionnaire_ConfirmedAndFlaggedResults_HaveIdenticalMessage()
    {
        var (dbName1, _, _, token1) = await SeedPendingGuarantor();
        var (dbName2, _, _, token2) = await SeedPendingGuarantor();
        using var db1 = CreateDb(dbName1);
        using var db2 = CreateDb(dbName2);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";

        var ok = await CreateGuarantorService(db1).Service.SubmitResponsesAsync(token1, GoodAnswers());
        var bad = await CreateGuarantorService(db2).Service.SubmitResponsesAsync(token2, flagged);

        Assert.Equal(ok.Message, bad.Message);
        Assert.Equal(ok.Success, bad.Success);
    }

    [Fact]
    public async Task Questionnaire_NameMatch_IgnoresCaseOrderAccentsMiddleNameAndOneTypo()
    {
        Assert.True(GuarantorResponseEvaluator.NameMatches("Vetting", "Subject", "subject vetting"));
        Assert.True(GuarantorResponseEvaluator.NameMatches("Vetting", "Subject", "Vetting Chidi Subject"));
        Assert.True(GuarantorResponseEvaluator.NameMatches("José", "Subject", "Jose Subject"));
        Assert.True(GuarantorResponseEvaluator.NameMatches("Vetting", "Subject", "Vettinng Subject")); // 1 typo on a long name
        Assert.False(GuarantorResponseEvaluator.NameMatches("Vetting", "Subject", "Vetting"));          // surname missing
        Assert.False(GuarantorResponseEvaluator.NameMatches("Ann", "Lee", "Amn Lee"));                  // short names get no typo allowance
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Questionnaire_PhoneMatch_ComparesLastTenDigits()
    {
        Assert.True(GuarantorResponseEvaluator.PhonesMatch("+2348012345678", "08012345678"));
        Assert.True(GuarantorResponseEvaluator.PhonesMatch("+234 801 234 5678", "0801-234-5678"));
        Assert.False(GuarantorResponseEvaluator.PhonesMatch("+2348012345678", "08012345679"));
        Assert.False(GuarantorResponseEvaluator.PhonesMatch("+2348012345678", ""));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Questionnaire_PreQuestionnaireGuarantor_NeverAutoConfirms()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using (var db = CreateDb(dbName))
        {
            // Simulate a guarantor created before the questionnaire existed.
            var g = await db.Guarantors.FirstAsync(x => x.Id == ObjectId.Parse(guarantorId));
            g.RelationshipCategory = null;
            g.YearsKnown = null;
            await db.SaveChangesAsync();
        }
        using (var db = CreateDb(dbName))
        {
            await CreateGuarantorService(db).Service.SubmitResponsesAsync(token, GoodAnswers());
            var raw = await db.Guarantors.FirstAsync(x => x.Id == ObjectId.Parse(guarantorId));
            Assert.Equal("needs_review", raw.Status);
            Assert.Contains("no_declared_data", raw.ReviewFlags!);
        }
    }

    [Fact]
    public async Task Questionnaire_SecondSubmission_IsNoOp_AndCannotUpgradeAFlaggedGuarantor()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        // The guarantor (or the caregiver, who may control the inbox) retries with the "right" answers.
        var retry = await harness.Service.SubmitResponsesAsync(token, GoodAnswers());

        Assert.True(retry.Success);
        var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
        Assert.Equal("needs_review", raw.Status);
        Assert.Equal("Wrong Person", raw.RespCaregiverName);
    }

    [Fact]
    public async Task Questionnaire_TooShortDescription_Rejected_WithoutStateChange()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var answers = GoodAnswers();
        answers.HowTheyKnow = "A good person.";

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateGuarantorService(db).Service.SubmitResponsesAsync(token, answers));

        var raw = await db.Guarantors.FirstAsync(g => g.Id == ObjectId.Parse(guarantorId));
        Assert.Equal("pending", raw.Status);
        Assert.Null(raw.ResponsesSubmittedAt);
    }

    [Fact]
    public async Task Questionnaire_ReservationWithoutExplanation_Rejected()
    {
        var (dbName, _, _, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var answers = GoodAnswers();
        answers.TrustAnswer = "no";
        answers.TrustExplanation = "  ";

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateGuarantorService(db).Service.SubmitResponsesAsync(token, answers));
    }

    [Fact]
    public async Task QuizInfo_ValidToken_ReturnsOnlyNameAndState_InvalidReturnsNull()
    {
        var (dbName, _, _, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);

        var info = await harness.Service.GetQuizInfoAsync(token);
        Assert.NotNull(info);
        Assert.Equal("Guarantor A", info!.GuarantorName);
        Assert.False(info.AlreadyResponded);
        // The type itself carries no declared data to leak.
        Assert.Equal(new[] { "AlreadyResponded", "GuarantorName" },
            typeof(GuarantorQuizInfo).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());

        await harness.Service.SubmitResponsesAsync(token, GoodAnswers());
        Assert.True((await harness.Service.GetQuizInfoAsync(token))!.AlreadyResponded);
        Assert.Null(await harness.Service.GetQuizInfoAsync("garbage"));
    }

    [Fact]
    public async Task CaregiverView_NeverExposesAnswersOrFlags()
    {
        var (dbName, cgId, _, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        var view = (await harness.Service.GetGuarantorsAsync(cgId)).Single();

        Assert.Equal("needs_review", view.Status);
        Assert.IsNotType<AdminGuarantorResponse>(view);
        Assert.False(typeof(GuarantorResponse).GetProperties().Any(p => p.Name.StartsWith("Resp") || p.Name == "ReviewFlags"));
    }

    [Fact]
    public async Task NeedsReviewGuarantor_CannotBeEditedOrResentALink()
    {
        var (dbName, cgId, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.UpdateGuarantorAsync(cgId, guarantorId, new UpdateGuarantorRequest
            {
                Name = "X Y", RelationshipToCaregiver = "Friend", RelationshipCategory = "friend", YearsKnown = 3,
                PhoneNo = "+2348012345678", Email = "x@example.com", Address = "Somewhere"
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.AdminResendConfirmationLinkAsync(guarantorId, "admin-1", "ops@carepro.com", null));
    }

    [Fact]
    public async Task Review_Approve_Confirms_WithStaffReviewAttribution_AndAudit()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        var listed = (await harness.Service.AdminGetNeedsReviewAsync()).Single();
        Assert.Equal("Vetting Subject", listed.CaregiverName);
        Assert.Contains("name_mismatch", listed.ReviewFlags!);

        var result = await harness.Service.AdminReviewGuarantorAsync(guarantorId, "admin-1", "ops@carepro.com",
            new AdminReviewGuarantorRequest { Decision = "approve", Note = "Called them, nickname." });

        Assert.Equal("confirmed", result.Status);
        Assert.Equal("staff_review", result.ConfirmationMethod);
        Assert.Equal("ops@carepro.com", result.ReviewedByAdminEmail);
        Assert.Empty(await harness.Service.AdminGetNeedsReviewAsync());
        Assert.Single(await db.AdminAuditLogs.Where(a => a.TargetEntityId == guarantorId && a.Action == "GuarantorReviewApproved").ToListAsync());
    }

    [Fact]
    public async Task Review_Reject_SetsRejected_AuditsAndNotifiesCaregiver()
    {
        var (dbName, cgId, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);
        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        var result = await harness.Service.AdminReviewGuarantorAsync(guarantorId, "admin-1", "ops@carepro.com",
            new AdminReviewGuarantorRequest { Decision = "reject", Note = "Could not verify." });

        Assert.Equal("rejected", result.Status);
        Assert.Null(result.VerifiedAt);
        Assert.Single(await db.AdminAuditLogs.Where(a => a.TargetEntityId == guarantorId && a.Action == "GuarantorReviewRejected").ToListAsync());
        harness.Mediator.Verify(m => m.Send(It.IsAny<Application.Commands.SendNotificationCommand>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);

        // A rejected guarantor does not count toward readiness.
        var all = (await harness.Service.GetGuarantorsAsync(cgId)).ToList();
        Assert.DoesNotContain(all, g => g.Status == "confirmed");
    }

    [Fact]
    public async Task Review_OnlyFromNeedsReview_AndNoteRequired()
    {
        var (dbName, _, guarantorId, token) = await SeedPendingGuarantor();
        using var db = CreateDb(dbName);
        var harness = CreateGuarantorService(db);

        // Still pending → cannot be reviewed.
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.AdminReviewGuarantorAsync(
            guarantorId, "admin-1", null, new AdminReviewGuarantorRequest { Decision = "approve", Note = "Looks fine." }));

        var flagged = GoodAnswers();
        flagged.CaregiverName = "Wrong Person";
        await harness.Service.SubmitResponsesAsync(token, flagged);

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.AdminReviewGuarantorAsync(
            guarantorId, "admin-1", null, new AdminReviewGuarantorRequest { Decision = "approve", Note = "ok" }));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.AdminReviewGuarantorAsync(
            guarantorId, "admin-1", null, new AdminReviewGuarantorRequest { Decision = "maybe", Note = "Not sure at all." }));

        await harness.Service.AdminReviewGuarantorAsync(guarantorId, "admin-1", null,
            new AdminReviewGuarantorRequest { Decision = "approve", Note = "Verified by phone." });
        // Already decided → cannot be decided again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.AdminReviewGuarantorAsync(
            guarantorId, "admin-2", null, new AdminReviewGuarantorRequest { Decision = "reject", Note = "Changed my mind." }));
    }

    [Fact]
    public async Task AddGuarantor_PersistsCategoryAndYearsKnown()
    {
        using var db = CreateDb(NewDbName());
        var caregiver = SeedCaregiver(db);
        var harness = CreateGuarantorService(db);

        var g = await harness.Service.AddGuarantorAsync(caregiver.Id.ToString(), SampleGuarantor("A"));

        Assert.Equal("former_employer_or_client", g.RelationshipCategory);
        Assert.Equal(5, g.YearsKnown);
    }
}
