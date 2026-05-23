using FluentAssertions;
using Makables.Core.Domain.Auditing;

namespace Makables.Tests.Domain.AuditingTests;

public class AdminAuditLogEntryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-23T10:00:00Z");

    [Fact]
    public void Record_Sets_All_Fields()
    {
        var entry = AdminAuditLogEntry.Record(
            id: "01H-1",
            adminUserId: "admin@example.com",
            actionCode: "maker.verify",
            targetEntity: "maker",
            targetId: "M-CZ-1",
            beforeJson: """{"isVerified":false}""",
            afterJson: """{"isVerified":true}""",
            now: Now,
            notes: "Reviewed photos",
            ipAddress: "10.0.0.5",
            userAgent: "Mozilla/5.0");

        entry.Id.Should().Be("01H-1");
        entry.AdminUserId.Should().Be("admin@example.com");
        entry.ActionCode.Should().Be("maker.verify");
        entry.TargetEntity.Should().Be("maker");
        entry.TargetId.Should().Be("M-CZ-1");
        entry.BeforeJson.Should().Contain("false");
        entry.AfterJson.Should().Contain("true");
        entry.Notes.Should().Be("Reviewed photos");
        entry.IpAddress.Should().Be("10.0.0.5");
        entry.UserAgent.Should().Be("Mozilla/5.0");
        entry.CreatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("", "admin", "code", "entity", "id")]
    [InlineData("01H", "", "code", "entity", "id")]
    [InlineData("01H", "admin", "", "entity", "id")]
    [InlineData("01H", "admin", "code", "", "id")]
    [InlineData("01H", "admin", "code", "entity", "")]
    public void Record_Rejects_Missing_Required_Field(
        string id, string adminUserId, string actionCode, string targetEntity, string targetId)
    {
        var act = () => AdminAuditLogEntry.Record(
            id, adminUserId, actionCode, targetEntity, targetId,
            beforeJson: null, afterJson: null, now: Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_Allows_Null_Before_And_After()
    {
        // Create commands have no before-state; admin queries may have null after.
        var entry = AdminAuditLogEntry.Record(
            "01H-1", "admin", "country.create", "country", "CZ",
            beforeJson: null, afterJson: """{"countryId":"CZ"}""", now: Now);

        entry.BeforeJson.Should().BeNull();
        entry.AfterJson.Should().NotBeNull();
    }
}
