using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Address proof (power-holding / NEPA bill for the CURRENT address): caregiver upload, staff review, and the rule that the
/// step is only complete once a bill is approved. Real Mongo (27018), fake private storage.
/// </summary>
public class AddressProofTests
{
    private static readonly AdminIdentity Admin = new() { Id = "admin-1", Email = "ops@carepro.test" };

    private sealed class Rig
    {
        public Infrastructure.Content.Data.CareProDbContext Db = null!;
        public string DbName = string.Empty;
        public FakeClearanceFileStore Store = new();
        public Mock<IAddressProofNotifier> Notifier = new();
        public FakeClock Clock = new() { UtcNowValue = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc) };
        public AddressProofService Caregiver = null!;
        public AddressProofAdminService Staff = null!;
        public Caregiver Cg = null!;
        public string CgId => Cg.Id.ToString();
        public CaregiverAddressHistory Past = null!;
        public CaregiverAddressHistory Current = null!;
    }

    private static Rig Build()
    {
        var name = PoliceClearanceTestSupport.NewDbName();
        var r = new Rig { DbName = name, Db = PoliceClearanceTestSupport.CreateDb(name) };
        r.Cg = PoliceClearanceTestSupport.SeedCaregiver(r.Db);
        var now = r.Clock.UtcNowValue;
        r.Past = new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = r.CgId, Address = "10 Old Street, Lagos", MovedIn = now.AddYears(-6), MovedOut = now.AddYears(-3), CreatedAt = now, UpdatedAt = now };
        r.Current = new CaregiverAddressHistory { Id = ObjectId.GenerateNewId(), CaregiverId = r.CgId, Address = "22 New Avenue, Lagos", MovedIn = now.AddYears(-3), MovedOut = null, CreatedAt = now, UpdatedAt = now };
        r.Db.CaregiverAddressHistories.AddRange(r.Past, r.Current);
        r.Db.SaveChanges();
        var cfg = new ConfigurationBuilder().Build();
        r.Caregiver = new AddressProofService(r.Db, r.Store, r.Notifier.Object, cfg, Mock.Of<ILogger<AddressProofService>>(), r.Clock);
        r.Staff = new AddressProofAdminService(r.Db, r.Store, r.Notifier.Object, cfg, r.Clock);
        return r;
    }

    private static SubmitAddressProofRequest Req(string billDate = "2026-10-01", byte[]? bytes = null, string ct = "application/pdf", string name = "ADDRTEST Subject") => new()
    {
        BillDate = billDate, NameOnBill = name,
        File = new ClearanceUploadFile { FileName = "nepa.pdf", ContentType = ct, Bytes = bytes ?? ClearanceHostFactory.PdfBytes() }
    };

    private static ApproveAddressProofRequest FullChecklist() => new()
    {
        Checklist = new AddressProofChecklistDTO { NameMatchesProfile = true, AddressMatchesEntry = true, BillWithinAllowedAge = true, ScanLegibleAndUnaltered = true }
    };

    private static Task<CaregiverAddressHistory> Reload(Rig r, ObjectId id)
    {
        using var fresh = PoliceClearanceTestSupport.CreateDb(r.DbName);
        return fresh.CaregiverAddressHistories.AsNoTracking().FirstAsync(a => a.Id == id);
    }

    // ───────────── the rule ─────────────

    [Fact]
    public async Task Everyone_IsFlippedToIncomplete_UntilTheCurrentAddressBillIsApproved()
    {
        var r = Build();
        var all = new List<CaregiverAddressHistory> { r.Past, r.Current };
        var now = r.Clock.UtcNowValue;

        // Two addresses cover five years — exactly the data that used to mean "complete".
        Assert.True(CaregiverAddressHistoryCoverage.HasDateCoverage(all, now));
        Assert.Equal(AddressVettingState.Incomplete, CaregiverAddressHistoryCoverage.Evaluate(all, now, true));
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(all, now, true));

        r.Current.ProofStatus = AddressProofStatuses.Pending;
        Assert.Equal(AddressVettingState.AwaitingReview, CaregiverAddressHistoryCoverage.Evaluate(all, now, true));
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(all, now, true));

        r.Current.ProofStatus = AddressProofStatuses.Rejected;
        Assert.Equal(AddressVettingState.Incomplete, CaregiverAddressHistoryCoverage.Evaluate(all, now, true));

        r.Current.ProofStatus = AddressProofStatuses.Approved;
        Assert.Equal(AddressVettingState.Complete, CaregiverAddressHistoryCoverage.Evaluate(all, now, true));
    }

    [Fact]
    public void ApprovedBillOnAPastAddress_DoesNotCount()
    {
        var r = Build();
        r.Past.ProofStatus = AddressProofStatuses.Approved;
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(new[] { r.Past, r.Current }, r.Clock.UtcNowValue, true));
    }

    [Fact]
    public void ApprovedBill_DoesNotRescueMissingDateCoverage()
    {
        var r = Build();
        r.Current.ProofStatus = AddressProofStatuses.Approved;
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(new[] { r.Current }, r.Clock.UtcNowValue, true)); // one record only
    }

    // ───────────── rollout: nobody flips before the enforcement date ─────────────

    [Fact]
    public void BeforeEnforcement_DatesAloneAreComplete_NobodyFlips_AndABillChangesNothing()
    {
        var r = Build();
        var all = new[] { r.Past, r.Current };
        var now = r.Clock.UtcNowValue;
        Assert.Equal(AddressVettingState.Complete, CaregiverAddressHistoryCoverage.Evaluate(all, now, false));
        r.Current.ProofStatus = AddressProofStatuses.Pending;
        Assert.Equal(AddressVettingState.Complete, CaregiverAddressHistoryCoverage.Evaluate(all, now, false));
        r.Current.ProofStatus = AddressProofStatuses.Rejected;
        Assert.Equal(AddressVettingState.Complete, CaregiverAddressHistoryCoverage.Evaluate(all, now, false));
        // Missing dates still fail, enforced or not.
        Assert.Equal(AddressVettingState.Incomplete, CaregiverAddressHistoryCoverage.Evaluate(new[] { r.Current }, now, false));
    }

    [Theory]
    [InlineData(null, false)]                 // unset: not enforced (fail open)
    [InlineData("garbage", false)]            // unparseable: not enforced
    [InlineData("2026-12-01", false)]         // future
    [InlineData("2026-10-24T12:00:00Z", true)] // exactly now
    [InlineData("2026-10-01", true)]          // past
    public void EnforcementDate_Parsing_AndBoundary(string? raw, bool enforced)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(raw == null ? new Dictionary<string, string?>() : new() { ["AddressProof:EnforcementDate"] = raw }).Build();
        Assert.Equal(enforced, Infrastructure.Content.Helpers.AddressProofSettings.Load(cfg).IsEnforced(new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task CoverageEndpoint_ReflectsEnforcement_ForTheSameData()
    {
        var r = Build();
        CaregiverVettingService With(string? date) => new(r.Db, Mock.Of<ILogger<CaregiverVettingService>>(), r.Store,
            new ConfigurationBuilder().AddInMemoryCollection(date == null ? new Dictionary<string, string?>() : new() { ["AddressProof:EnforcementDate"] = date }).Build());

        var before = await With(null).GetAddressHistoryCoverageAsync(r.CgId);
        Assert.True(before.IsComplete);
        Assert.False(before.ProofRequired);
        Assert.Equal("complete", before.State);

        var future = await With(DateTime.UtcNow.AddDays(30).ToString("o")).GetAddressHistoryCoverageAsync(r.CgId);
        Assert.True(future.IsComplete);
        Assert.NotNull(future.ProofEnforcementDate);

        var after = await With(DateTime.UtcNow.AddDays(-1).ToString("o")).GetAddressHistoryCoverageAsync(r.CgId);
        Assert.False(after.IsComplete);
        Assert.True(after.ProofRequired);
        Assert.True(after.DatesCovered);
        Assert.Equal("incomplete", after.State);

        // The per-address info also tells the UI whether the bill is required yet.
        var items = (await With(null).GetAddressHistoryAsync(r.CgId)).ToList();
        Assert.False(items.Single(i => i.Proof.Required).Proof.Enforced);
    }

    [Fact]
    public async Task VettingNoticeSteps_AfterEnforcement_SubmittedBillIsNotMissing_NoBillIs()
    {
        var r = Build();
        var all = new[] { r.Past, r.Current };
        var now = r.Clock.UtcNowValue;
        Assert.DoesNotContain("address-history", VettingNoticeService.MissingSteps(true, false, true, 2, all, "none", now, false));
        Assert.Contains("address-history", VettingNoticeService.MissingSteps(true, false, true, 2, all, "none", now, true));
        r.Current.ProofStatus = AddressProofStatuses.Pending;
        Assert.DoesNotContain("address-history", VettingNoticeService.MissingSteps(true, false, true, 2, all, "none", now, true));
        await Task.CompletedTask;
    }

    // ───────────── caregiver upload ─────────────

    [Fact]
    public async Task Submit_OnCurrentAddress_StoresPrivately_GoesPending_AlertsAdmins()
    {
        var r = Build();
        var info = await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        Assert.Equal("pending", info.Status);
        Assert.False(info.CanSubmit);
        Assert.Single(r.Store.Files);
        var saved = await Reload(r, r.Current.Id);
        Assert.Equal(AddressProofStatuses.Pending, saved.ProofStatus);
        Assert.Equal("ADDRTEST Subject", saved.ProofNameOnBill);
        Assert.Single(saved.ProofFiles!);
        r.Notifier.Verify(n => n.NotifySubmittedAsync(It.IsAny<CaregiverAddressHistory>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Submit_OnPastAddress_IsRefused_NothingStored()
    {
        var r = Build();
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Past.Id.ToString(), Req()));
        Assert.Empty(r.Store.Files);
    }

    [Fact]
    public async Task Submit_OnSomeoneElsesAddress_Is403_NothingStored()
    {
        var r = Build();
        var other = PoliceClearanceTestSupport.SeedCaregiver(r.Db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Caregiver.SubmitAsync(other.Id.ToString(), r.Current.Id.ToString(), Req()));
        Assert.Empty(r.Store.Files);
        Assert.Null((await Reload(r, r.Current.Id)).ProofStatus);
    }

    // today in Lagos = 2026-10-24; 3 months back = 2026-07-24
    [Theory]
    [InlineData("2026-07-24", true)]
    [InlineData("2026-10-24", true)]
    [InlineData("2026-07-23", false)]
    [InlineData("2026-10-25", false)]
    [InlineData("not a date", false)]
    public async Task BillDate_AgeRule_Boundaries(string date, bool accepted)
    {
        var r = Build();
        if (accepted) Assert.Equal("pending", (await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req(date))).Status);
        else
        {
            await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req(date)));
            Assert.Empty(r.Store.Files);
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/webp")]
    public async Task Submit_RejectsNonBillFiles(string contentType)
    {
        var r = Build();
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req(ct: contentType)));
        // Declared PDF but the bytes are not a PDF.
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req(bytes: new byte[] { 1, 2, 3, 4, 5, 6 })));
        Assert.Empty(r.Store.Files);
    }

    [Fact]
    public async Task Submit_WithoutFile_OrName_IsRefused()
    {
        var r = Build();
        var noFile = Req(); noFile.File = null;
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), noFile));
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req(name: " ")));
    }

    [Fact]
    public async Task Submit_WhilePendingOrApproved_IsAConflict()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        await Assert.ThrowsAsync<PoliceClearanceConflictException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req()));
        await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());
        await Assert.ThrowsAsync<PoliceClearanceConflictException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req()));
        Assert.Single(r.Store.Files);
    }

    [Fact]
    public async Task Submit_StorageFailure_LeavesTheRecordUntouched()
    {
        var r = Build();
        r.Store.FailUploads = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req()));
        Assert.Null((await Reload(r, r.Current.Id)).ProofStatus);
    }

    // ───────────── staff review ─────────────

    [Fact]
    public async Task Approve_RequiresEveryChecklistItem()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        var partial = FullChecklist(); partial.Checklist!.AddressMatchesEntry = false;
        var ex = await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, partial));
        Assert.Contains("address on the bill matches", ex.Message);
        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, new ApproveAddressProofRequest()));
        Assert.Equal(AddressProofStatuses.Pending, (await Reload(r, r.Current.Id)).ProofStatus);
    }

    [Fact]
    public async Task Approve_CompletesTheStep_NotifiesTheCaregiver_AndAuditsIt()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        var detail = await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());
        Assert.Equal("approved", detail.Status);
        Assert.False(detail.CanReview);

        var saved = await Reload(r, r.Current.Id);
        Assert.Equal("ops@carepro.test", saved.ProofReviewedByAdminEmail);
        Assert.True(CaregiverAddressHistoryCoverage.IsComplete(new[] { r.Past, saved }, r.Clock.UtcNowValue, true));
        r.Notifier.Verify(n => n.NotifyApprovedAsync(r.CgId, r.Current.Id.ToString()), Times.Once);
        Assert.Contains(await r.Db.AdminAuditLogs.ToListAsync(), a => a.Action == "AddressProofApproved" && a.TargetEntityId == r.Current.Id.ToString());
    }

    [Fact]
    public async Task Reject_NeedsAReason_ThenCaregiverSeesItAndMayResubmit_OldFileDeleted()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        var firstFile = (await Reload(r, r.Current.Id)).ProofFiles![0].PublicId!;

        await Assert.ThrowsAsync<PoliceClearanceValidationException>(() => r.Staff.RejectAsync(r.Current.Id.ToString(), Admin, "no"));
        var detail = await r.Staff.RejectAsync(r.Current.Id.ToString(), Admin, "The address on the bill is different from the one you entered.");
        Assert.Equal("rejected", detail.Status);
        r.Notifier.Verify(n => n.NotifyRejectedAsync(r.CgId, r.Current.Id.ToString(), It.Is<string>(s => s.Contains("different"))), Times.Once);

        // Rejected is not complete, and the caregiver may upload again.
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(new[] { r.Past, await Reload(r, r.Current.Id) }, r.Clock.UtcNowValue, true));
        var again = await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        Assert.Equal("pending", again.Status);
        Assert.Null(again.RejectionReason);
        Assert.Contains(firstFile, r.Store.Deleted);        // the superseded file was removed from private storage
        Assert.Single(r.Store.Files);
    }

    [Fact]
    public async Task SecondDecisionOnTheSameBill_IsAConflict_FirstWins()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());
        await Assert.ThrowsAsync<PoliceClearanceConflictException>(() => r.Staff.RejectAsync(r.Current.Id.ToString(), Admin, "Changed my mind about this."));
        Assert.Equal(AddressProofStatuses.Approved, (await Reload(r, r.Current.Id)).ProofStatus);
    }

    [Fact]
    public async Task ListAndPendingCount_OnlyCurrentAddressesWithABill_OldestFirst()
    {
        var r = Build();
        Assert.Equal(0, await r.Staff.PendingCountAsync());
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        Assert.Equal(1, await r.Staff.PendingCountAsync());
        var list = await r.Staff.ListAsync("pending", null, 1, 20);
        Assert.Contains(list.Items, i => i.Id == r.Current.Id.ToString() && i.Address.StartsWith("22 New"));
        await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());
        Assert.Equal(0, await r.Staff.PendingCountAsync());
        Assert.DoesNotContain(( await r.Staff.ListAsync("pending", null, 1, 20)).Items, i => i.Id == r.Current.Id.ToString());
    }

    [Fact]
    public async Task StaffFileView_IsAuditedFirst_AndStreamsTheBill()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        var file = await r.Staff.GetFileForStaffAsync(r.Current.Id.ToString(), Admin);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Contains(await r.Db.AdminAuditLogs.ToListAsync(), a => a.Action == "AddressProofFileViewed");
        // The owner can read their own; another caregiver cannot.
        Assert.NotEmpty((await r.Caregiver.GetOwnFileAsync(r.CgId, r.Current.Id.ToString())).Bytes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Caregiver.GetOwnFileAsync("someone-else", r.Current.Id.ToString()));
    }

    // ───────────── editing / deleting the address invalidates the bill ─────────────

    private static CaregiverVettingService Vetting(Rig r) =>
        new(r.Db, Mock.Of<ILogger<CaregiverVettingService>>(), r.Store, new ConfigurationBuilder().Build());

    [Fact]
    public async Task EditingTheAddressText_OrMoveInDate_RemovesTheApprovedBill_AndItsFile()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());

        var res = await Vetting(r).UpdateAddressHistoryAsync(r.CgId, r.Current.Id.ToString(),
            new UpdateCaregiverAddressHistoryRequest { Address = "99 Different Road, Lagos", MovedIn = r.Current.MovedIn, MovedOut = null });
        Assert.Equal("none", res.Proof.Status);
        Assert.True(res.Proof.CanSubmit);
        Assert.Empty(r.Store.Files);
        var saved = await Reload(r, r.Current.Id);
        Assert.Null(saved.ProofStatus);
        Assert.Null(saved.ProofReviewedByAdminEmail);
        Assert.False(CaregiverAddressHistoryCoverage.IsComplete(new[] { r.Past, saved }, r.Clock.UtcNowValue, true));
    }

    [Fact]
    public async Task EditingOnlyTheMoveOutDate_OrNothing_KeepsTheBill()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        var res = await Vetting(r).UpdateAddressHistoryAsync(r.CgId, r.Current.Id.ToString(),
            new UpdateCaregiverAddressHistoryRequest { Address = r.Current.Address, MovedIn = r.Current.MovedIn, MovedOut = null });
        Assert.Equal("pending", res.Proof.Status);
        Assert.Single(r.Store.Files);
    }

    [Fact]
    public async Task SavingTheFormUnchanged_WithADateOnlyMoveIn_KeepsTheApprovedBill()
    {
        // The edit form posts "yyyy-MM-dd" (midnight), while the stored MovedIn has a time-of-day. Same calendar day = no change.
        var r = Build();
        r.Current.MovedIn = new DateTime(2023, 10, 24, 5, 14, 59, DateTimeKind.Utc);
        await r.Db.SaveChangesAsync();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        await r.Staff.ApproveAsync(r.Current.Id.ToString(), Admin, FullChecklist());

        var res = await Vetting(r).UpdateAddressHistoryAsync(r.CgId, r.Current.Id.ToString(),
            new UpdateCaregiverAddressHistoryRequest { Address = "  " + r.Current.Address + " ", MovedIn = new DateTime(2023, 10, 24), MovedOut = null });
        Assert.Equal("approved", res.Proof.Status);
        Assert.Single(r.Store.Files);

        // A different calendar day IS a change.
        var changed = await Vetting(r).UpdateAddressHistoryAsync(r.CgId, r.Current.Id.ToString(),
            new UpdateCaregiverAddressHistoryRequest { Address = r.Current.Address, MovedIn = new DateTime(2023, 10, 25), MovedOut = null });
        Assert.Equal("none", changed.Proof.Status);
        Assert.Empty(r.Store.Files);
    }

    [Fact]
    public async Task DeletingTheAddress_DeletesTheBillFile()
    {
        var r = Build();
        await r.Caregiver.SubmitAsync(r.CgId, r.Current.Id.ToString(), Req());
        await Vetting(r).DeleteAddressHistoryAsync(r.CgId, r.Current.Id.ToString());
        Assert.Empty(r.Store.Files);
    }

    [Fact]
    public async Task ListResponse_ShowsProofOnlyAsRequiredForTheCurrentAddress()
    {
        var r = Build();
        var items = (await Vetting(r).GetAddressHistoryAsync(r.CgId)).ToList();
        Assert.False(items.Single(i => i.Address.StartsWith("10 Old")).Proof.Required);
        var cur = items.Single(i => i.Address.StartsWith("22 New")).Proof;
        Assert.True(cur.Required);
        Assert.True(cur.CanSubmit);
        Assert.Equal(3, cur.MaxBillAgeMonths);
    }
}
