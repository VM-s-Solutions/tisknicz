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
        return (true, null);
    }
}
