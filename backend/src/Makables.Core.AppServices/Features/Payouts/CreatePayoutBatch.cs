using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Features.Auth;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Observability;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payouts;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Claim every payout-eligible <see cref="OrderState.Delivered"/> order for
/// the default country into a new immutable <see cref="PayoutBatch"/> born
/// in <see cref="PayoutBatchState.Processing"/>, sum each order's
/// <c>MakerPayoutAmountMinor</c> into the batch total, and surface what was
/// excluded and why (Q3 partial-refunds, Q5 NULL-bank-account makers). The
/// money it writes is the amount the operator wires from the company bank
/// account (US-admin-0007). T-0102a.
///
/// <para>
/// <b>Money-aggregation admin command</b> (security_touching). Fail-closed
/// session check first (RefundOrder precedent — money movement is never
/// attributed to "system"). The claim + batch insert + audit row commit
/// atomically in ONE UoW (ADR 0014); the handler never calls
/// <c>SaveChangesAsync()</c>.
/// </para>
///
/// <para>
/// <b>Idempotency.</b> A re-run while a <see cref="PayoutBatchState.Processing"/>
/// batch is open returns THAT batch (<c>AlreadyExisted = true</c>, Silent
/// Success — T-0067/T-0076/T-0105 precedent), never a second row. A
/// same-ISO-week re-run after the batch completed returns
/// <see cref="BusinessErrorMessage.PayoutBatchWeekAlreadyProcessed"/>. An
/// empty run returns <see cref="BusinessErrorMessage.PayoutBatchEmpty"/>
/// with NO row.
/// </para>
///
/// <para>
/// <b>Artifacts (T-0102b).</b> After the claim/lookup the handler calls
/// <see cref="IPayoutArtifactService.GenerateAsync"/> to issue per-maker Fee
/// invoices, render PDFs, build the bank CSV, and enqueue maker emails. An
/// artifact failure must NOT unclaim the batch — the handler maps the
/// (already-caught) result onto the response and still returns success so
/// the claim + completed artifacts commit; the re-run path resumes the rest.
/// </para>
/// </summary>
public static class CreatePayoutBatch
{
    public sealed record Command : ICommand<CreatePayoutBatchResponse>;

    /// <summary>
    /// <b>Globally-unique name</b> (not nested <c>Response</c>) per the
    /// post-PR-#38 NSwag convention.
    /// </summary>
    public sealed record CreatePayoutBatchResponse(
        string BatchId,
        string BatchNumber,
        PayoutBatchState State,
        long TotalAmountMinor,
        string Currency,
        int OrderCount,
        int MakerCount,
        int ExcludedPartiallyRefundedOrderCount,
        int ExcludedNoBankAccountOrderCount,
        int ExcludedNoBankAccountMakerCount,
        bool AlreadyExisted,
        bool ArtifactsComplete,
        int FeeInvoiceCount,
        bool CsvReady,
        long DeductionsAppliedMinor,
        int DeductionCount);

    /// <summary>
    /// No-op validator — the command is parameterless (it claims everything
    /// eligible for the default country). Present so the one-file feature
    /// shape stays uniform and the validation pipeline behaviour has a hook
    /// if a future parameter lands.
    /// </summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            // Nothing to validate — the command carries no input.
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IPayoutBatchRepository payoutBatches,
        IPayoutDeductionRepository payoutDeductions,
        IPayoutBatchNumberGenerator numberGenerator,
        ICountryConfigurationRepository countries,
        IOptions<AuthDefaultCountryOptions> defaultCountry,
        IAdminAuditLogWriter auditWriter,
        IUserSessionProvider session,
        IIdGenerator idGenerator,
        IClock clock,
        IPayoutMetrics metrics,
        IPayoutArtifactService artifactService,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<CreatePayoutBatchResponse>>
    {
        public async Task<BusinessResult<CreatePayoutBatchResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: fail-closed session check (RefundOrder precedent) — a
            // privileged money movement is never attributed to "system".
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<CreatePayoutBatchResponse>(Error.Unauthorized());
            }

