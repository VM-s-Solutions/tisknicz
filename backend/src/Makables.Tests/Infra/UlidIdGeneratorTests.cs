using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Infra.Common.Identifiers;

namespace Makables.Tests.Infra;

public class UlidIdGeneratorTests
{
    [Fact]
    public void Next_Returns_NonEmpty_String()
    {
        IIdGenerator gen = new UlidIdGenerator();

        var id = gen.Next();

        id.Should().NotBeNullOrWhiteSpace();
        id.Length.Should().Be(26);   // ULID canonical length
    }

    [Fact]
    public void Next_Returns_Unique_Values()
    {
        IIdGenerator gen = new UlidIdGenerator();

        var ids = Enumerable.Range(0, 100).Select(_ => gen.Next()).ToHashSet();

        ids.Should().HaveCount(100);
    }

    [Fact]
    public void Next_Returns_Lexicographically_Sortable_Sequence()
    {
        IIdGenerator gen = new UlidIdGenerator();

        var ids = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            ids.Add(gen.Next());
            // 1ms apart ensures the timestamp portion differs in adjacent IDs.
            Thread.Sleep(2);
        }

        var sorted = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();
        ids.Should().Equal(sorted);
    }
}
