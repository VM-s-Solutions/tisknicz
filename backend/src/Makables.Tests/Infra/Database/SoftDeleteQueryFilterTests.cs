using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Makables.Tests.Infra.Database;

public class SoftDeleteQueryFilterTests
{
    [Fact]
    public async Task Active_Rows_Are_Returned_By_Default()
    {
        using var h = TestDbHarness.Create();

        h.Db.Widgets.Add(new WidgetEntity("01H-1", "CZ", "Anvil"));
        h.Db.Widgets.Add(new WidgetEntity("01H-2", "CZ", "Bellows"));
        await h.Db.SaveChangesAsync(default);

        var widgets = await h.Db.Widgets.ToListAsync();
        widgets.Should().HaveCount(2);
    }

    [Fact]
    public async Task Soft_Deleted_Rows_Are_Filtered_Out()
    {
        using var h = TestDbHarness.Create();

        var w1 = new WidgetEntity("01H-3", "CZ", "Active");
        var w2 = new WidgetEntity("01H-4", "CZ", "Soft-deleted");
        h.Db.Widgets.AddRange(w1, w2);
        await h.Db.SaveChangesAsync(default);

        w2.MarkDeactivated("admin@example.com", h.FixedNow);
        await h.Db.SaveChangesAsync(default);

        var widgets = await h.Db.Widgets.ToListAsync();
        widgets.Should().ContainSingle().Which.Id.Should().Be("01H-3");
    }

    [Fact]
    public async Task IgnoreQueryFilters_Returns_Soft_Deleted_Rows()
    {
        using var h = TestDbHarness.Create();

        var w1 = new WidgetEntity("01H-5", "CZ", "Active");
        var w2 = new WidgetEntity("01H-6", "CZ", "Soft-deleted");
        h.Db.Widgets.AddRange(w1, w2);
        await h.Db.SaveChangesAsync(default);

        w2.MarkDeactivated("admin@example.com", h.FixedNow);
        await h.Db.SaveChangesAsync(default);

        var widgets = await h.Db.Widgets.IgnoreQueryFilters().ToListAsync();
        widgets.Should().HaveCount(2);
        widgets.Should().Contain(w => !w.IsActive);
    }
}
