namespace Makables.Infra.Common.Outbox;

/// <summary>
/// Storage-queue config for the T-0029 outbox publisher. Bound from the
/// <c>OutboxQueues</c> section of configuration; the underlying
/// <c>AzureWebJobsStorage</c> connection string is reused as the queue
/// connection — Functions and the publisher share one storage account
/// per ADR 0020 §"Local development" + §"Hosting".
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