            // Step 2: resolve the default country + its configuration.
            var countryCode = defaultCountry.Value.CountryCodePrimary;
            var config = await countries.GetByCodeAsync(countryCode, cancellationToken);
            if (config is null)
            {
                logger.LogCritical(
                    "CreatePayoutBatch: CountryConfiguration {CountryCode} not found — refusing to run.",
                    countryCode);
                return BusinessResult.Failure<CreatePayoutBatchResponse>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigMissing));
            }

            // Step 3: re-run guard — an open Processing batch is returned
            // verbatim (Silent Success). The artifact service resumes any
            // missing artifacts on this path (T-0102b re-entrancy).
            var openBatch = await payoutBatches.GetOpenBatchAsync(countryCode, cancellationToken);
            if (openBatch is not null)
            {
                metrics.RecordRun("already_open");
                // Re-run path: the claim is committed; the service reads it
                // from the DB (pass null).
                var artifacts = await artifactService.GenerateAsync(openBatch, claimedOrders: null, cancellationToken);
                if (!artifacts.Complete)
                {
                    logger.LogCritical(
                        "CreatePayoutBatch: re-run of open batch {BatchNumber} left artifacts incomplete " +
                        "({FeeInvoiceCount} fee invoices, CsvReady={CsvReady}).",
                        openBatch.BatchNumber, artifacts.FeeInvoiceCount, artifacts.CsvReady);
                }
                return BusinessResult.Success(BuildResponse(
                    openBatch, alreadyExisted: true, artifacts, deductionsAppliedMinor: 0, deductionCount: 0));
            }

            // Step 4: derive the batch number from the country-LOCAL date
            // (TZ-aware per T-0062/T-0068a) so a Sunday 23:30 UTC run buckets
            // into Monday's ISO week.
            var localDate = ToCountryLocalDate(clock.UtcNow, config.TimeZoneId);
            var batchNumber = numberGenerator.For(countryCode, localDate);

            // Step 5: week guard — a closed batch already exists for this
            // ISO week. Pre-check so the unique-index race surfaces as a
            // typed Conflict, not a raw 500.
            var existingForWeek = await payoutBatches.GetByNumberAsync(countryCode, batchNumber, cancellationToken);
            if (existingForWeek is not null)
            {
                metrics.RecordRun("week_already_processed");
                return BusinessResult.Failure<CreatePayoutBatchResponse>(
                    Error.Conflict("batchNumber", BusinessErrorMessage.PayoutBatchWeekAlreadyProcessed));
            }

            // Step 6: load candidates + classify into eligible / excluded.
            var candidates = await orders.GetPayoutEligibleUnscopedAsync(countryCode, cancellationToken);

            var eligible = new List<PayoutCandidate>();
            var excludedRefundedOrderCount = 0;
            var excludedNoBankOrderCount = 0;
            var excludedNoBankMakerIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var candidate in candidates)
            {
                var verdict = PayoutEligibility.Classify(
                    candidate.Order.State,
                    candidate.Order.PayoutBatchId,
                    candidate.Order.RefundedAmountMinor,
                    candidate.MakerBankAccount);

                switch (verdict)
                {
                    case PayoutEligibilityVerdict.Eligible:
                        eligible.Add(candidate);
                        break;
                    case PayoutEligibilityVerdict.ExcludedPartiallyRefunded:
                        excludedRefundedOrderCount++;
                        break;
                    case PayoutEligibilityVerdict.ExcludedNoBankAccount:
                        excludedNoBankOrderCount++;
                        excludedNoBankMakerIds.Add(candidate.MakerId);
                        break;
                    case PayoutEligibilityVerdict.NotClaimable:
                    default:
                        break;
                }
            }

            var excludedNoBankMakerCount = excludedNoBankMakerIds.Count;

            // Step 7: empty run — no row, structured warning, metrics. The
            // exclusion counts tell ops WHY it was empty.
            if (eligible.Count == 0)
            {
                logger.LogWarning(
                    "CreatePayoutBatch: no eligible orders for {CountryCode}. " +
                    "Excluded: partiallyRefunded={Refunded}, noBankOrders={NoBankOrders}, noBankMakers={NoBankMakers}.",
                    countryCode, excludedRefundedOrderCount, excludedNoBankOrderCount, excludedNoBankMakerCount);
                metrics.RecordRun("empty");
                metrics.RecordExcluded("partially_refunded", excludedRefundedOrderCount);
                metrics.RecordExcluded("no_bank_account", excludedNoBankOrderCount);
                return BusinessResult.Failure<CreatePayoutBatchResponse>(
                    Error.Conflict("orders", BusinessErrorMessage.PayoutBatchEmpty));
            }

            // Step 8: currency-homogeneity guard. Summing mixed currencies
            // into one TotalAmountMinor would silently corrupt the wire
            // amount. Batch currency = the country's default currency.
            var currency = config.DefaultCurrencyCode;
            if (eligible.Any(c => !string.Equals(c.Order.Currency, currency, StringComparison.Ordinal)))
            {
                logger.LogCritical(
                    "CreatePayoutBatch: eligible orders for {CountryCode} do not all use {Currency}.",
                    countryCode, currency);
                metrics.RecordRun("currency_mismatch");
                return BusinessResult.Failure<CreatePayoutBatchResponse>(
                    Error.Conflict("currency", BusinessErrorMessage.PayoutBatchCurrencyMismatch));
            }

            // Step 9: claim (ONE UoW). Build the batch born Processing, then
            // assign each eligible order. A refusal in AssignToPayoutBatch is
            // a programmer error (the claim query already filtered
            // PayoutBatchId == null) — it throws, the UoW rolls everything
            // back.
            var total = eligible.Sum(c => c.Order.MakerPayoutAmountMinor);
            var orderCount = eligible.Count;
            var makerIds = eligible.Select(c => c.MakerId).Distinct(StringComparer.Ordinal).ToList();
            var makerCount = makerIds.Count;

            // Step 8b (T-0146 / Q-0037): every eligible maker's pending
            // PayoutDeduction rows (e.g. a return-shipment cost from
            // GenerateReturnLabel) are claimed into THIS batch — subtracted
            // from the wire total the operator sends. Clamped so a
            // deduction total can never zero out or invert the batch
            // (defensive; expected deductions are small relative to a
            // week's claimed orders).
            var pendingDeductions = new List<PayoutDeduction>();
            foreach (var makerId in makerIds)
            {
                var pending = await payoutDeductions.GetPendingForMakerAsync(makerId, cancellationToken);
                pendingDeductions.AddRange(pending);
            }
            var deductionsTotal = pendingDeductions.Sum(d => d.AmountMinor);
            if (deductionsTotal >= total)
            {
                logger.LogCritical(
                    "CreatePayoutBatch: pending deductions ({DeductionsTotal}) for {CountryCode} would consume " +
                    "the entire claimed total ({Total}) — capping at total-1 minor unit.",
                    deductionsTotal, countryCode, total);
                deductionsTotal = total - 1;
            }
            total -= deductionsTotal;

            var batch = PayoutBatch.Create(
                id: idGenerator.Next(),
                batchNumber: batchNumber,
                countryCode: countryCode,
                totalAmountMinor: total,
                currency: currency,
                orderCount: orderCount,
                makerCount: makerCount,
                excludedPartiallyRefundedOrderCount: excludedRefundedOrderCount,
                excludedNoBankAccountOrderCount: excludedNoBankOrderCount,
                excludedNoBankAccountMakerCount: excludedNoBankMakerCount);

            await payoutBatches.AddAsync(batch, cancellationToken);

            var claimedOrders = new List<Order>(eligible.Count);
            foreach (var candidate in eligible)
            {
                candidate.Order.AssignToPayoutBatch(batch.Id);
                claimedOrders.Add(candidate.Order);
            }

            // Claim the deductions into this batch (set-once FK) now that
            // batch.Id is known. A refusal (already applied) is a
            // programmer error — GetPendingForMakerAsync already filtered
            // PayoutBatchId == null — so it throws and the UoW rolls back,
            // mirroring the AssignToPayoutBatch guard above.
            foreach (var deduction in pendingDeductions)
            {
                var applyResult = deduction.ApplyToPayoutBatch(batch.Id);
                if (!applyResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"PayoutDeduction {deduction.Id} was already applied to a batch — " +
                        "GetPendingForMakerAsync should never have returned it.");
                }
            }

            // Step 10: artifacts (T-0102b) — Fee invoices + CSV + maker
            // emails, all DB writes inside this same UoW (blob uploads are
            // non-transactional but overwrite-safe). The just-claimed orders
            // are passed in because the UoW has not committed them yet (a
            // fresh DB query would see nothing). A failure is caught inside
            // the service; we log Critical and still commit the claim.
            var artifactResult = await artifactService.GenerateAsync(batch, claimedOrders, cancellationToken);
            if (!artifactResult.Complete)
            {
                logger.LogCritical(
                    "CreatePayoutBatch: batch {BatchNumber} claimed but artifacts incomplete " +
                    "({FeeInvoiceCount} fee invoices, CsvReady={CsvReady}). Re-run completes the rest.",
                    batch.BatchNumber, artifactResult.FeeInvoiceCount, artifactResult.CsvReady);
            }

            // Step 11: audit + metrics. The handler-written row carries the
            // real batch id + a run-summary afterJson (§C.7) — a create
            // command cannot name its TargetId at command time, so this is
            // NOT an IAdminAuditableCommand. Rides the same UoW.
            var afterJson = JsonSerializer.Serialize(new
            {
                batch.BatchNumber,
                batch.TotalAmountMinor,
                batch.Currency,
                batch.OrderCount,
                batch.MakerCount,
                batch.ExcludedPartiallyRefundedOrderCount,
                batch.ExcludedNoBankAccountOrderCount,
                batch.ExcludedNoBankAccountMakerCount,
                ArtifactsComplete = artifactResult.Complete,
                FeeInvoiceCount = artifactResult.FeeInvoiceCount,
                CsvReady = artifactResult.CsvReady,
                DeductionsAppliedMinor = deductionsTotal,
                DeductionCount = pendingDeductions.Count,
            });

            await auditWriter.AppendAsync(
                AdminAuditLogEntry.Record(
                    id: idGenerator.Next(),
                    adminUserId: session.GetUserId()!,
                    actionCode: "payoutBatch.create",
                    targetEntity: "payout_batch",
                    targetId: batch.Id,
                    beforeJson: null,
                    afterJson: afterJson,
                    now: clock.UtcNow,
                    notes: null),
                cancellationToken);

            metrics.RecordRun("created");
            metrics.RecordClaimed(orderCount, total);
            metrics.RecordExcluded("partially_refunded", excludedRefundedOrderCount);
            metrics.RecordExcluded("no_bank_account", excludedNoBankOrderCount);

            // The pipeline commits batch + N claims + fee invoices + outbox +
            // audit atomically (ADR 0014).
            return BusinessResult.Success(BuildResponse(
                batch, alreadyExisted: false, artifactResult,
                deductionsAppliedMinor: deductionsTotal, deductionCount: pendingDeductions.Count));
        }

        private static CreatePayoutBatchResponse BuildResponse(
            PayoutBatch batch, bool alreadyExisted, PayoutArtifactResult artifacts,
            long deductionsAppliedMinor, int deductionCount) => new(
            BatchId: batch.Id,
            BatchNumber: batch.BatchNumber,
            State: batch.State,
            TotalAmountMinor: batch.TotalAmountMinor,
            Currency: batch.Currency,
            OrderCount: batch.OrderCount,
            MakerCount: batch.MakerCount,
            ExcludedPartiallyRefundedOrderCount: batch.ExcludedPartiallyRefundedOrderCount,
            ExcludedNoBankAccountOrderCount: batch.ExcludedNoBankAccountOrderCount,
            ExcludedNoBankAccountMakerCount: batch.ExcludedNoBankAccountMakerCount,
            AlreadyExisted: alreadyExisted,
            ArtifactsComplete: artifacts.Complete,
            FeeInvoiceCount: artifacts.FeeInvoiceCount,
            CsvReady: artifacts.CsvReady || batch.CsvBlobPath is not null,
            DeductionsAppliedMinor: deductionsAppliedMinor,
            DeductionCount: deductionCount);

        private static DateOnly ToCountryLocalDate(DateTimeOffset instant, string timeZoneId)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var local = TimeZoneInfo.ConvertTime(instant, tz);
            return DateOnly.FromDateTime(local.DateTime);
        }
    }
}
