namespace Makables.Infra.Common.Outbox;

/// <summary>
/// Storage-queue config for the T-0029 outbox publisher. Bound from the
/// <c>OutboxQueues</c> section of configuration; the underlying
/// <c>AzureWebJobsStorage</c> connection string is reused as the queue
/// connection — Functions and the publisher share one storage account
/// per ADR 0020 §"Local development" + §"Hosting".
///
/// Validated at startup by <see cref="OutboxQueueOptionsValidator"/>
/// (T-0029 sec reviewer M-4 / CQ m-3) so a typo'd connection string or
/// queue name crashes the host at boot, not on the first publish.
/// </summary>
public sealed class OutboxQueueOptions
{
    public const string SectionName = "OutboxQueues";

    /// <summary>
    /// Storage account connection string. In production this is a Key Vault
    /// reference; in local dev <c>UseDevelopmentStorage=true</c> works for
    /// Azurite, matching the Functions <c>AzureWebJobsStorage</c> default.
    /// </summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    /// <summary>
    /// Queue name for outbox-driven email sends. <c>SendEmailFunction</c>
    /// listens here. ADR 0020 §"Specific Functions at launch" calls it
    /// <c>send-email</c>.
    /// </summary>
    public string SendEmailQueueName { get; set; } = "send-email";

    /// <summary>
    /// Queue name for outbox-driven invoice PDF generation.
    /// <c>GenerateInvoiceFunction</c> listens here and dispatches
    /// <c>IssueInvoice.Command</c> via Mediator. Per T-0069 locked
    /// decision 2: distinct from <see cref="SendEmailQueueName"/> so
    /// render/upload failures don't contaminate the email-send retry
    /// budget (and vice versa — a stuck SendGrid call doesn't stall
    /// invoice rendering).
    /// </summary>
    public string GenerateInvoiceQueueName { get; set; } = "generate-invoice";

    /// <summary>
    /// Queue name for outbox-driven Packeta shipping-label PDF storage.
    /// <c>GenerateLabelFunction</c> listens here and dispatches
    /// <c>FetchAndStoreShippingLabel.Command</c> via Mediator. Per T-0072
    /// + ADR 0020 queue-per-event-class: distinct from
    /// <see cref="SendEmailQueueName"/> and <see cref="GenerateInvoiceQueueName"/>
    /// so a slow Packeta label download doesn't contaminate either retry
    /// budget. T-0074 owns the consumer.
    /// </summary>
    public string GenerateLabelQueueName { get; set; } = "generate-label";
}

/// <summary>
/// Startup validator for <see cref="OutboxQueueOptions"/>. Per T-0029
/// sec reviewer M-4 / CQ m-3: a misconfigured queue connection string
/// silently fails at first publish (sometimes 30 s after host boot, deep
/// inside a timer tick). Fail at boot instead so deploy logs show the
/// problem before the App Insights "everything's healthy" window closes.
/// </summary>
public static class OutboxQueueOptionsValidator
{
    public static (bool Ok, string? Error) Validate(OutboxQueueOptions options)
    {
        if (options is null) return (false, "OutboxQueues section is missing.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return (false, "OutboxQueues:ConnectionString is required.");
        if (string.IsNullOrWhiteSpace(options.SendEmailQueueName))
            return (false, "OutboxQueues:SendEmailQueueName is required.");
        if (!IsValidAzureQueueName(options.SendEmailQueueName))
            return (false, "OutboxQueues:SendEmailQueueName must match Azure queue naming rules (^[a-z0-9-]{3,63}$).");
        if (string.IsNullOrWhiteSpace(options.GenerateInvoiceQueueName))
            return (false, "OutboxQueues:GenerateInvoiceQueueName is required.");
        if (!IsValidAzureQueueName(options.GenerateInvoiceQueueName))
            return (false, "OutboxQueues:GenerateInvoiceQueueName must match Azure queue naming rules (^[a-z0-9-]{3,63}$).");
        if (string.IsNullOrWhiteSpace(options.GenerateLabelQueueName))
            return (false, "OutboxQueues:GenerateLabelQueueName is required.");
        if (!IsValidAzureQueueName(options.GenerateLabelQueueName))
            return (false, "OutboxQueues:GenerateLabelQueueName must match Azure queue naming rules (^[a-z0-9-]{3,63}$).");
        return (true, null);
    }

    /// <summary>
    /// Azure Storage queue name rules per
    /// <see href="https://learn.microsoft.com/en-us/rest/api/storageservices/naming-queues-and-metadata"/>:
    /// 3-63 chars, lowercase letters / digits / hyphens, must start and end
    /// with letter or digit (no leading / trailing / consecutive hyphens).
    /// The regex below is a tightening of that contract — the storage SDK
    /// will reject anything that slips past on its first publish, but
    /// failing at boot is the right place to catch a typo.
    /// </summary>
    private static bool IsValidAzureQueueName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name, "^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$");
}
