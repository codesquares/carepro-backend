using Application.DTOs;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using Xunit;

namespace CommitmentGate.Tests;

/// <summary>
/// Chat is only open between a client and a caregiver who share an Accepted assignment. Once that
/// assignment ends the thread is read-only; old-model threads with no assignment are archived
/// read-only history; everything else is closed. Real local MongoDB (replica set on 127.0.0.1:27018,
/// same infra as the other suites).
/// </summary>
public class ChatAccessServiceTests
{
    private const string Client = "6a0000000000000000000c01";
    private const string Caregiver = "6a0000000000000000000a01";

    private static CareProDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CareProDbContext>()
            .UseMongoDB("mongodb://127.0.0.1:27018", $"carepro_chataccess_tests_{Guid.NewGuid():N}")
            .Options;
        return new TestCareProDbContext(options);
    }

    private static Assignment NewAssignment(string status, DateTime? respondedAt, string client = Client, string caregiver = Caregiver) => new()
    {
        Id = ObjectId.GenerateNewId(),
        ClientId = client,
        CaregiverId = caregiver,
        PackageRequestId = ObjectId.GenerateNewId().ToString(),
        Status = status,
        RespondedAt = respondedAt,
        AssignedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private static ChatMessage NewMessage(string from, string to) => new()
    {
        MessageId = ObjectId.GenerateNewId(),
        SenderId = from,
        ReceiverId = to,
        Message = "hello",
        Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public async Task Accepted_assignment_opens_chat_for_both_roles()
    {
        await using var db = CreateDb();
        var accepted = NewAssignment(AssignmentStatuses.Accepted, DateTime.UtcNow);
        db.Assignments.Add(accepted);
        await db.SaveChangesAsync();
        var sut = new ChatAccessService(db);

        var asClient = await sut.GetAccessAsync(Client, "Client", Caregiver);
        var asCaregiver = await sut.GetAccessAsync(Caregiver, "Caregiver", Client);

        Assert.Equal(ChatAccessStates.Active, asClient.State);
        Assert.True(asClient.CanSend);
        Assert.Equal(accepted.Id.ToString(), asClient.AssignmentId);
        Assert.Equal(ChatAccessStates.Active, asCaregiver.State);
        Assert.True(asCaregiver.CanSend);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")] // no such status exists yet; any post-acceptance terminal status must lock the chat
    public async Task Ended_after_acceptance_is_read_only(string endedStatus)
    {
        await using var db = CreateDb();
        db.Assignments.Add(NewAssignment(endedStatus, DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var access = await new ChatAccessService(db).GetAccessAsync(Client, "Client", Caregiver);

        Assert.Equal(ChatAccessStates.Ended, access.State);
        Assert.False(access.CanSend);
        Assert.Contains("contact CarePro support", access.Reason);
    }

    [Theory]
    [InlineData(AssignmentStatuses.PendingAcceptance, false)]
    [InlineData(AssignmentStatuses.Declined, true)]
    [InlineData(AssignmentStatuses.Cancelled, false)] // cancelled while still pending: never accepted, so never had a chat
    public async Task Never_accepted_assignments_do_not_open_or_end_a_chat(string status, bool responded)
    {
        await using var db = CreateDb();
        db.Assignments.Add(NewAssignment(status, responded ? DateTime.UtcNow : null));
        await db.SaveChangesAsync();

        var access = await new ChatAccessService(db).GetAccessAsync(Client, "Client", Caregiver);

        Assert.Equal(ChatAccessStates.None, access.State);
        Assert.False(access.CanSend);
        Assert.Null(access.Counterpart);
    }

    [Fact]
    public async Task Old_model_thread_without_an_assignment_is_archived_read_only()
    {
        await using var db = CreateDb();
        db.ChatMessages.Add(NewMessage(Client, Caregiver));
        await db.SaveChangesAsync();

        var access = await new ChatAccessService(db).GetAccessAsync(Client, "Client", Caregiver);

        Assert.Equal(ChatAccessStates.Archived, access.State);
        Assert.False(access.CanSend);
    }

    [Fact]
    public async Task Unrelated_or_unknown_users_and_non_participant_roles_are_closed()
    {
        await using var db = CreateDb();
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Accepted, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sut = new ChatAccessService(db);

        var unrelated = await sut.GetAccessAsync(Client, "Client", "6a0000000000000000000a99");
        var unknown = await sut.GetAccessAsync(Client, "Client", "000000000000000000000000");
        var admin = await sut.GetAccessAsync("6a0000000000000000000d01", "Admin", Caregiver);
        var self = await sut.GetAccessAsync(Client, "Client", Client);

        foreach (var access in new[] { unrelated, unknown, admin, self })
        {
            Assert.Equal(ChatAccessStates.None, access.State);
            Assert.False(access.CanSend);
        }
    }

    [Fact]
    public async Task Another_pairs_accepted_assignment_does_not_open_this_pair()
    {
        await using var db = CreateDb();
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Accepted, DateTime.UtcNow, client: "6a0000000000000000000c02"));
        await db.SaveChangesAsync();

        var access = await new ChatAccessService(db).GetAccessAsync(Client, "Client", Caregiver);

        Assert.False(access.CanSend);
    }

    [Fact]
    public async Task A_new_accepted_assignment_reopens_a_pair_whose_earlier_one_ended()
    {
        await using var db = CreateDb();
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Cancelled, DateTime.UtcNow.AddDays(-30)));
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Accepted, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var access = await new ChatAccessService(db).GetAccessAsync(Client, "Client", Caregiver);

        Assert.Equal(ChatAccessStates.Active, access.State);
        Assert.True(access.CanSend);
    }

    [Fact]
    public async Task Inbox_states_classify_every_partner_and_stamp_conversations()
    {
        await using var db = CreateDb();
        const string ended = "6a0000000000000000000a02", legacy = "6a0000000000000000000a03";
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Accepted, DateTime.UtcNow));
        db.Assignments.Add(NewAssignment(AssignmentStatuses.Cancelled, DateTime.UtcNow.AddDays(-2), caregiver: ended));
        await db.SaveChangesAsync();
        var sut = new ChatAccessService(db);
        var conversations = new List<ConversationDTO>
        {
            new() { UserId = Caregiver }, new() { UserId = ended }, new() { UserId = legacy },
        };

        await sut.EnrichConversationsAsync(Client, "Client", conversations);

        Assert.Equal((ChatAccessStates.Active, true), (conversations[0].AccessState, conversations[0].CanSend));
        Assert.Equal((ChatAccessStates.Ended, false), (conversations[1].AccessState, conversations[1].CanSend));
        Assert.Equal((ChatAccessStates.Archived, false), (conversations[2].AccessState, conversations[2].CanSend));
    }
}
