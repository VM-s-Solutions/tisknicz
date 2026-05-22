using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Domain.Common;

public class AuditableTests
{
    private sealed class TestEntity : Auditable
    {
        public TestEntity(string id, string countryCode)
        {
            Id = id;
            CountryCode = countryCode;
        }
    }

    [Fact]
    public void New_Auditable_Is_Active_By_Default()
    {
        var e = new TestEntity("01HZ", "CZ");

        e.IsActive.Should().BeTrue();
        e.UpdatedAt.Should().BeNull();
        e.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public void MarkCreated_Sets_Audit_Fields()
    {
        var e = new TestEntity("01HZ", "CZ");
        var at = DateTimeOffset.Parse("2026-05-22T10:00:00Z");

        e.MarkCreated("alice@example.com", at);

        e.CreatedBy.Should().Be("alice@example.com");
        e.CreatedAt.Should().Be(at);
    }

    [Fact]
    public void MarkUpdated_Sets_Audit_Fields()
    {
        var e = new TestEntity("01HZ", "CZ");
        var at = DateTimeOffset.Parse("2026-05-22T11:00:00Z");

        e.MarkUpdated("bob@example.com", at);

        e.UpdatedBy.Should().Be("bob@example.com");
        e.UpdatedAt.Should().Be(at);
        // Marking updated does NOT touch IsActive or DeactivatedAt.
        e.IsActive.Should().BeTrue();
        e.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public void MarkDeactivated_Sets_Audit_Fields_And_Flips_IsActive()
    {
        var e = new TestEntity("01HZ", "CZ");
        var at = DateTimeOffset.Parse("2026-05-22T12:00:00Z");

        e.MarkDeactivated("admin@example.com", at);

        e.DeactivatedBy.Should().Be("admin@example.com");
        e.DeactivatedAt.Should().Be(at);
        e.IsActive.Should().BeFalse();
    }

    [Fact]
    public void MarkDeactivated_Is_Idempotent_Stamp_Wise()
    {
        var e = new TestEntity("01HZ", "CZ");
        var firstAt = DateTimeOffset.Parse("2026-05-22T12:00:00Z");
        var secondAt = DateTimeOffset.Parse("2026-05-22T13:00:00Z");

        e.MarkDeactivated("admin@example.com", firstAt);
        e.MarkDeactivated("other@example.com", secondAt);

        // Last call wins; this is an intentional behaviour, documented in the role file.
        e.DeactivatedBy.Should().Be("other@example.com");
        e.DeactivatedAt.Should().Be(secondAt);
        e.IsActive.Should().BeFalse();
    }

    [Fact]
    public void CountryCode_Is_Set_Once_And_Read_Back()
    {
        var e = new TestEntity("01HZ", "CZ");
        e.CountryCode.Should().Be("CZ");
    }

    [Fact]
    public void Id_Is_Settable_By_Concrete_Entity()
    {
        var e = new TestEntity("01HZ-CUSTOM", "CZ");
        e.Id.Should().Be("01HZ-CUSTOM");
        ((IEntity)e).Id.Should().Be("01HZ-CUSTOM");
    }
}
