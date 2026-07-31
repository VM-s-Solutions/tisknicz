using Asp.Versioning;
using Makables.Config.Auth;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.AppServices.Features.Profile;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity.Validators;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
///
/// <para>
/// <b>Image uploads</b> (avatar, maker logo) follow the ADR 0011 §"Uploads"
/// sequence established by <c>ProductController.UploadImage</c>: validate
/// size + MIME + magic bytes against the buffered header, stream to the
/// <c>profile-images</c> container under a fresh ULID filename (never the
/// caller's), then dispatch the attach command. Blob I/O sits OUTSIDE the
/// unit-of-work transaction, so failures are compensated by hand — a
/// rejected attach deletes the blob just uploaded, and a successful
/// replace deletes the superseded one.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public sealed class ProfileController(
    IMakerRepository makers,
    IBlobStorageClient blobs,
    IUserSessionProvider session,
    IIdGenerator ids,
    IHostAudience hostAudience) : MakablesApiController
{
    public sealed record UpdateProfileRequest(string FullName, string? Phone);

    /// <summary>Blob path of the stored image, for the client to build a URL from.</summary>
    public sealed record UploadProfileImageResponse(string BlobPath);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record DeleteMyAccountRequest(string ConfirmedEmail);
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

    /// <summary>
    /// Self-service GDPR account deletion (soft delete + logout-all).
    /// <c>POST .../delete</c> (not <c>DELETE /me</c>) — the operation is
    /// side-effecting and gated by a retype body, mirroring the admin
    /// <c>POST users/{id}/erase</c> naming convention. On success the
    /// session cookies are cleared so the caller is logged out immediately.
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteMyAccountRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new DeleteMyAccount.Command(body.ConfirmedEmail), ct);
        if (result.IsSuccess)
        {
            AuthCookies.ClearSessionCookies(Response, hostAudience.Value);
        }

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

    // === Avatar (any authenticated user) ===

    [HttpPost("avatar")]
    [RequestSizeLimit(ProfileImageValidator.MaxSizeBytes + 4096)]  // file + small multipart overhead
    [ProducesResponseType(typeof(UploadProfileImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        var country = session.GetUserCountryCode();
        if (string.IsNullOrEmpty(country))
        {
            return Unauthorized(Error.Unauthorized());
        }

        return await StoreProfileImageAsync(
            file,
            folder: "avatars",
            ownerId: userId,
            country: country,
            attach: async (path, token) =>
            {
                var r = await Mediator.Send(new SetUserAvatar.Command(path), token);
                return r.IsSuccess
                    ? BusinessResult.Success(r.Value!.PreviousBlobPath)
                    : BusinessResult.Failure<string?>(r.Error!);
            },
            ct);
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var result = await Mediator.Send(new SetUserAvatar.Command(null), ct);
        return await HandleClearAsync(result.IsSuccess ? result.Value!.PreviousBlobPath : null, result, ct);
    }

    // === Maker logo (maker host / users with a maker row) ===

    [HttpPost("maker/logo")]
    [RequestSizeLimit(ProfileImageValidator.MaxSizeBytes + 4096)]
    [ProducesResponseType(typeof(UploadProfileImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadMakerLogo(IFormFile file, CancellationToken ct)
    {
        var userId = session.GetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Resolve the maker for the id + country in the blob path. The
        // SetMakerLogo command re-resolves from the session, so this read
        // only shapes the path; it can never widen access.
        var maker = await makers.GetByUserIdAsync(userId, ct);
        if (maker is null)
        {
            return NotFound(Error.NotFound("maker"));
        }

        return await StoreProfileImageAsync(
            file,
            folder: "makers",
            ownerId: maker.Id,
            country: maker.CountryCode,
            attach: async (path, token) =>
            {
                var r = await Mediator.Send(new SetMakerLogo.Command(path), token);
                return r.IsSuccess
                    ? BusinessResult.Success(r.Value!.PreviousBlobPath)
                    : BusinessResult.Failure<string?>(r.Error!);
            },
            ct);
    }

    [HttpDelete("maker/logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMakerLogo(CancellationToken ct)
    {
        var result = await Mediator.Send(new SetMakerLogo.Command(null), ct);
        return await HandleClearAsync(result.IsSuccess ? result.Value!.PreviousBlobPath : null, result, ct);
    }

    // === Shared upload plumbing ===

    /// <summary>
    /// The ADR 0011 upload sequence, shared by the avatar and logo
    /// endpoints: validate → upload under a fresh ULID filename → attach
    /// → compensate. On attach failure the just-written blob is deleted
    /// so a rejected upload leaves no residue; on success the superseded
    /// blob is deleted so repeated replacement can't accumulate orphans.
    /// </summary>
    private async Task<IActionResult> StoreProfileImageAsync(
        IFormFile file,
        string folder,
        string ownerId,
        string country,
        Func<string, CancellationToken, Task<BusinessResult<string?>>> attach,
        CancellationToken ct)
    {
        // Bare IFormFile parameter — the multipart schema is rewritten to
        // { type: "string", format: "binary" } + required by
        // AddMakablesOpenApi's operation transformer. The runtime check
        // stays regardless of what the spec promises clients.
        if (file is null || file.Length == 0)
        {
            return BadRequest(Error.Validation("file", BusinessErrorMessage.FileInvalid));
        }

        await using (var probe = file.OpenReadStream())
        {
            var header = new byte[ProfileImageValidator.RequiredHeaderBytes];
            var read = await ReadAtLeastAsync(probe, header, ct);

            var validation = ProfileImageValidator.Validate(
                file.ContentType, file.Length, header.AsSpan(0, read));
            if (validation != ProfileImageValidator.Result.Valid)
            {
                var code = validation switch
                {
                    ProfileImageValidator.Result.TooLarge => BusinessErrorMessage.FileTooLarge,
                    ProfileImageValidator.Result.UnsupportedType => BusinessErrorMessage.FileUnsupportedType,
                    _ => BusinessErrorMessage.FileInvalid,  // MagicByteMismatch → generic "invalid"
                };
                return BadRequest(Error.Validation("file", code));
            }
        }

        // Fresh ULID filename — never the caller's, which is attacker-
        // controlled. Changing the filename on every upload also means a
        // replaced image can't be served from a stale CDN cache entry.
        var ext = ProfileImageValidator.ExtensionFor(file.ContentType);
        var blobPath = $"{country.ToLowerInvariant()}/{folder}/{ownerId}/{ids.Next()}.{ext}";

        // Re-open from the start — the probe above consumed the header.
        await using var uploadStream = file.OpenReadStream();
        var upload = await blobs.UploadAsync(
            BlobContainer.ProfileImages, blobPath, uploadStream, file.ContentType, ct);
        if (!upload.IsSuccess)
        {
            return HandleResult(upload);
        }

        var attached = await attach(blobPath, ct);
        if (!attached.IsSuccess)
        {
            await blobs.DeleteAsync(BlobContainer.ProfileImages, blobPath, ct);
            return HandleResult(BusinessResult.Failure<UploadProfileImageResponse>(attached.Error!));
        }

        // Replacing an existing image: drop the old blob. Best-effort —
        // the new path is already committed, so a failed cleanup is a
        // storage-cost problem, not a correctness one.
        if (!string.IsNullOrEmpty(attached.Value))
        {
            await blobs.DeleteAsync(BlobContainer.ProfileImages, attached.Value, ct);
        }

        return HandleResult(BusinessResult.Success(new UploadProfileImageResponse(blobPath)));
    }

    /// <summary>
    /// Shared tail for the two DELETE endpoints: on success, remove the
    /// blob that the command just detached (best-effort — the pointer is
    /// already gone, so a failure only leaves an unreferenced blob).
    /// </summary>
    private async Task<IActionResult> HandleClearAsync<T>(
        string? removedBlobPath,
        BusinessResult<T> result,
        CancellationToken ct)
    {
        if (result.IsSuccess && !string.IsNullOrEmpty(removedBlobPath))
        {
            await blobs.DeleteAsync(BlobContainer.ProfileImages, removedBlobPath, ct);
        }

        return result.IsSuccess ? Ok() : HandleResult(result);
    }
}
