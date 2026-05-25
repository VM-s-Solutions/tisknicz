using FluentAssertions;
using Makables.Infra.Common.Outbox;

namespace Makables.Tests.Infra.Outbox;

/// <summary>
/// T-0029 sec reviewer M-4: a typo'd queue connection string must crash
/// the host at boot, not silently inside a timer tick 30 s later.
/// </summary>
public class OutboxQueueOptionsValidatorTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        var (ok, _) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions());
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_connection_string(string cs)
    {
        var (ok, err) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions
        {
            ConnectionString = cs,
        });
        ok.Should().BeFalse();
        err.Should().Contain("ConnectionString");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_queue_name(string name)
    {
        var (ok, err) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions
        {
            SendEmailQueueName = name,
        });
        ok.Should().BeFalse();
        err.Should().Contain("SendEmailQueueName");
    }
}
