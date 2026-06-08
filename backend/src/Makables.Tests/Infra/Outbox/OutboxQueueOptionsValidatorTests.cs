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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_generate_invoice_queue_name(string name)
    {
        var (ok, err) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions
        {
            GenerateInvoiceQueueName = name,
        });
        ok.Should().BeFalse();
        err.Should().Contain("GenerateInvoiceQueueName");
    }

    [Theory]
    [InlineData("AB")]              // < 3 chars + uppercase
    [InlineData("Invoice-Queue")]   // uppercase
    [InlineData("-leading-hyphen")] // leading hyphen
    [InlineData("trailing-hyphen-")] // trailing hyphen
    [InlineData("under_score")]     // underscore not allowed
    [InlineData("ab")]              // < 3 chars
    public void Rejects_non_azure_queue_name_shapes(string name)
    {
        // T-0069: Azure queue names are 3-63 chars, lowercase + digits +
        // single hyphens, must start and end with letter/digit. A typo'd
        // queue name should fail at boot, not on the first publish.
        var (ok, err) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions
        {
            GenerateInvoiceQueueName = name,
        });
        ok.Should().BeFalse();
        err.Should().Contain("GenerateInvoiceQueueName");
    }

    [Theory]
    [InlineData("generate-invoice")]
    [InlineData("send-email")]
    [InlineData("abc")]
    [InlineData("a1b")]
    public void Accepts_valid_azure_queue_name_shapes(string name)
    {
        var (ok, _) = OutboxQueueOptionsValidator.Validate(new OutboxQueueOptions
        {
            SendEmailQueueName = name,
            GenerateInvoiceQueueName = name,
        });
        ok.Should().BeTrue();
    }
}
