using System.Globalization;
using System.Text.Json;
using System.Threading;
using Application.Commands;
using Application.DTOs;
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
/// Package caregiver "can't attend" notices end to end: category-only client view (note privacy), staff workflow,
/// caregiver withdrawal (+ race), once-only 24h escalation, Lagos-midnight date rule. Real local MongoDB (127.0.0.1:27018).
/// </summary>
public class VisitAbsenceNoticeTests
{
    private const string SecretNote = "SECRET-NOTE call me on 08031234567 not through CarePro";

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now;
        public FixedClock(DateTimeOffset now) { Now = now; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public CareProDbContext Db = null!;
        public VisitAbsenceService Svc = null!;
        public FixedClock Clock = null!;
        public List<SendNotificationCommand> Sent = new();
        public List<(string to, string subject, string body)> Emails = new();
        public string CaregiverId = ObjectId.GenerateNewId().ToString(), ClientId = ObjectId.GenerateNewId().ToString();
        public string AssignmentId = "", PackageRequestId = "", ApprovedAdminId = "", PendingAdminId = "";
        public string AdminId = ObjectId.GenerateNewId().ToString();
        public string DbName = "";
        public VisitAbsenceService Make(CareProDbContext db) => new(db, Mediator(), EmailSvc(), Mock.Of<ILogger<VisitAbsenceService>>(), Clock);
        public IMediator Mediator()
        {
            var m = new Mock<IMediator>();
            m.Setup(x => x.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
                .Callback<IRequest<string>, CancellationToken>((c, _) => { lock (Sent) Sent.Add((SendNotificationCommand)c); }).ReturnsAsync("n");
            return m.Object;
        }
        public IEmailService EmailSvc()
        {
            var e = new Mock<IEmailService>();
            e.Setup(x => x.SendGenericNotificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Callback<string, string, string, string, bool, string?, string?>((to, _, subj, body, _, _, _) => { lock (Emails) Emails.Add((to, subj, body)); })
                .Returns(Task.CompletedTask);
            return e.Object;
        }
        public string LagosDate(int plusDays) =>
            TimeZoneInfo.ConvertTimeFromUtc(Clock.Now.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos")).Date
                .AddDays(plusDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static Rig Build(DateTimeOffset? now = null)
    {
        var name = PackageAssignmentCheckinAndTaskSheetTests.NewDbName();
        var r = new Rig { DbName = name, Db = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(name) };
        r.Clock = new FixedClock(now ?? new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero)); // 09:00 Lagos
        (r.AssignmentId, r.PackageRequestId) = PackageAssignmentCheckinAndTaskSheetTests.SeedAcceptedAssignmentAndContract(r.Db, r.CaregiverId, r.ClientId);
        r.Db.PackageRequests.Add(new PackageRequest { Id = ObjectId.Parse(r.PackageRequestId), ClientId = r.ClientId, PackageCategory = "Elderly Care", PackageTierLabel = "Standard", Status = "confirmed", CreatedAt = DateTime.UtcNow });
        r.Db.Clients.Add(new Client { Id = ObjectId.Parse(r.ClientId), FirstName = "Cli", LastName = "EntSurname", Email = "client@example.com", Role = "Client", Status = true });
        r.Db.CareGivers.Add(new Caregiver { Id = ObjectId.Parse(r.CaregiverId), FirstName = "Care", LastName = "Giver", Email = "cg@example.com" });
        var admin = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "a", LastName = "b", Email = $"a-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.Approved, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        var pending = new AdminUser { Id = ObjectId.GenerateNewId(), FirstName = "p", LastName = "b", Email = $"p-{Guid.NewGuid():N}@example.com", Password = "x", Role = "SuperAdmin", Department = AdminDepartments.Finance, Status = AdminUserStatus.PendingApproval, CreatedAt = DateTime.UtcNow }; // pragma: allowlist-secret
        r.Db.AdminUsers.AddRange(admin, pending);
        r.ApprovedAdminId = admin.Id.ToString(); r.PendingAdminId = pending.Id.ToString();
        r.Db.SaveChanges();
        r.Svc = r.Make(r.Db);
        return r;
    }

    private static CreateVisitAbsenceRequest Req(string date, string category = "illness", string? note = SecretNote) => new() { Date = date, Category = category, Note = note };
    private Task<VisitAbsenceNoticeDTO> Give(Rig r, int plusDays = 5, string? note = SecretNote) => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(plusDays), "illness", note), r.CaregiverId);
    private static SetAbsenceStatusRequest St(string s, string note = "staff note") => new() { Status = s, Note = note };

    // ───────────── creation + note privacy ─────────────

    [Fact]
    public async Task Create_NotifiesClientAndApprovedAdmins_ClientSidesNeverContainTheNote_AdminSidesLinkToTheNotice()
    {
        var r = Build();
        var dto = await Give(r);

        Assert.Equal("illness", dto.Category);
        Assert.Equal(SecretNote, dto.Note); // the giving caregiver sees their own note
        Assert.Equal(2, r.Sent.Count);
        var client = r.Sent.Single(n => n.RecipientId == r.ClientId);
        var admin = r.Sent.Single(n => n.RecipientId == r.ApprovedAdminId);
        Assert.DoesNotContain(r.PendingAdminId, r.Sent.Select(n => n.RecipientId));
        Assert.Equal(r.PackageRequestId, client.RelatedEntityId);
        Assert.Equal(dto.Id, admin.RelatedEntityId);                         // admin deep link opens the notice
        Assert.Contains("Illness", client.Content);
        Assert.DoesNotContain("SECRET-NOTE", client.Content + client.Title);
        Assert.DoesNotContain("08031234567", client.Content + admin.Content); // the note isn't copied into any notification
        var mail = Assert.Single(r.Emails);
        Assert.Equal("client@example.com", mail.to);
        Assert.DoesNotContain("SECRET-NOTE", mail.subject + mail.body);
        Assert.DoesNotContain("08031234567", mail.body);
        Assert.Contains("Illness", mail.body);
    }

    [Fact]
    public async Task ClientPayload_HasDateAndCategoryOnly_AdminPayloadHasTheFullNote()
    {
        var r = Build();
        await Give(r);

        var clientJson = JsonSerializer.Serialize(await r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId));
        Assert.DoesNotContain("SECRET-NOTE", clientJson);
        Assert.DoesNotContain("08031234567", clientJson);
        using (var doc = JsonDocument.Parse(clientJson))
        {
            var props = doc.RootElement[0].EnumerateObject().Select(p => p.Name).ToHashSet();
            foreach (var forbidden in new[] { "Note", "Reason", "StatusNote", "StatusChangedByAdminId", "CaregiverId", "AssignmentId" })
                Assert.DoesNotContain(forbidden, props);
            Assert.Contains("Date", props); Assert.Contains("Category", props);
        }

        var adminJson = JsonSerializer.Serialize(await r.Svc.ListForAdminAsync(null, null));
        Assert.Contains("SECRET-NOTE", adminJson);
        Assert.Contains("illness", adminJson);
    }

    [Fact]
    public async Task Create_CategoryRequired_NoteOptionalAndCapped()
    {
        var r = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(5), "  ", "x"), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(5), "holiday", "x"), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(5), "illness", new string('x', 501)), r.CaregiverId));
        var ok = await r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(5), "transport", null), r.CaregiverId);
        Assert.Null(ok.Note);
        Assert.Equal("Transport problem", ok.CategoryLabel);
    }

    [Fact]
    public async Task LegacyNotice_NoCategoryNoStatus_ReadsAsOtherAndOpen_EverywhereWithoutCrashing()
    {
        var r = Build();
        // Insert the way v1 stored it: only the original fields, so Category/Status/etc. are absent from the document.
        var id = ObjectId.GenerateNewId();
        var coll = new MongoDB.Driver.MongoClient("mongodb://127.0.0.1:27018").GetDatabase(r.DbName).GetCollection<BsonDocument>("VisitAbsenceNotices");
        await coll.InsertOneAsync(new BsonDocument {
            { "_id", id }, { "AssignmentId", r.AssignmentId }, { "PackageRequestId", r.PackageRequestId }, { "CaregiverId", r.CaregiverId },
            { "AbsenceDate", DateTime.SpecifyKind(DateTime.Parse(r.LagosDate(7)), DateTimeKind.Utc) }, { "Reason", "old free text" }, { "ShortNotice", false }, { "CreatedAt", DateTime.UtcNow } });

        var client = Assert.Single(await r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId));
        Assert.Equal("other", client.Category); Assert.Null(client.Outcome);
        var cg = Assert.Single(await r.Svc.GetForAssignmentAsync(r.AssignmentId, r.CaregiverId));
        Assert.Equal("open", cg.Status);
        var admin = Assert.Single(await r.Svc.ListForAdminAsync("open", null));
        Assert.Equal("other", admin.Category); Assert.Equal("old free text", admin.Note);
        // and staff can work it
        var updated = await r.Svc.SetStatusAsync(id.ToString(), St("closed"), r.AdminId);
        Assert.Equal("closed", updated.Status);
    }

    // ───────────── staff workflow ─────────────

    [Fact]
    public async Task SetStatus_RecordsAdminIdTimeNote_CreditedAndCoverNotifyClientOnce_ClosedAndNoActionSendNothing()
    {
        var r = Build();
        var n = await Give(r);
        r.Sent.Clear(); r.Emails.Clear();

        var credited = await r.Svc.SetStatusAsync(n.Id, St("credited", "manual credit to be applied"), r.AdminId);
        Assert.Equal("credited", credited.Status);
        Assert.Equal(r.AdminId, credited.StatusChangedByAdminId);
        Assert.NotNull(credited.StatusChangedAt);
        Assert.Equal("manual credit to be applied", credited.StatusNote);
        var note = Assert.Single(r.Sent);
        Assert.Equal(r.ClientId, note.RecipientId);
        Assert.DoesNotContain("manual credit", note.Content);       // the staff note is internal
        Assert.DoesNotContain("SECRET-NOTE", note.Content);
        Assert.Single(r.Emails);
        // setting the same status again can't re-notify
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.SetStatusAsync(n.Id, St("credited"), r.AdminId));
        Assert.Single(r.Sent);

        r.Sent.Clear(); r.Emails.Clear();
        await r.Svc.SetStatusAsync(n.Id, St("cover_arranged"), r.AdminId);
        Assert.Single(r.Sent); Assert.Single(r.Emails);
        r.Sent.Clear(); r.Emails.Clear();
        await r.Svc.SetStatusAsync(n.Id, St("no_action_needed"), r.AdminId);
        await r.Svc.SetStatusAsync(n.Id, St("closed"), r.AdminId);
        Assert.Empty(r.Sent); Assert.Empty(r.Emails);

        var detail = await r.Svc.GetForAdminAsync(n.Id);
        Assert.Equal(4, detail.History!.Count(h => h.Kind == "status"));
        Assert.All(detail.History!, h => Assert.Equal(r.AdminId, h.ActorId));
    }

    [Fact]
    public async Task SetStatus_Validation_AndWithdrawnIsTerminalForStaff()
    {
        var r = Build();
        var n = await Give(r);
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.SetStatusAsync(n.Id, St("paid_out"), r.AdminId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.SetStatusAsync(n.Id, St("closed", " "), r.AdminId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.SetStatusAsync(n.Id, St("closed", new string('x', 501)), r.AdminId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.SetStatusAsync(n.Id, St("closed"), ""));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Svc.SetStatusAsync(ObjectId.GenerateNewId().ToString(), St("closed"), r.AdminId));
        await r.Svc.SetStatusAsync(n.Id, St("withdrawn"), r.AdminId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.SetStatusAsync(n.Id, St("open"), r.AdminId));
    }

    [Fact]
    public async Task List_OpenFirst_ThenShortNotice_AndFilters_AndShowsFirstNameOnly()
    {
        var r = Build();
        var later = await Give(r, 9);
        var shortN = await Give(r, 0);          // today -> short notice
        var closed = await Give(r, 3);
        await r.Svc.SetStatusAsync(closed.Id, St("closed"), r.AdminId);

        var all = await r.Svc.ListForAdminAsync(null, null);
        Assert.Equal(new[] { shortN.Id, later.Id, closed.Id }, all.Select(x => x.Id).ToArray());
        Assert.Equal("Cli", all[0].ClientFirstName);
        Assert.DoesNotContain("EntSurname", JsonSerializer.Serialize(all));
        Assert.Equal("Care Giver", all[0].CaregiverName);
        Assert.Equal("Elderly Care — Standard", all[0].PackageName);
        Assert.Equal(new[] { shortN.Id }, (await r.Svc.ListForAdminAsync(null, true)).Select(x => x.Id).ToArray());
        Assert.Equal(new[] { closed.Id }, (await r.Svc.ListForAdminAsync("closed", null)).Select(x => x.Id).ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.ListForAdminAsync("bogus", null));
    }

    [Fact]
    public async Task Statuses_MoveNoMoneySheetOrAssignment()
    {
        var r = Build();
        var sheet = new TaskSheet { AssignmentId = r.AssignmentId, PackageRequestId = r.PackageRequestId, CaregiverId = r.CaregiverId, Status = "in-progress" };
        r.Db.TaskSheets.Add(sheet); r.Db.SaveChanges();
        var n = await Give(r);
        foreach (var s in new[] { "cover_arranged", "credited", "no_action_needed", "closed" }) await r.Svc.SetStatusAsync(n.Id, St(s), r.AdminId);
        Assert.Equal("in-progress", r.Db.TaskSheets.Single().Status);
        Assert.Equal(AssignmentStatuses.Accepted, r.Db.Assignments.Single().Status);
        Assert.Empty(r.Db.ClientWalletLedgers.ToList());
        Assert.Empty(r.Db.ClientWallets.ToList());
    }

    // ───────────── withdrawal ─────────────

    [Fact]
    public async Task Withdraw_OkOnce_NotifiesClientAndAdminsOnce_SecondWithdrawIs409_AndFreesTheDay()
    {
        var r = Build();
        var n = await Give(r);
        r.Sent.Clear(); r.Emails.Clear();

        var w = await r.Svc.WithdrawAsync(r.AssignmentId, n.Id, r.CaregiverId);
        Assert.Equal("withdrawn", w.Status);
        Assert.Equal(2, r.Sent.Count);
        Assert.Contains(r.Sent, x => x.RecipientId == r.ClientId);
        Assert.Contains(r.Sent, x => x.RecipientId == r.ApprovedAdminId);
        Assert.Single(r.Emails);
        var raw = r.Db.VisitAbsenceNotices.Single();
        Assert.NotNull(raw.WithdrawnAt); Assert.Equal(r.CaregiverId, raw.WithdrawnById);

        r.Sent.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.WithdrawAsync(r.AssignmentId, n.Id, r.CaregiverId)); // -> HTTP 409
        Assert.Empty(r.Sent);

        // the day is free again for a corrected notice
        var again = await Give(r);
        Assert.Equal("open", again.Status);
        Assert.Equal("withdrawn", (await r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId)).Single(x => x.Id == n.Id).Outcome);
    }

    [Fact]
    public async Task Withdraw_Refused_AfterCreditOrCover_OnOrAfterTheDay_ForeignCaregiver_WrongAssignment()
    {
        var r = Build();
        var n = await Give(r);
        var stranger = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.WithdrawAsync(r.AssignmentId, n.Id, stranger));          // -> 403
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Svc.WithdrawAsync(r.AssignmentId, ObjectId.GenerateNewId().ToString(), r.CaregiverId)); // -> 404

        await r.Svc.SetStatusAsync(n.Id, St("credited"), r.AdminId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.WithdrawAsync(r.AssignmentId, n.Id, r.CaregiverId)); // -> 409
        Assert.Contains("only be withdrawn by staff", ex.Message);
        await r.Svc.SetStatusAsync(n.Id, St("cover_arranged"), r.AdminId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.WithdrawAsync(r.AssignmentId, n.Id, r.CaregiverId));
        // staff can still withdraw it
        Assert.Equal("withdrawn", (await r.Svc.SetStatusAsync(n.Id, St("withdrawn"), r.AdminId)).Status);

        var today = await Give(r, 0); // same day: too late for the caregiver
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Svc.WithdrawAsync(r.AssignmentId, today.Id, r.CaregiverId));
    }

    [Fact]
    public async Task Withdraw_VersusStaffChange_ExactlyOneWins()
    {
        var r = Build();
        var n = await Give(r);
        // The caregiver's request has already read the notice (stale 'open') when staff credit it.
        var staleDb = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(r.DbName);
        _ = staleDb.VisitAbsenceNotices.First(); // tracked, Status = open
        await r.Svc.SetStatusAsync(n.Id, St("credited"), r.AdminId);
        var caregiverSvc = r.Make(staleDb);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => caregiverSvc.WithdrawAsync(r.AssignmentId, n.Id, r.CaregiverId));
        Assert.Contains("changed", ex.Message);
        Assert.Equal("credited", r.Db.VisitAbsenceNotices.AsNoTracking().Single().Status);

        // and the other way round: staff change loses to an earlier withdrawal
        var n2 = await Give(r, 8);
        var staleDb2 = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(r.DbName);
        _ = staleDb2.VisitAbsenceNotices.First(x => x.Id == ObjectId.Parse(n2.Id));
        await r.Svc.WithdrawAsync(r.AssignmentId, n2.Id, r.CaregiverId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Make(staleDb2).SetStatusAsync(n2.Id, St("credited"), r.AdminId));
        Assert.Equal("withdrawn", r.Db.VisitAbsenceNotices.AsNoTracking().Single(x => x.Id == ObjectId.Parse(n2.Id)).Status);
    }

    // ───────────── escalation ─────────────

    [Fact]
    public async Task Escalation_OpenNoticeWithin24h_AlertsApprovedAdminsOnce_NotOthers()
    {
        var r = Build(); // 09:00 Lagos Oct 12
        var tomorrowOpen = await Give(r, 1);     // starts in 24h -> due
        var farOpen = await Give(r, 5);          // not due
        r.Sent.Clear();

        var claimed = await r.Svc.EscalateDueAsync();
        Assert.Equal(1, claimed);
        var alerts = r.Sent.ToList();
        Assert.All(alerts, a => { Assert.Equal(NotificationTypes.VisitAbsenceNotified, a.Type); Assert.Equal(tomorrowOpen.Id, a.RelatedEntityId); });
        Assert.Equal(new[] { r.ApprovedAdminId }, alerts.Select(a => a.RecipientId).ToArray());
        Assert.DoesNotContain("SECRET-NOTE", alerts[0].Content);

        r.Sent.Clear();
        Assert.Equal(0, await r.Svc.EscalateDueAsync());   // never twice
        Assert.Empty(r.Sent);
        Assert.NotNull(r.Db.VisitAbsenceNotices.AsNoTracking().Single(x => x.Id == ObjectId.Parse(tomorrowOpen.Id)).EscalatedAt);
        Assert.Null(r.Db.VisitAbsenceNotices.AsNoTracking().Single(x => x.Id == ObjectId.Parse(farOpen.Id)).EscalatedAt);
    }

    [Fact]
    public async Task Escalation_SkipsHandledNotices_AndFiresWhenTheDayComesCloser()
    {
        var r = Build();
        var handled = await Give(r, 1);
        await r.Svc.SetStatusAsync(handled.Id, St("no_action_needed"), r.AdminId);
        var later = await Give(r, 4);
        r.Sent.Clear();
        Assert.Equal(0, await r.Svc.EscalateDueAsync());
        r.Clock.Now = r.Clock.Now.AddDays(3);               // now the later one is within 24h
        Assert.Equal(1, await r.Svc.EscalateDueAsync());
        Assert.Equal(later.Id, r.Sent.Single().RelatedEntityId);
    }

    [Fact]
    public async Task Escalation_TwoInstancesRacing_ExactlyOneAlertSet()
    {
        var r = Build();
        var n = await Give(r, 1);
        r.Sent.Clear();
        var dbName = r.DbName;
        var svcs = Enumerable.Range(0, 4).Select(_ => r.Make(PackageAssignmentCheckinAndTaskSheetTests.CreateDb(dbName))).ToList();
        var results = await Task.WhenAll(svcs.Select(s => s.EscalateDueAsync()));
        Assert.Equal(1, results.Sum());
        Assert.Single(r.Sent);                       // one approved admin, one alert, however many instances ran
        Assert.Single(r.Db.VisitAbsenceNoticeEvents.Where(e => e.NoticeId == n.Id && e.Kind == "escalated").ToList());
    }

    [Fact]
    public async Task EscalatedAtToken_SecondWriterWithStaleSnapshotLoses()
    {
        var r = Build();
        var n = await Give(r, 1);
        var db1 = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(r.DbName);
        var db2 = PackageAssignmentCheckinAndTaskSheetTests.CreateDb(r.DbName);
        var a = db1.VisitAbsenceNotices.First(); var b = db2.VisitAbsenceNotices.First();   // both read EscalatedAt = null
        a.EscalatedAt = DateTime.UtcNow; await db1.SaveChangesAsync();
        b.EscalatedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    // ───────────── dates / short-notice rule ─────────────

    [Fact]
    public async Task PastDateCheck_UsesTheLagosDate_NotUtc()
    {
        // 22:59 UTC on Oct 12 = 23:59 Lagos Oct 12 -> Oct 12 is still today.
        var before = Build(new DateTimeOffset(2026, 10, 12, 22, 59, 0, TimeSpan.Zero));
        Assert.Equal("2026-10-12", (await before.Svc.CreateAsync(before.AssignmentId, Req("2026-10-12"), before.CaregiverId)).Date);

        // 23:00 UTC on Oct 12 = 00:00 Lagos Oct 13 -> Oct 12 is now the past, even though the server's UTC date is still Oct 12.
        var after = Build(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(() => after.Svc.CreateAsync(after.AssignmentId, Req("2026-10-12"), after.CaregiverId));
        var today = await after.Svc.CreateAsync(after.AssignmentId, Req("2026-10-13"), after.CaregiverId);   // Lagos 'today'
        Assert.True(today.ShortNotice);
    }

    [Fact]
    public async Task ShortNotice_IsAFlagOnly_24hBoundaryTo0900Lagos()
    {
        var r = Build(); // 09:00 Lagos Oct 12
        Assert.True((await Give(r, 0)).ShortNotice);   // 0h
        Assert.False((await Give(r, 1)).ShortNotice);  // exactly 24h to 09:00 -> not short
        var fresh = Build(new DateTimeOffset(2026, 10, 12, 8, 1, 0, TimeSpan.Zero)); // 09:01 -> tomorrow 09:00 is 23h59 away
        Assert.True((await fresh.Svc.CreateAsync(fresh.AssignmentId, Req("2026-10-13"), fresh.CaregiverId)).ShortNotice);
    }

    // ───────────── core rejections (carried over) ─────────────

    [Fact]
    public async Task Rejections()
    {
        var r = Build();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(5)), ObjectId.GenerateNewId().ToString()));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(-1)), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req(r.LagosDate(90)), r.CaregiverId));
        await Assert.ThrowsAsync<ArgumentException>(() => r.Svc.CreateAsync(r.AssignmentId, Req("12/10/2026"), r.CaregiverId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => r.Svc.CreateAsync(ObjectId.GenerateNewId().ToString(), Req(r.LagosDate(5)), r.CaregiverId));
        await Give(r, 5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Give(r, 5));
        var a = r.Db.Assignments.Single(); a.Status = AssignmentStatuses.Cancelled; r.Db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Give(r, 6));
    }

    [Fact]
    public async Task AlreadySubmittedVisitForThatDay_Rejected()
    {
        var r = Build();
        var day = DateTime.ParseExact(r.LagosDate(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        r.Db.TaskSheets.Add(new TaskSheet { AssignmentId = r.AssignmentId, PackageRequestId = r.PackageRequestId, CaregiverId = r.CaregiverId, ScheduledDate = day, Status = "submitted" });
        r.Db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Give(r, 0));
    }

    [Fact]
    public async Task Visibility_CaregiverOwnerAndOwningClientOnly()
    {
        var r = Build();
        await Give(r);
        var stranger = ObjectId.GenerateNewId().ToString();
        Assert.Single(await r.Svc.GetForAssignmentAsync(r.AssignmentId, r.CaregiverId));
        Assert.Single(await r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, r.ClientId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.GetForAssignmentAsync(r.AssignmentId, stranger));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => r.Svc.GetForPackageRequestAsClientAsync(r.PackageRequestId, stranger));
    }
}
