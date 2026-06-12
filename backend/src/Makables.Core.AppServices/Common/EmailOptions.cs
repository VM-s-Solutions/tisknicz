namespace Makables.Core.AppServices.Common;

/// <summary>
/// Cross-cutting email settings that are not provider credentials
/// (SendGrid options live in <c>Infra.Clients</c>). T-0106 introduces
/// the single member: the ops mailbox that receives the
/// <c>order.disputed.adminEmail</c> digest.
///
/// <para>
/// Bound from the <c>Email</c> configuration section, with the raw
/// <c>ADMIN_NOTIFICATION_EMAIL</c> environment variable as a deployment
/// override (T-0106 manual step). Deliberately NOT <c>ValidateOnStart</c>:
/// a missing address must not stop the hosts from booting — the outbox
/// send fails Configuration-class
/// (<see cref="Makables.Core.Domain.Common.BusinessErrorMessage.EmailAdminRecipientNotConfigured"/>)
/// and is retried after the fix per ADR 0020 (§C.9).
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Override key checked verbatim so the documented deployment env var
    /// works without the <c>Email__</c> prefix convention.
    /// </summary>
    public const string AdminNotificationAddressEnvKey = "ADMIN_NOTIFICATION_EMAIL";

    /// <summary>Recipient of admin dispute notifications. Null/blank → send-time Configuration failure.</summary>
    public string? AdminNotificationAddress { get; set; }
}
