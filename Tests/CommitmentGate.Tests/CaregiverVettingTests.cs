using Application.DTOs;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
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

        var service = new GuarantorService(
            db, RealTokenHandler(), email.Object, config, origin.Object,
            Mock.Of<ILogger<GuarantorService>>());

        return new GuarantorHarness { Service = service, SentLinks = sentLinks };
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
        PhoneNo = "+2348012345678",
        Email = $"guarantor-{suffix}-{Guid.NewGuid():N}@example.com",
        Address = "12 Marina Road, Lagos"
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
            var result = await harness.Service.ConfirmByTokenAsync(token);

            Assert.True(result.Success);
            Assert.True(result.NewlyConfirmed);
            Assert.Equal(guarantorId, result.GuarantorId);
            Assert.Equal(cgId, result.CaregiverId);
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
            var again = await harness.Service.ConfirmByTokenAsync(token);
            Assert.True(again.Success);
            Assert.False(again.NewlyConfirmed);
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

        var result = await harness.Service.ConfirmByTokenAsync("not-a-real-token");
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
            await CreateGuarantorService(db).Service.ConfirmByTokenAsync(token);
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
        var service = CreateService(db);

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

        var complete = await service.GetAddressHistoryCoverageAsync(cgId);
        Assert.True(complete.IsComplete);
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
            await CreateGuarantorService(db).Service.ConfirmByTokenAsync(token);
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
            var result = await harness.Service.ConfirmByTokenAsync(token);
            Assert.True(result.Success);
            Assert.False(result.NewlyConfirmed);

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
            Address = "New", MovedIn = DateTime.UtcNow.AddYears(-2), MovedOut = null,
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
}
