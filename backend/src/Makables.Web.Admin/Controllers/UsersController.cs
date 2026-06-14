using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Users;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin user endpoints (T-0110 / US-admin-0016). The GDPR hard-delete is
/// the only irreversible operation in the system — <c>POST .../erase</c>
/// (not <c>DELETE</c>) names the side-effecting, matrix-running, NON-
/// idempotent operation honestly. <c>[Authorize]</c> under the admin
/// audience (ADR 0013); the command implements <c>IAdminAuditableCommand</c>
/// so the before/after JSONB + reason ride the pipeline (ADR 0014). Security
/// review + architect sign-off required on the PR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
public sealed class UsersController : MakablesApiController
{
    /// <summary>Request body for <see cref="Erase"/>. The user id rides the route.</summary>
    public sealed record EraseUserRequest(string ConfirmedEmail, string Reason);

    /// <summary>
    /// GDPR-erase a user (US-admin-0016): runs the anonymize + hard-delete
    /// matrix in one transaction. Requires <c>confirmedEmail</c> to match the
    /// user's email (retype gate) and is rejected (409) while the user has any
    /// in-flight order. A re-call after erasure resolves to <c>user.notFound</c>
    /// (no Silent-Success — the op is irreversible).
    /// </summary>
    [HttpPost("{id}/erase")]
    [ProducesResponseType(typeof(DeleteUserPermanently.DeleteUserPermanentlyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Erase(
        string id, [FromBody] EraseUserRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new DeleteUserPermanently.Command(id, request.ConfirmedEmail, request.Reason), ct));
}
