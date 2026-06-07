using Makables.Core.Domain.Rendering;
using Makables.Infra.PdfRendering;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

namespace Makables.Config.Extensions;

/// <summary>
/// Registers <see cref="IInvoicePdfRenderer"/> backed by
/// <see cref="QuestPdfInvoiceRenderer"/>. Pins QuestPDF Community
/// license per T-0068b locked decision 1 / ADR 0025. Called by every
/// API host's <c>Program.cs</c> and by <c>Makables.Functions/Program.cs</c>
/// (the <c>GenerateInvoiceFunction</c> in T-0069 dispatches
/// <c>IssueInvoice</c> via Mediator).
///
/// <para>
/// Singleton lifetime — the renderer is stateless + thread-safe and
/// QuestPDF document templates allocate per-call.
/// </para>
/// </summary>
public static class MakablesPdfRenderingExtensions
{
    public static IServiceCollection AddMakablesPdfRendering(
        this IServiceCollection services)
    {
        // Idempotent — assigning the static License flag twice is harmless.
        // Pinned both here and in QuestPdfInvoiceRenderer's static ctor so
        // a host that constructs the renderer directly (bypassing DI) also
        // ends up with the right license posture.
        QuestPDF.Settings.License = LicenseType.Community;

        services.AddSingleton<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();

        return services;
    }
}
