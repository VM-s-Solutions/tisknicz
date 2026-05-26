using Asp.Versioning;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.AppServices.Features.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Config.Controllers.Profile;

/// <summary>
/// Authenticated self-service profile endpoints. The User-level shape
/// (full name, phone, email-confirmed, role) is shared across customer
/// and maker hosts; the Maker-level shape (bio, bank account, pickup
/// toggle/note, ARES snapshot view) is reached via the
/// <c>/me/maker</c> sub-route — calling that on the customer host
/// returns 404 (the User has no maker row).
///
/// <para>
/// Audience isolation is enforced by JWT validation: a customer JWT
/// (aud=customer) cannot be replayed on the maker host. The controller
/// itself only needs <c>[Authorize]</c>.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public sealed class ProfileController : MakablesApiController
{
    public sealed record UpdateProfileRequest(string FullName, string? Phone);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record UpdateMakerProfileRequest(
        string? Bio,
        string? BankAccount,
        bool? PersonalPickupEnabled,
        string? PickupNote);

    [HttpGet]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetMyProfile.Query(), ct);
        return HandleResult(result);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateUserProfile.Command(body.FullName, body.Phone), ct);
        return HandleResult(result);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new ChangePassword.Command(body.CurrentPassword, body.NewPassword), ct);
        return HandleResult(result);
    }

    [HttpGet("maker")]
    public async Task<IActionResult> Maker(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetMyMakerProfile.Query(), ct);
        return HandleResult(result);
    }

    [HttpPut("maker")]
    public async Task<IActionResult> UpdateMaker([FromBody] UpdateMakerProfileRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateMakerProfile.Command(
            Bio: body.Bio,
            BankAccount: body.BankAccount,
            PersonalPickupEnabled: body.PersonalPickupEnabled,
            PickupNote: body.PickupNote), ct);
        return HandleResult(result);
    }
}
