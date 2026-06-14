using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Settle a payout batch (US-admin-0007 AC-2 / US-maker-0012): the operator
/// has executed the bank wire described by the batch's CSV and records that
/// fact. The command moves the batch <see cref="PayoutBatchState.Processing"/>
/// → <see cref="PayoutBatchState.Completed"/>, drives every claimed order
/// <see cref="OrderState.Delivered"/> → <see cref="OrderState.Completed"/> in
/// the SAME UoW, and enqueues exactly one payout-sent email per distinct
/// maker. T-0103.
///
/// <para>
/// <b>Materialized loop, NOT per-order mediator.Send</b> (Q2 / Q-0008 MARS
/// lesson): the claimed orders are already inside the batch's transactional
/// scope; <see cref="Order.Complete"/> is a pure in-memory transition. A
/// nested <c>Send</c> would re-enter <c>UnitOfWorkPipelineBehavior</c> and
/// risk a Multiple Active Result Sets fault on the shared DbContext. One pass
/// completes each order AND accumulates the per-maker totals.
/// </para>
///
/// <para>
/// <b>Money-terminal admin command</b> (security_touching). Fail-closed
/// session check first (RefundOrder precedent — settlement is never "system").
/// <b>Silent-Success</b> on an already-Completed batch (T-0067/T-0076/T-0105
/// precedent): 200 echoing the stored facts with <c>AlreadyCompleted = true</c>,
/// no re-transition, no order re-completion, no email re-emit, no second audit
/// row. Audited via <see cref="IAdminAuditableCommand"/> (the batch id is known
/// at command time). The batch transition + N order transitions + N outbox
/// rows + the audit row commit atomically in ONE UoW (ADR 0014); the handler
/// never calls <c>SaveChangesAsync()</c>.
/// </para>
///
/// <para>
/// <b>Forward-only</b> (reversibility lock): there is no un-complete. Once
/// Completed the Fee invoices are immutable, the wire executed, the emails
/// dispatched, and the post-payout refund gate armed. Errors are corrected
/// forward via T-0105 (refund) / T-0107 (manual state change).
/// </para>
/// </summary>
public static class MarkPayoutBatchCompleted
{
    public sealed record Command(
        string PayoutBatchId,
        string BankReference,
        DateOnly? PaymentDate)
        : ICommand<MarkPayoutBatchCompletedResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "payoutBatch.complete";
        public string TargetEntity => "payout_batch";
        public string TargetId => PayoutBatchId;
        public string? Notes => BankReference;
    }

    /// <summary>
    /// <b>Globally-unique name</b> (not nested <c>Response</c>) per the
    /// post-PR-#38 NSwag convention — avoids the TS schema collision.
    /// </summary>
    public sealed record MarkPayoutBatchCompletedResponse(
        string BatchId,
        PayoutBatchState State,
        DateTimeOffset CompletedAt,
        string BankReference,
        int OrderCount,
        int MakerCount,
        long TotalAmountMinor,
        string Currency,
        bool AlreadyCompleted);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.PayoutBatchId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Caps to the column width so an oversize payload is a clean 400,
            // not a SaveChanges 500 (RefundOrder reason precedent).
            RuleFor(c => c.BankReference)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(PayoutBatch.MaxBankReferenceLength).WithErrorCode(BusinessErrorMessage.MaxLength);

            // PaymentDate needs no rule — any valid DateOnly is acceptable;
            // a future value-date is the operator's call.
        }
    }

    public sealed class Handler(
        IPayoutBatchRepository payoutBatches,
        IOrderRepository orders,
        IMakerRepository makers,
        Makables.Core.Domain.Identity.IUserRepository users,
        IOutbox outbox,
        IClock clock,
        ILanguageResolver languageResolver,
        IOptions<PublicAppUrlsOptions> publicAppUrls,
        IUserSessionProvider session,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<MarkPayoutBatchCompletedResponse>>
    {
        public async Task<BusinessResult<MarkPayoutBatchCompletedResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: fail-closed session check (RefundOrder precedent) — a
            // money settlement is never attributed to "system".
            var adminUserId = session.GetUserId();
            if (string.IsNullOrEmpty(adminUserId))
            {
                return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(Error.Unauthorized());
            }

            // Step 2: tracked unscoped load (admin host only, ADR 0013).
            var batch = await payoutBatches.GetByIdUnscopedAsync(command.PayoutBatchId, cancellationToken);
            if (batch is null)
            {
                return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(
                    Error.NotFound("id", BusinessErrorMessage.PayoutBatchNotFound));
            }

            // Step 3: Silent-Success on an already-Completed batch — echo the
            // stored facts, no transition / order loop / email / audit. The
            // first settlement is authoritative; the second call's bank ref +
            // date are ignored (makes the timer/retry path safe).
            if (batch.State == PayoutBatchState.Completed)
            {
                logger.LogInformation(
                    "MarkPayoutBatchCompleted: batch {BatchNumber} already Completed; idempotent skip.",
                    batch.BatchNumber);
                return BusinessResult.Success(BuildResponse(batch, orderCount: batch.OrderCount,
                    makerCount: batch.MakerCount, alreadyCompleted: true));
            }

            // Step 4: transition the batch (only PayoutBatchNotProcessing is
            // reachable; the already-Completed branch was handled above).
            var transition = batch.Complete(clock, command.BankReference, command.PaymentDate, adminUserId);
            if (!transition.IsSuccess)
            {
                return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(transition.Error!);
            }

            // Step 5: materialize the claimed orders (tracked).
            var claimedOrders = await orders.GetByPayoutBatchIdForCompletionUnscopedAsync(
                batch.Id, cancellationToken);

            // Step 6: single pass — complete each order + accumulate per-maker
            // totals. NO per-order mediator.Send (Q-0008 MARS lesson).
            var perMaker = new Dictionary<string, MakerAccumulator>(StringComparer.Ordinal);
            foreach (var order in claimedOrders)
            {
                var completion = order.Complete(clock);
                if (!completion.IsSuccess)
                {
                    // Belt-and-braces — all claimed orders are Delivered by the
                    // claim invariant; a refusal is a programmer error. Surface
                    // it so the UoW rolls back everything.
                    logger.LogCritical(
                        "MarkPayoutBatchCompleted: order {OrderId} in batch {BatchNumber} refused Complete with {Code}. " +
                        "Rolling back the settlement.",
                        order.Id, batch.BatchNumber, completion.Error!.Code);
                    return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(completion.Error!);
                }

                if (!perMaker.TryGetValue(order.MakerId, out var acc))
                {
                    acc = new MakerAccumulator();
                    perMaker[order.MakerId] = acc;
                }
                acc.TotalPaidMinor += order.MakerPayoutAmountMinor;
                acc.OrderCount++;
            }

            // Step 7: one payout-sent email per distinct maker (Q3).
            var feeInvoiceBaseUrl = publicAppUrls.Value.WebBaseUrl.TrimEnd('/');
            foreach (var (makerId, acc) in perMaker)
            {
                var maker = await makers.GetByIdAsync(makerId, cancellationToken);
                if (maker is null)
                {
                    // FK invariant violation — a claimed order's maker must
                    // exist. Refuse to commit so the inconsistency surfaces.
                    logger.LogCritical(
                        "MarkPayoutBatchCompleted: maker {MakerId} for batch {BatchNumber} not found at email enqueue. " +
                        "Refusing to commit.",
                        makerId, batch.BatchNumber);
                    return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(
                        Error.NotFound("makerId", BusinessErrorMessage.MakerNotFound));
                }

                var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
                if (makerUser is null)
                {
                    logger.LogCritical(
                        "MarkPayoutBatchCompleted: maker {MakerId} user {UserId} missing at email enqueue (batch {BatchNumber}). " +
                        "Refusing to commit.",
                        makerId, maker.UserId, batch.BatchNumber);
                    return BusinessResult.Failure<MarkPayoutBatchCompletedResponse>(
                        Error.NotFound("userId", BusinessErrorMessage.MakerNotFound));
                }

                var language = await languageResolver.ResolveForUserAsync(makerUser, cancellationToken);
                var payload = new PayoutBatchPayoutSentMakerEmailPayload(
                    MakerId: maker.Id,
                    MakerEmail: makerUser.Email,
                    BatchNumber: batch.BatchNumber,
                    MakerTotalPaidMinor: acc.TotalPaidMinor,
                    Currency: batch.Currency,
                    OrderCount: acc.OrderCount,
                    FeeInvoiceActionUrl: $"{feeInvoiceBaseUrl}/dashboard/maker/vyplaty/{batch.Id}",
                    LanguageCode: language);

                outbox.Enqueue(
                    aggregateId: batch.Id,
                    eventType: OutboxEventTypes.PayoutBatchPayoutSentMakerEmail,
                    payloadJson: JsonSerializer.Serialize(payload));
            }

            // Step 8: the pipeline commits batch + N order transitions + N
            // outbox rows + the payoutBatch.complete audit row atomically
            // (ADR 0014).
            return BusinessResult.Success(BuildResponse(batch, claimedOrders.Count, perMaker.Count,
                alreadyCompleted: false));
        }

        private static MarkPayoutBatchCompletedResponse BuildResponse(
            PayoutBatch batch, int orderCount, int makerCount, bool alreadyCompleted) => new(
            BatchId: batch.Id,
            State: batch.State,
            // CompletedAt is non-null on both paths (Completed batch has it set).
            CompletedAt: batch.CompletedAt!.Value,
            BankReference: batch.BankReference!,
            OrderCount: orderCount,
            MakerCount: makerCount,
            TotalAmountMinor: batch.TotalAmountMinor,
            Currency: batch.Currency,
            AlreadyCompleted: alreadyCompleted);

        private sealed class MakerAccumulator
        {
            public long TotalPaidMinor { get; set; }
            public int OrderCount { get; set; }
        }
    }
}
