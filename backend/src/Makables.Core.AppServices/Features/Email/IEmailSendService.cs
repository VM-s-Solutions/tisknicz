using System.Text.Json;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Email;

/// <summary>
/// Composes + dispatches one transactional email from a queued outbox
/// row. Owns the end-to-end logic (decode payload → look up template →
/// resolve translation with fallback → assemble <see cref="EmailMessage"/>
/// → call <see cref="IEmailProvider"/>) so T-0029's
/// <c>ProcessOutboxFunction</c> stays a thin scheduler.
///
/// Per ADR 0019 (amended): the outbox is the single chokepoint into
/// emails — no handler ever calls <see cref="IEmailProvider"/> directly.
/// This service is the only consumer.
/// </summary>
public interface IEmailSendService
{
    /// <summary>
    /// Render and send the email described by an outbox row.
    /// </summary>
    /// <param name="outboxEventType">One of <see cref="OutboxEventTypes"/>.</param>
    /// <param name="payloadJson">JSON-encoded payload — type depends on
    /// the event (auth flows use <see cref="OneTimeTokenOutboxPayload"/>;
    /// order flows use <see cref="OrderPaidCustomerEmailPayload"/> /
    /// <see cref="OrderPlacedMakerEmailPayload"/>).</param>
    Task<BusinessResult<EmailSentReceipt>> SendAsync(
        string outboxEventType,
        string payloadJson,
        CancellationToken cancellationToken);
}

