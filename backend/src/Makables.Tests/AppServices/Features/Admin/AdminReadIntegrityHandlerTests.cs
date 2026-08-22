using FluentAssertions;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.Admin;

/// <summary>
/// T-0177 + T-0178 (admin read-integrity bundle).
///
/// <para>
/// T-0177 (audit ADM-H2): the order-detail audit trail fetched the GLOBAL
/// <c>targetEntity:'order'</c> slice and filtered it client-side, so an
/// order's evidence trail could render EMPTY while its entries sat on
/// later pages. The filter must reach the query.
/// </para>
///
/// <para>
/// T-0178 (audit ADM-H1/M9): the GDPR erase ran on unverified pasted
/// identifiers, and a typo was reported as "already deleted" — a false
/// compliance signal. Lookup resolves server-side, and "not found" and
/// "already erased" must stay distinguishable.
/// </para>
/// </summary>
public sealed class AdminReadIntegrityHandlerTests
{
    private readonly IAdminQueries _admin = Substitute.For<IAdminQueries>();

    private static AdminUserLookupDto Dto(
        string userId = "user-1",
        bool isActive = true,
        DateTimeOffset? deactivatedAt = null,
        int inFlight = 0) =>
        new(userId, "anna@example.cz", "Anna Nováková", "Customer", "CZ",
            isActive, EmailConfirmed: true, deactivatedAt,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30), MakerId: null, inFlight);

    // === T-0177 — audit trail scoped to one entity ===

    [Fact]
    public async Task Audit_log_passes_targetId_through_to_the_query_filter()
    {
        _admin.GetAdminAuditLogPagedAsync(
                Arg.Any<AdminAuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(PagedData<AdminAuditLogItemDto>.Empty(1, 20));
        var handler = new GetAdminAuditLog.Handler(_admin);

        await handler.Handle(
            new GetAdminAuditLog.Query(1, 20, null, null, "order", null, null, TargetId: "order-42"),
            default);

        await _admin.Received(1).GetAdminAuditLogPagedAsync(
            Arg.Is<AdminAuditLogFilter>(f => f.TargetId == "order-42" && f.TargetEntity == "order"),
            1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Audit_log_validator_bounds_targetId_length()
    {
        var validator = new GetAdminAuditLog.Validator();

        validator.Validate(new GetAdminAuditLog.Query(
            1, 20, null, null, null, null, null, new string('x', 41))).IsValid.Should().BeFalse();
        validator.Validate(new GetAdminAuditLog.Query(
            1, 20, null, null, null, null, null, "order-42")).IsValid.Should().BeTrue();
        // Absent stays valid — the global log is still a legitimate read.
        validator.Validate(new GetAdminAuditLog.Query(
            1, 20, null, null, null, null, null, null)).IsValid.Should().BeTrue();
    }

    // === T-0178 — user lookup behind the GDPR erase ===

    [Fact]
    public async Task Lookup_returns_the_server_resolved_identity()
    {
        _admin.LookupUserAsync(null, "anna@example.cz", Arg.Any<CancellationToken>())
            .Returns(Dto(inFlight: 2));
        var handler = new LookupAdminUser.Handler(_admin);

        var result = await handler.Handle(
            new LookupAdminUser.Query(null, "anna@example.cz"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User.UserId.Should().Be("user-1");
        result.Value.User.Email.Should().Be("anna@example.cz");
        result.Value.User.InFlightOrderCount.Should().Be(2);
    }

    [Fact]
    public async Task Lookup_of_an_unknown_identifier_is_notFound_not_already_erased()
    {
        _admin.LookupUserAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AdminUserLookupDto?)null);
        var handler = new LookupAdminUser.Handler(_admin);

        var result = await handler.Handle(new LookupAdminUser.Query("typo-id", null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.UserNotFound);
    }

    [Fact]
    public async Task An_already_erased_account_still_resolves_so_the_UI_can_say_so()
    {
        var erasedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _admin.LookupUserAsync("user-1", null, Arg.Any<CancellationToken>())
            .Returns(Dto(isActive: false, deactivatedAt: erasedAt));
        var handler = new LookupAdminUser.Handler(_admin);

        var result = await handler.Handle(new LookupAdminUser.Query("user-1", null), default);

        result.IsSuccess.Should().BeTrue("an erased account is a real answer, not a 404");
        result.Value!.User.IsActive.Should().BeFalse();
        result.Value.User.DeactivatedAt.Should().Be(erasedAt);
    }

    [Theory]
    [InlineData(null, null)]              // neither — unanswerable
    [InlineData("user-1", "a@b.cz")]      // both — ambiguous
    [InlineData("", "")]
    public void Lookup_validator_requires_exactly_one_selector(string? userId, string? email)
    {
        new LookupAdminUser.Validator()
            .Validate(new LookupAdminUser.Query(userId, email)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Lookup_validator_accepts_either_selector_alone_and_rejects_a_malformed_email()
    {
        var validator = new LookupAdminUser.Validator();

        validator.Validate(new LookupAdminUser.Query("user-1", null)).IsValid.Should().BeTrue();
        validator.Validate(new LookupAdminUser.Query(null, "anna@example.cz")).IsValid.Should().BeTrue();
        validator.Validate(new LookupAdminUser.Query(null, "not-an-email")).IsValid.Should().BeFalse();
    }
}
