using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Makables.Tools.AdminBootstrap;

/// <summary>
/// Creates the FIRST admin user on an environment that has none.
///
/// <para>
/// <b>Why a tool and not a feature.</b> <c>Register</c> refuses
/// <see cref="UserRole.Admin"/> outright, and every admin-management use case
/// requires an existing admin — so a fresh database has no reachable path to
/// its own console. The options were a seed migration (bakes an identity, and
/// any hash, permanently into schema history), a startup bootstrap from
/// configuration (puts a credential path in app settings and re-runs on every
/// boot forever), or a one-shot operator-run tool. This is the tool, and it
/// deliberately mirrors <c>Makables.Tools.Seeder</c>'s shape so operators meet
/// one pattern rather than two.
/// </para>
///
/// <para>
/// <b>This tool is not a security boundary.</b> Its only prerequisite is write
/// access to the production connection string, and anyone holding that could
/// insert an admin row by hand — the Argon2id hash format is self-describing.
/// The guards below are operator-safety guards: they stop a correct operator
/// making a wrong move. The actual control is database network and credential
/// isolation.
/// </para>
///
/// <para>
/// <b>Safety posture.</b> Unlike the seeder — which hard-refuses any target
/// whose host or database name contains "prod" — this tool MUST be able to
/// target production, because that is its entire purpose. The controls are
/// therefore different:
/// <list type="bullet">
/// <item>The password is read from stdin, never from argv, so it cannot land in
/// shell history, a CI log, or another user's <c>ps</c> output.</item>
/// <item>The operator must name the target database with
/// <c>--confirm-database</c>, and it must match the one the connection string
/// actually resolves to. A host-is-localhost check was NOT enough: reaching a
/// private Postgres means an SSH tunnel, so <c>localhost:5432</c> is the normal
/// way to touch production — such a check would wave through the one target
/// that most needs confirming, while printing a reassuring "localhost".</item>
/// <item>It refuses when an active admin already exists, so it cannot be used to
/// quietly mint a second one. This is a bootstrap, not a back door. Note the
/// check is read-then-write with no constraint behind it — unlike the email
/// there is no "at most one admin" index — so two operators racing with
/// different addresses would both succeed. Accepted: both accounts are
/// operator-created and each writes its own audit row, so there is no
/// attacker-controlled outcome to win.</item>
/// <item>It records an <see cref="AdminAuditLogEntry"/> for its own action, so
/// the very first privileged account is not an unexplained row.</item>
/// </list>
/// </para>
///
/// <para>
/// The "already exists" check counts only ACTIVE admins — the global
/// soft-delete filter applies. That is deliberate: the only in-product path that
/// deactivates a <see cref="User"/> is self-service account deletion, so the
/// realistic way to reach this state is a solo admin locking themselves out.
/// Ignoring the filter would turn a recoverable lockout into a permanent one for
/// no attacker-facing gain.
/// </para>
/// </summary>
internal sealed class AdminBootstrapper(
    MakablesDbContext db,
    IPasswordHasher passwordHasher,
    IIdGenerator idGenerator,
    IClock clock,
    ILogger<AdminBootstrapper> logger)
{
    /// <summary>Exit code: an admin was created.</summary>
    internal const int ExitSuccess = 0;

    /// <summary>Exit code: refused for a safety reason. Nothing was written.</summary>
    internal const int ExitRefused = 1;

    /// <summary>Exit code: the inputs were unusable.</summary>
    internal const int ExitBadInput = 2;

    internal const int MinPasswordLength = 12;

    internal async Task<int> RunAsync(
        string? email,
        string? fullName,
        string? password,
        string? confirmDatabase,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            logger.LogError("A valid --email is required.");
            return ExitBadInput;
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            logger.LogError("--name is required (it appears in the admin audit log).");
            return ExitBadInput;
        }

        // Longer than the app's own 10-char registration floor: this account can
        // refund money, erase users and change country configuration, and it is
        // typed once by an operator rather than remembered by a customer.
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            logger.LogError(
                "Password must be at least {MinLength} characters. It is read from stdin, never from the command line.",
                MinPasswordLength);
            return ExitBadInput;
        }

        if (!TargetIsConfirmed(confirmDatabase))
        {
            return ExitRefused;
        }

        // Same precondition the seeder enforces. Without it a bootstrap run
        // before `dotnet ef database update` on a fresh environment — the exact
        // environment this tool targets — dies on `relation "users" does not
        // exist` instead of saying what is wrong.
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            logger.LogError(
                "Refusing: {Count} migration(s) are pending, starting with {First}. Run the migration job first.",
                pending.Count, pending[0]);
            return ExitRefused;
        }

        if (await db.Set<User>().AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
        {
            logger.LogError(
                "Refusing: this database already has an active admin. This tool bootstraps the FIRST one only — "
                + "create further admins through the admin console.");
            return ExitRefused;
        }

        // EmailNormalized, not Email: the unique index is on the normalised
        // column and User.Create preserves the operator's casing, so comparing
        // the display column would let `Ops@x.cz` slip past a check for
        // `ops@x.cz` and surface as a raw 23505 instead of a clean refusal.
        //
        // IgnoreQueryFilters for the same reason UserRepository.EmailExistsAsync
        // does (ADR 0013): a soft-deleted account still owns its address.
        // Without it the insert would succeed — the index is partial on
        // is_active — and permanently block that user's reactivation.
        var normalisedEmail = User.NormalizeEmail(email);
        var emailTaken = await db.Set<User>()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EmailNormalized == normalisedEmail, cancellationToken);
        if (emailTaken)
        {
            logger.LogError(
                "Refusing: that address is already registered. Promoting an existing account is not "
                + "this tool's job — pick a dedicated admin address.");
            return ExitRefused;
        }

        var now = clock.UtcNow;
        var admin = User.Create(
            id: idGenerator.Next(),
            email: email.Trim(),
            role: UserRole.Admin,
            fullName: fullName.Trim(),
            countryCodePrimary: "CZ",
            passwordHash: passwordHasher.Hash(password),
            // Confirmed on creation: the email-confirmation flow runs through
            // the outbox, and on a fresh environment nothing has drained it yet.
            // An admin who cannot log in cannot start the platform.
            emailAlreadyConfirmed: true,
            confirmedAt: now);

        db.Set<User>().Add(admin);

        // Self-referential by necessity — there is no prior admin to attribute
        // this to. Recording it means the first privileged account is explained
        // rather than simply present. admin_user_id has no FK, and the
        // append-only trigger fires only on UPDATE/DELETE, so the insert is fine.
        db.Set<AdminAuditLogEntry>().Add(AdminAuditLogEntry.Record(
            id: idGenerator.Next(),
            adminUserId: admin.Id,
            actionCode: "admin.bootstrap",
            // Lowercase to match every other producer ("maker", "category",
            // "payout_batch", ...). AdminQueries filters TargetEntity with a
            // case-sensitive Postgres comparison, so "User" would hide this row
            // from the very filter an auditor uses — defeating the point of
            // writing it.
            targetEntity: "user",
            targetId: admin.Id,
            beforeJson: null,
            afterJson: $$"""{"role":"Admin","emailNormalized":"{{normalisedEmail}}"}""",
            now: now,
            notes: "First admin created by Makables.Tools.AdminBootstrap."));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent insert, or hit an edge the
            // pre-check could not see. Fail as a refusal, never as an unhandled
            // throw — the runbook's exit-code table has to hold.
            logger.LogError(ex,
                "Refusing: the database rejected the insert as a duplicate. No admin was created.");
            return ExitRefused;
        }

        logger.LogInformation(
            "Created admin {UserId}. Sign in at the admin host and change this password.",
            admin.Id);
        return ExitSuccess;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Requires the operator to name the database they intend to write to, and
    /// refuses unless the connection string resolves to exactly that name. See
    /// the safety-posture note on the class for why a localhost check is not the
    /// control here.
    /// </summary>
    private bool TargetIsConfirmed(string? confirmDatabase)
    {
        string host, database;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            host = builder.Host ?? string.Empty;
            database = builder.Database ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refusing: the connection string could not be parsed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            logger.LogError("Refusing: the connection string names no database.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(confirmDatabase))
        {
            logger.LogError(
                "Refusing: pass --confirm-database <name> naming the database you intend to write to. "
                + "This connection resolves to {Host}/{Database}.",
                host, database);
            return false;
        }

        if (!string.Equals(confirmDatabase.Trim(), database, StringComparison.Ordinal))
        {
            logger.LogError(
                "Refusing: --confirm-database said {Expected} but the connection resolves to {Host}/{Actual}.",
                confirmDatabase.Trim(), host, database);
            return false;
        }

        logger.LogWarning("Bootstrapping the first admin on {Host}/{Database}.", host, database);
        return true;
    }
}