public sealed class EmailSendService(
    IEmailTemplateRepository templates,
    IEmailTemplateTranslationRepository translations,
    IEmailProvider provider,
    IInvoiceRepository invoices,
    IBlobStorageClient blobStorage,
    ICountryConfigurationRepository countries,
    IOptions<PublicAppUrlsOptions> urls,
    IOptions<EmailOptions> emailOptions,
    ILogger<EmailSendService> logger) : IEmailSendService
{
    public Task<BusinessResult<EmailSentReceipt>> SendAsync(
        string outboxEventType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        // Per-event-type switch (T-0067 Q3). Each branch deserializes the
        // matching payload record and routes through a private helper.
        // Adding a new event = one new case + one new helper; the
        // discriminated routing is a compile-time check, not a runtime
        // string lookup.
        return outboxEventType switch
        {
            OutboxEventTypes.AuthMagicLinkSend
                or OutboxEventTypes.AuthEmailConfirmationSend
                or OutboxEventTypes.AuthPasswordResetSend
                    => SendAuthEmailAsync(outboxEventType, payloadJson, cancellationToken),

            OutboxEventTypes.OrderPaidCustomerEmail
                => SendOrderPaidCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderPlacedMakerEmail
                => SendOrderPlacedMakerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderAcceptedCustomerEmail
                => SendOrderAcceptedCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderShippedCustomerEmail
                => SendOrderShippedCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderDeliveredCustomerEmail
                => SendOrderDeliveredCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderMessagePostedCustomerEmail
                => SendOrderMessagePostedCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderMessagePostedMakerEmail
                => SendOrderMessagePostedMakerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderCancelledCustomerEmail
                => SendOrderCancelledCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderRefundedCustomerEmail
                => SendOrderRefundedCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderDisputedAdminEmail
                => SendOrderDisputedAdminEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.OrderDisputeResolvedCustomerEmail
                => SendOrderDisputeResolvedCustomerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.PayoutFeeInvoiceMakerEmail
                => SendPayoutFeeInvoiceMakerEmailAsync(payloadJson, cancellationToken),

            OutboxEventTypes.PayoutBatchPayoutSentMakerEmail
                => SendPayoutSentMakerEmailAsync(payloadJson, cancellationToken),

            _ => UnknownEventTypeAsync(outboxEventType),
        };
    }

    // === T-0102b: Payout fee-invoice (maker) email branch. ===

    private async Task<BusinessResult<EmailSentReceipt>> SendPayoutFeeInvoiceMakerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<PayoutFeeInvoiceMakerEmailPayload>(
            payloadJson, OutboxEventTypes.PayoutFeeInvoiceMakerEmail);
        if (!payloadResult.IsSuccess)
        {
            return BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!);
        }
        var payload = payloadResult.Value!;

        // T-0069 pattern: look up the invoice at SEND time. A not-yet-rendered
        // invoice (PdfBlobPath null) returns Transient(InvoiceNotYetRendered)
        // for outbox re-delivery — never baked into the payload.
        var invoice = await invoices.GetByIdAsync(payload.InvoiceId, cancellationToken);
        if (invoice is null || string.IsNullOrWhiteSpace(invoice.PdfBlobPath))
        {
            logger.LogDebug(
                "PayoutFeeInvoiceMakerEmail for invoice {InvoiceId}: not yet rendered " +
                "(invoice null = {InvoiceNull}). Returning Transient for outbox re-delivery.",
                payload.InvoiceId, invoice is null);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.InvoiceNotYetRendered));
        }

        byte[] pdfBytes;
        try
        {
            var download = await blobStorage.DownloadAsync(
                BlobContainer.Invoices, invoice.PdfBlobPath!, cancellationToken);
            if (!download.IsSuccess)
            {
                logger.LogCritical(
                    "PayoutFeeInvoiceMakerEmail for invoice {InvoiceNumber}: blob download failed at {BlobPath}: {Code}.",
                    invoice.InvoiceNumber, invoice.PdfBlobPath, download.Error!.Code);
                return BusinessResult.Failure<EmailSentReceipt>(
                    Error.Permanent(BusinessErrorMessage.InvoicePdfAttachmentDownloadFailed));
            }
            using var stream = download.Value!.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            pdfBytes = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "PayoutFeeInvoiceMakerEmail for invoice {InvoiceNumber}: blob download threw at {BlobPath}.",
                invoice.InvoiceNumber, invoice.PdfBlobPath);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.InvoicePdfAttachmentDownloadFailed));
        }

        // Language-aware filename (T-0069 §C.6): en-US → fee-invoice-{n}.pdf,
        // else faktura-provize-{n}.pdf.
        var filename = payload.LanguageCode == "en-US"
            ? $"fee-invoice-{payload.InvoiceNumber}.pdf"
            : $"faktura-provize-{payload.InvoiceNumber}.pdf";
        var attachment = new Attachment(filename, pdfBytes, "application/pdf");

        return await DispatchOrderEmailAsync(
            templateType: EmailTemplateType.PayoutFeeInvoiceMaker,
            toAddress: payload.MakerEmail,
            toName: null,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["batch_number"] = payload.BatchNumber,
                ["invoice_number"] = payload.InvoiceNumber,
                ["fee_amount_minor"] = payload.FeeAmountMinor,
                ["fee_amount"] = FormatAmount(payload.FeeAmountMinor, payload.Currency),
                ["currency"] = payload.Currency,
                ["language_code"] = payload.LanguageCode,
            },
            attachment: attachment,
            cancellationToken: cancellationToken);
    }

    // === T-0103: Payout-sent (maker) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendPayoutSentMakerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<PayoutBatchPayoutSentMakerEmailPayload>(
            payloadJson, OutboxEventTypes.PayoutBatchPayoutSentMakerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        // Plain summary — NO PDF attachment (unlike the fee-invoice email).
        // {{total_paid}} is server-formatted CZK; the fee-invoice page deep
        // link is pre-baked into the payload at enqueue time.
        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.PayoutSentMaker,
            toAddress: payload.MakerEmail,
            toName: null,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["batch_number"] = payload.BatchNumber,
                ["total_paid_minor"] = payload.MakerTotalPaidMinor,
                ["total_paid"] = FormatAmount(payload.MakerTotalPaidMinor, payload.Currency),
                ["currency"] = payload.Currency,
                ["order_count"] = payload.OrderCount,
                ["fee_invoice_url"] = payload.FeeInvoiceActionUrl,
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0106: OrderDisputed (admin) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderDisputedAdminEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderDisputedAdminEmailPayload>(
            payloadJson, OutboxEventTypes.OrderDisputedAdminEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        // §C.9: the recipient resolves at SEND time — deliberately not
        // baked into the payload so a missing config never blocks the
        // dispute open itself; the outbox row parks Configuration-class
        // and is retried after the fix (ADR 0020).
        var adminAddress = emailOptions.Value.AdminNotificationAddress;
        if (string.IsNullOrWhiteSpace(adminAddress))
        {
            logger.LogCritical(
                "OrderDisputedAdminEmail for order {OrderId}: EmailOptions.AdminNotificationAddress " +
                "(ADMIN_NOTIFICATION_EMAIL) is not configured — parking the outbox row.",
                payload.OrderId);
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(
                Error.Configuration(BusinessErrorMessage.EmailAdminRecipientNotConfigured)));
        }

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderDisputedAdmin,
            toAddress: adminAddress,
            toName: null,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["dispute_id"] = payload.DisputeId,
                ["category"] = payload.Category.ToString(),
                ["source"] = payload.Source.ToString(),
                // USER-SUPPLIED (customer/maker free text) — neutralized so
                // hostile "{{key}}" sequences can never re-expand inside the
                // substitution engine (Gate 3 F1).
                ["description"] = NeutralizePlaceholderSyntax(payload.Description),
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0106: OrderDisputeResolved (customer) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderDisputeResolvedCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderDisputeResolvedCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderDisputeResolvedCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        // Czech outcome labels for the plain-text body (no conditional
        // syntax in the substitution engine; pre-rendered like the
        // refund_kind variant in the T-0105 branch).
        var outcomeLabel = payload.Outcome switch
        {
            Makables.Core.Domain.Orders.DisputeResolutionOutcome.Refunded => "vrácení peněz",
            Makables.Core.Domain.Orders.DisputeResolutionOutcome.Cancelled => "zrušení objednávky",
            _ => "pokračování objednávky",
        };

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderDisputeResolvedCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["outcome"] = payload.Outcome.ToString(),
                ["outcome_label"] = outcomeLabel,
                // Customer-VISIBLE per §C.7. Admin-authored free text —
                // neutralized like Description so "{{key}}" sequences can
                // never re-expand inside the substitution engine (Gate 3 F1).
                ["resolution_notes"] = NeutralizePlaceholderSyntax(payload.ResolutionNotes),
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0105: OrderRefunded (customer) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderRefundedCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderRefundedCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderRefundedCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderRefundedCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["refunded_amount_minor"] = payload.RefundedAmountMinor,
                ["refunded_amount"] = FormatAmount(payload.RefundedAmountMinor, payload.Currency),
                ["currency"] = payload.Currency,
                // Template renders full-vs-partial copy from this flag.
                // Pre-rendered as a string because the plain-text
                // substitution path has no conditional syntax.
                ["is_full_refund"] = payload.IsFullRefund ? "true" : "false",
                ["refund_kind"] = payload.IsFullRefund ? "plná" : "částečná",
                ["language_code"] = payload.LanguageCode,
            },
            // No PDF attachment — credit-note invoices are v1.1 (Q1).
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0079: OrderMessagePostedCustomer email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderMessagePostedCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderMessagePostedCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderMessagePostedCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderMessagePostedCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["sender_name"] = payload.SenderName,
                ["unread_count"] = payload.UnreadMessageCount,
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0079: OrderMessagePostedMaker email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderMessagePostedMakerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderMessagePostedMakerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderMessagePostedMakerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderMessagePostedMaker,
            toAddress: payload.MakerEmail,
            toName: null,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["sender_name"] = payload.SenderName,
                ["unread_count"] = payload.UnreadMessageCount,
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0083: OrderCancelled (customer) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderCancelledCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderCancelledCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderCancelledCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        // T-0083 ships AutoExpiry-source copy only. Customer + Admin variants
        // pick distinct templates via the same routing branch in T-0105 / T-0107.
        var templateType = payload.Reason switch
        {
            OrderCancellationSource.AutoExpiry => EmailTemplateType.OrderCancelledAutoExpiryCustomer,
            _ => EmailTemplateType.OrderCancelledAutoExpiryCustomer, // fallback to AutoExpiry copy at MVP
        };

        return DispatchOrderEmailAsync(
            templateType: templateType,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["reason"] = payload.Reason.ToString(),
                ["language_code"] = payload.LanguageCode,
            },
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0076: Order delivered (customer) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderDeliveredCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderDeliveredCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderDeliveredCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderDeliveredCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["language_code"] = payload.LanguageCode,
            },
            // No PDF attachment — invoice rides only on the
            // OrderPaidCustomer email per T-0069 locked decision 10.
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0071: Order accepted (maker → customer) email branch. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderAcceptedCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderAcceptedCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderAcceptedCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderAcceptedCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["language_code"] = payload.LanguageCode,
            },
            // No PDF attachment — invoice rides only on the OrderPaidCustomer
            // email per T-0069 locked decision 10. Subsequent state-change
            // emails are reference-only.
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === T-0072 / T-0073: Order shipped (customer) email branch — unified. ===

    private Task<BusinessResult<EmailSentReceipt>> SendOrderShippedCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderShippedCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderShippedCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        // T-0073: TrackingUrl is nullable — personal-pickup passes null. The
        // SendGrid template conditionally renders the tracking-URL row only
        // when the substitution is non-empty. We pass the empty string for
        // null so the substitution variable is always present in the dict.
        var trackingUrlSubstitution = payload.TrackingUrl ?? string.Empty;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderShippedCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["tracking_url"] = trackingUrlSubstitution,
                ["language_code"] = payload.LanguageCode,
            },
            // No PDF attachment — the label is for the maker. Customer gets
            // the tracking URL instead (or no link for personal-pickup).
            attachment: null,
            cancellationToken: cancellationToken);
    }

    // === Auth-flow branch — preserves T-0028 behaviour byte-for-byte. ===

    private async Task<BusinessResult<EmailSentReceipt>> SendAuthEmailAsync(
        string outboxEventType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var templateType = MapAuthEventToTemplateType(outboxEventType);

        OneTimeTokenOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OneTimeTokenOutboxPayload>(payloadJson);
        }
        catch (JsonException ex)
        {
            // T-0028 CQ reviewer N-4: split from MissingFields so T-0029's
            // triage UI can distinguish "decode crashed" from "decode succeeded
            // but a field is blank".
            logger.LogWarning(ex, "Outbox payload JSON malformed for event {EventType}.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadMalformed,
                    "Outbox payload could not be JSON-decoded."));
        }
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Email)
            || string.IsNullOrWhiteSpace(payload.RawToken)
            || string.IsNullOrWhiteSpace(payload.LanguageCode))
        {
            logger.LogWarning("Outbox payload for {EventType} decoded but is missing required fields.", outboxEventType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailPayloadMissingFields,
                    "Outbox payload is missing one or more required fields (Email, RawToken, LanguageCode)."));
        }

        var template = await templates.GetByTypeAsync(templateType, cancellationToken);
        if (template is null)
        {
            logger.LogError("EmailTemplate row missing for type {TemplateType}.", templateType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateNotFound,
                    $"No EmailTemplate row exists for type '{templateType}'."));
        }

        var translation = await ResolveTranslationAsync(template.Id, payload.LanguageCode, templateType, cancellationToken);
        if (translation is null)
        {
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateTranslationMissing,
                    $"No translation exists for template '{template.Id}' in '{payload.LanguageCode}' or the fallback '{LanguageCode.DefaultFallback}'."));
        }

        var u = urls.Value;
        var actionUrl = BuildActionUrl(u, templateType, payload.RawToken);

        // The expiry the recipient reads is their own wall clock in their
        // own date format. {{expires_at}} used to be ToString("u") with a
        // literal " UTC" bolted on in the DB copy, which asked a Czech
        // reader to convert "2026-08-24 18:30:00Z" in their head.
        var format = await ResolveCountryFormattingAsync(template.CountryCode, cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["action_url"] = actionUrl,
            ["expires_at"] = EmailFormatting.FormatDateTime(
                payload.ExpiresAt, format.Zone, EmailFormatting.CultureFor(translation.LanguageCode), format.DatePattern),
            // Machine-readable companion, deliberately untouched: it is not
            // rendered to a human and must stay zone-independent.
            ["expires_at_unix"] = payload.ExpiresAt.ToUnixTimeSeconds(),
            ["language_code"] = payload.LanguageCode,
        };

        var plainTextBody = SubstitutePlainTextPlaceholders(translation.PlainTextBody, data);

        var message = new EmailMessage(
            ProviderTemplateId: template.ProviderTemplateId,
            LanguageCode: translation.LanguageCode,
            ToAddress: payload.Email,
            ToName: null,
            FromAddress: template.FromAddress ?? string.Empty,
            FromName: template.FromName,
            ReplyToAddress: template.ReplyToAddress,
            Subject: translation.Subject,
            PlainTextBody: plainTextBody,
            Data: data)
        {
            HtmlBody = EmailHtmlLayout.Render(
                heading: translation.Subject,
                plainTextBody: plainTextBody,
                ctaLabel: EmailCallToAction.LabelFor(templateType, translation.LanguageCode),
                languageCode: translation.LanguageCode),
        };

        return await SendViaProviderAsync(message, cancellationToken);
    }

    // === Order-flow branches (T-0067). ===

    private async Task<BusinessResult<EmailSentReceipt>> SendOrderPaidCustomerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderPaidCustomerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderPaidCustomerEmail);
        if (!payloadResult.IsSuccess)
        {
            return BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!);
        }
        var payload = payloadResult.Value!;

        // T-0069: lookup-at-send-time + retry-based eventual consistency.
        // If the invoice.generate queue lost the FIFO race against the
        // send-email queue, the Invoice row may not exist yet (or may
        // exist with PdfBlobPath still null because IssueInvoice.Handler
        // is mid-flight). Return Transient(InvoiceNotYetRendered) so the
        // outbox retry policy re-delivers in 1m; the second attempt
        // almost always succeeds. Atomic enqueue of the 3 events stays
        // unchanged per locked decision 1.
        var invoice = await invoices.GetByOrderIdAsync(payload.OrderId, cancellationToken);
        if (invoice is null || string.IsNullOrWhiteSpace(invoice.PdfBlobPath))
        {
            logger.LogDebug(
                "OrderPaidCustomerEmail for order {OrderId}: invoice not yet rendered " +
                "(invoice null = {InvoiceNull}, PdfBlobPath null/blank = {PdfPathMissing}). " +
                "Returning Transient for outbox re-delivery.",
                payload.OrderId,
                invoice is null,
                invoice is not null && string.IsNullOrWhiteSpace(invoice.PdfBlobPath));
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Transient(BusinessErrorMessage.InvoiceNotYetRendered));
        }

        // T-0069 AC-5/AC-8: download the rendered PDF from blob. Azure
        // Storage SDK already retries transient blips internally; anything
        // surfacing here is data-integrity (blob missing) or
        // mis-configuration (auth) — both Permanent so the outbox parks
        // the row for ops attention rather than burning the retry budget.
        byte[] pdfBytes;
        try
        {
            var downloadResult = await blobStorage.DownloadAsync(
                BlobContainer.Invoices, invoice.PdfBlobPath!, cancellationToken);
            if (!downloadResult.IsSuccess)
            {
                logger.LogCritical(
                    "OrderPaidCustomerEmail for order {OrderId}: blob download failed for invoice {InvoiceNumber} " +
                    "at {BlobPath}: {Code}. Returning Permanent — data-integrity issue.",
                    payload.OrderId, invoice.InvoiceNumber, invoice.PdfBlobPath, downloadResult.Error!.Code);
                return BusinessResult.Failure<EmailSentReceipt>(
                    Error.Permanent(BusinessErrorMessage.InvoicePdfAttachmentDownloadFailed));
            }
            using var stream = downloadResult.Value!.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            pdfBytes = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "OrderPaidCustomerEmail for order {OrderId}: blob download threw for invoice {InvoiceNumber} " +
                "at {BlobPath}. Returning Permanent — data-integrity issue.",
                payload.OrderId, invoice.InvoiceNumber, invoice.PdfBlobPath);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.InvoicePdfAttachmentDownloadFailed));
        }

        // T-0069 AC-6: language-aware filename. Reuses payload.LanguageCode
        // (already pre-resolved at MarkOrderPaid enqueue time per T-0067).
        // English locale gets "invoice-..."; everything else (cs-CZ +
        // future Czech variants + fallback) gets "faktura-...".
        // OrderNumber is pre-baked into the payload by MarkOrderPaid.Handler at
        // T-0067 enqueue time — no second Order load needed. Reviewer DC1/ID5 fold.
        var filename = BuildInvoiceAttachmentFilename(payload.LanguageCode, payload.OrderNumber);
        var attachment = new Attachment(filename, pdfBytes, "application/pdf");

        return await DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderPaidCustomer,
            toAddress: payload.Email,
            toName: payload.ContactName,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["contact_name"] = payload.ContactName,
                ["total_amount_minor"] = payload.TotalAmountMinor,
                ["total_amount"] = FormatAmount(payload.TotalAmountMinor, payload.Currency),
                ["currency"] = payload.Currency,
                ["language_code"] = payload.LanguageCode,
            },
            attachment: attachment,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Filename for the invoice PDF attachment. English → <c>invoice-{n}.pdf</c>;
    /// everything else (cs-CZ + future Czech variants + the default fallback)
    /// → <c>faktura-{n}.pdf</c>. Per T-0069 locked decision 6: the filename
    /// surfaces in the customer's inbox preview pane BEFORE they open the
    /// attachment, so it gets the locale-aware label even though the PDF
    /// body itself is Czech-only at MVP.
    /// </summary>
    private static string BuildInvoiceAttachmentFilename(string languageCode, string orderNumber) =>
        languageCode == "en-US"
            ? $"invoice-{orderNumber}.pdf"
            : $"faktura-{orderNumber}.pdf";

    private Task<BusinessResult<EmailSentReceipt>> SendOrderPlacedMakerEmailAsync(
        string payloadJson, CancellationToken cancellationToken)
    {
        var payloadResult = DeserializeOrderPayload<OrderPlacedMakerEmailPayload>(
            payloadJson, OutboxEventTypes.OrderPlacedMakerEmail);
        if (!payloadResult.IsSuccess)
        {
            return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(payloadResult.Error!));
        }
        var payload = payloadResult.Value!;

        return DispatchOrderEmailAsync(
            templateType: EmailTemplateType.OrderPlacedMaker,
            toAddress: payload.MakerEmail,
            toName: null,
            languageCode: payload.LanguageCode,
            substitutions: new Dictionary<string, object>
            {
                ["action_url"] = payload.ActionUrl,
                ["order_id"] = payload.OrderId,
                ["order_number"] = payload.OrderNumber,
                ["maker_id"] = payload.MakerId,
                ["maker_email"] = payload.MakerEmail,
                ["total_amount_minor"] = payload.TotalAmountMinor,
                ["total_amount"] = FormatAmount(payload.TotalAmountMinor, payload.Currency),
                ["currency"] = payload.Currency,
                ["language_code"] = payload.LanguageCode,
            },
            // T-0069 AC-10: maker email does NOT carry the invoice PDF —
            // only the customer email does. Pass null explicitly so the
            // intent is on the call site, not implicit in a default.
            attachment: null,
            cancellationToken: cancellationToken);
    }

    private async Task<BusinessResult<EmailSentReceipt>> DispatchOrderEmailAsync(
        EmailTemplateType templateType,
        string toAddress,
        string? toName,
        string languageCode,
        IReadOnlyDictionary<string, object> substitutions,
        Attachment? attachment,
        CancellationToken cancellationToken)
    {
        var template = await templates.GetByTypeAsync(templateType, cancellationToken);
        if (template is null)
        {
            logger.LogError("EmailTemplate row missing for type {TemplateType}.", templateType);
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateNotFound,
                    $"No EmailTemplate row exists for type '{templateType}'."));
        }

        var translation = await ResolveTranslationAsync(template.Id, languageCode, templateType, cancellationToken);
        if (translation is null)
        {
            return BusinessResult.Failure<EmailSentReceipt>(
                Error.Permanent(BusinessErrorMessage.EmailTemplateTranslationMissing,
                    $"No translation exists for template '{template.Id}' in '{languageCode}' or the fallback '{LanguageCode.DefaultFallback}'."));
        }

        // Subject is run through the same placeholder-substitution path as
        // the body — order subjects carry the order number (T-0067 reviewer
        // B-1) so the customer's inbox shows "Děkujeme za objednávku #M-2026-…"
        // rather than the raw template literal. SendGrid's Dynamic Template
        // does NOT substitute the subject header from dynamicTemplateData,
        // so the producer must inline the substitutions itself.
        var subject = SubstitutePlainTextPlaceholders(translation.Subject, substitutions);
        var plainTextBody = SubstitutePlainTextPlaceholders(translation.PlainTextBody, substitutions);

        var message = new EmailMessage(
            ProviderTemplateId: template.ProviderTemplateId,
            LanguageCode: translation.LanguageCode,
            ToAddress: toAddress,
            ToName: toName,
            FromAddress: template.FromAddress ?? string.Empty,
            FromName: template.FromName,
            ReplyToAddress: template.ReplyToAddress,
            Subject: subject,
            PlainTextBody: plainTextBody,
            Data: substitutions.ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            // T-0069: customer invoice PDF rides as an inline attachment;
            // maker email passes null explicitly. Provider skips its
            // attachment SDK call when null.
            Attachment = attachment,

            // Every template type gets the same branded shell, derived from
            // the plain-text translation rather than hand-authored per type
            // — so the catalog stays one row of copy per language and no
            // email is left looking like a 1997 mail-merge.
            HtmlBody = EmailHtmlLayout.Render(
                heading: subject,
                plainTextBody: plainTextBody,
                ctaLabel: EmailCallToAction.LabelFor(templateType, translation.LanguageCode),
                languageCode: translation.LanguageCode),
        };

        return await SendViaProviderAsync(message, cancellationToken);
    }

    // === Helpers shared across branches. ===

    private static BusinessResult<T> DeserializeOrderPayload<T>(string payloadJson, string outboxEventType)
        where T : class
    {
        try
        {
            var payload = JsonSerializer.Deserialize<T>(payloadJson);
            if (payload is null)
            {
                return BusinessResult.Failure<T>(
                    Error.Permanent(BusinessErrorMessage.OrderEmailPayloadMalformed,
                        $"Outbox payload for '{outboxEventType}' deserialized to null."));
            }
            return BusinessResult.Success(payload);
        }
        catch (JsonException ex)
        {
            return BusinessResult.Failure<T>(
                Error.Permanent(BusinessErrorMessage.OrderEmailPayloadMalformed,
                    $"Outbox payload for '{outboxEventType}' could not be JSON-decoded: {ex.Message}"));
        }
    }

    /// <summary>
    /// Per-country date presentation, read from configuration rather than
    /// branched on a country code (CLAUDE.md §2.7). A missing row degrades
    /// to UTC + the default pattern: a slightly worse timestamp is a far
    /// better outcome than a parked outbox row on a password-reset email.
    /// </summary>
    private async Task<(TimeZoneInfo Zone, string DatePattern)> ResolveCountryFormattingAsync(
        string countryCode, CancellationToken cancellationToken)
    {
        var country = await countries.GetByCodeAsync(countryCode, cancellationToken);
        if (country is null)
        {
            logger.LogWarning(
                "CountryConfiguration row missing for {CountryCode}; email timestamps fall back to UTC.",
                countryCode);
            return (TimeZoneInfo.Utc, EmailFormatting.DefaultDatePattern);
        }

        return (EmailFormatting.TimeZoneFor(country.TimeZoneId), country.DateFormat);
    }

    private async Task<EmailTemplateTranslation?> ResolveTranslationAsync(
        string templateId, string languageCode, EmailTemplateType templateType, CancellationToken cancellationToken)
    {
        var translation = await translations.GetAsync(templateId, languageCode, cancellationToken);
        if (translation is null && languageCode != LanguageCode.DefaultFallback)
        {
            logger.LogInformation(
                "EmailTemplateTranslation missing for ({TemplateType}, {LanguageCode}); falling back to {Fallback}.",
                templateType, languageCode, LanguageCode.DefaultFallback);
            translation = await translations.GetAsync(templateId, LanguageCode.DefaultFallback, cancellationToken);
        }
        return translation;
    }

    private async Task<BusinessResult<EmailSentReceipt>> SendViaProviderAsync(
        EmailMessage message, CancellationToken cancellationToken)
    {
        var result = await provider.SendAsync(message, cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Email send failed via {Provider}: {Code} ({Type})",
                provider.Code, result.Error!.Code, result.Error.Type);
        }
        return result;
    }

    private Task<BusinessResult<EmailSentReceipt>> UnknownEventTypeAsync(string outboxEventType)
    {
        logger.LogWarning("Unknown outbox event type {EventType}.", outboxEventType);
        return Task.FromResult(BusinessResult.Failure<EmailSentReceipt>(
            Error.Permanent(BusinessErrorMessage.EmailEventTypeUnknown,
                $"No email template is mapped to outbox event '{outboxEventType}'.")));
    }

    private static EmailTemplateType MapAuthEventToTemplateType(string outboxEventType) =>
        outboxEventType switch
        {
            OutboxEventTypes.AuthMagicLinkSend => EmailTemplateType.AuthMagicLink,
            OutboxEventTypes.AuthEmailConfirmationSend => EmailTemplateType.AuthEmailConfirmation,
            OutboxEventTypes.AuthPasswordResetSend => EmailTemplateType.AuthPasswordReset,
            _ => throw new InvalidOperationException(
                $"MapAuthEventToTemplateType called with non-auth event '{outboxEventType}'. Switch above is wrong."),
        };

    private static string BuildActionUrl(PublicAppUrlsOptions u, EmailTemplateType type, string rawToken)
    {
        var pathTemplate = type switch
        {
            EmailTemplateType.AuthMagicLink         => u.MagicLinkPath,
            EmailTemplateType.AuthEmailConfirmation => u.EmailConfirmationPath,
            EmailTemplateType.AuthPasswordReset     => u.PasswordResetPath,
            _ => throw new InvalidOperationException($"No URL path mapped for {type}."),
        };
        // The PublicAppUrlsOptionsValidator (startup) guarantees pathTemplate
        // contains {token} — so Replace is always a real substitution here.
        // Sec reviewer M-1 closed at the validator layer rather than via a
        // post-replace check.
        var path = pathTemplate.Replace(PublicAppUrlsOptions.TokenPlaceholder, Uri.EscapeDataString(rawToken));
        var basePart = u.WebBaseUrl.TrimEnd('/');
        return path.StartsWith('/') ? basePart + path : $"{basePart}/{path}";
    }

    /// <summary>
    /// Format an amount in minor units as a display string (e.g. 57900 →
    /// "579,00"). The SendGrid Dynamic Template handles
    /// currency-symbol placement; the plain-text body just shows the
    /// number. <paramref name="currency"/> is accepted for forward
    /// compatibility but unused at MVP (CZK only). Plain-text only — no
    /// HTML in this catalog.
    /// </summary>
    private static string FormatAmount(long amountMinor, string currency)
    {
        _ = currency;
        var major = amountMinor / 100m;
        return major.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    // SECURITY: plain-text only. Do NOT reuse for HTML bodies — there is no
    // escaping, so any value-containing-{{key}} would produce an XSS-shaped
    // surprise. RULE: substitution VALUES must be trusted (URLs, timestamps,
    // language tags, our own ids/amounts). The sequential Replace loop means
    // a value containing "{{key}}" re-expands inside hostile text — so any
    // USER-SUPPLIED value (dispute description, resolution notes, future
    // free-text fields) MUST be passed through NeutralizePlaceholderSyntax
    // by the calling email branch BEFORE it enters the substitution map
    // (Gate 3 F1). The same map rides EmailMessage.Data to SendGrid dynamic
    // templates: template authors must use double-stache ({{description}},
    // never triple-stache) for user-supplied keys and render them under a
    // "text from the customer/maker" label.
    private static string SubstitutePlainTextPlaceholders(
        string body, IReadOnlyDictionary<string, object> data)
    {
        var result = body;
        foreach (var (key, value) in data)
            result = result.Replace($"{{{{{key}}}}}", value?.ToString() ?? string.Empty);
        return result;
    }

    /// <summary>
    /// SECURITY (Gate 3 F1): break placeholder syntax in user-supplied
    /// values before they enter the substitution map. "{{" becomes
    /// "{ {" so a hostile description containing e.g.
    /// <c>{{language_code}}</c> renders as inert text instead of being
    /// re-expanded by <see cref="SubstitutePlainTextPlaceholders"/> (or
    /// by a SendGrid template). Visual fidelity is near-identical; the
    /// content is plain-text triage/notification copy.
    /// </summary>
    private static string NeutralizePlaceholderSyntax(string value)
        => value.Replace("{{", "{ {");
}
