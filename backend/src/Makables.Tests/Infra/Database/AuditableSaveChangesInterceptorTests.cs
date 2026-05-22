using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Infra.Database;

public class AuditableSaveChangesInterceptorTests
{
    [Fact]
    public async Task On_Add_Stamps_CreatedBy_And_CreatedAt_From_Session_And_Clock()
    {
        using var h = TestDbHarness.Create(userId: "alice@example.com");

        var widget = new WidgetEntity("01H-A", "CZ", "Hammer");
        h.Db.Widgets.Add(widget);
        await h.Db.SaveChangesAsync(default);

        widget.CreatedBy.Should().Be("alice@example.com");
        widget.CreatedAt.Should().Be(h.FixedNow);
        widget.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task On_Add_Falls_Back_To_System_When_No_User()
    {
        using var h = TestDbHarness.Create(userId: null);

        var widget = new WidgetEntity("01H-B", "CZ", "Saw");
        h.Db.Widgets.Add(widget);
        await h.Db.SaveChangesAsync(default);

        widget.CreatedBy.Should().Be("system");
    }

    [Fact]
    public async Task On_Add_Does_Not_Overwrite_Pre_Set_CreatedBy()
    {
        // Seed data and migration scenarios may set CreatedBy explicitly via
        // MarkCreated. The interceptor honours that.
        using var h = TestDbHarness.Create(userId: "alice@example.com");

        var widget = new WidgetEntity("01H-C", "CZ", "Drill");
        widget.MarkCreated("seed", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        h.Db.Widgets.Add(widget);
        await h.Db.SaveChangesAsync(default);

        widget.CreatedBy.Should().Be("seed");
        widget.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    }

    [Fact]
    public async Task On_Update_Stamps_UpdatedBy_And_UpdatedAt()
    {
        using var h = TestDbHarness.Create(userId: "alice@example.com");

        var widget = new WidgetEntity("01H-D", "CZ", "Plane");
        h.Db.Widgets.Add(widget);
        await h.Db.SaveChangesAsync(default);
        var originalCreatedAt = widget.CreatedAt;

        widget.Name = "Smoothing Plane";
        await h.Db.SaveChangesAsync(default);

        widget.UpdatedBy.Should().Be("alice@example.com");
        widget.UpdatedAt.Should().Be(h.FixedNow);
        widget.CreatedAt.Should().Be(originalCreatedAt);  // unchanged
    }

    [Fact]
    public async Task On_Soft_Delete_Audit_Fields_Set_By_Domain_Not_Interceptor()
    {
        // MarkDeactivated is a domain action; the interceptor doesn't
        // implicitly flip IsActive on EF Remove.
        using var h = TestDbHarness.Create(userId: "admin@example.com");

        var widget = new WidgetEntity("01H-E", "CZ", "Chisel");
        h.Db.Widgets.Add(widget);
        await h.Db.SaveChangesAsync(default);

        widget.MarkDeactivated("admin@example.com", h.FixedNow);
        await h.Db.SaveChangesAsync(default);

        widget.IsActive.Should().BeFalse();
        widget.DeactivatedBy.Should().Be("admin@example.com");
        widget.DeactivatedAt.Should().Be(h.FixedNow);
        // The "Modified" update also stamps UpdatedBy/At.
        widget.UpdatedAt.Should().Be(h.FixedNow);
    }
}
