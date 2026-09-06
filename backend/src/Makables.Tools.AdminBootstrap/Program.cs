using Makables.Tools.AdminBootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Creates the first admin on an environment that has none. See
// AdminBootstrapper for the safety posture and why this is a tool.
//
//   dotnet run --project Makables.Tools.AdminBootstrap -- \
//       --email ops@makables.cz --name "Ops" --confirm-database makables_prod
//
// The password is read from stdin so it never reaches argv. Interactive runs
// are masked; piped input works too, which is what a break-glass runbook step
// actually uses.

var email = ValueAfter(args, "--email");
var fullName = ValueAfter(args, "--name");
var confirmDatabase = ValueAfter(args, "--confirm-database");

var builder = Host.CreateApplicationBuilder(StripToolArgs(args));

// Checked here, not left to the DbContext factory, so a mis-set environment
// exits with the code the runbook documents instead of an unhandled stack
// trace. The factory keeps its own throw as a backstop.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
{
    Console.Error.WriteLine(
        "Connection string 'Postgres' is not configured. "
        + "Set ConnectionStrings__Postgres in the environment.");
    return AdminBootstrapper.ExitBadInput;
}

builder.Services.AddAdminBootstrap(builder.Configuration);

using var host = builder.Build();

var password = ReadPassword();

await using var scope = host.Services.CreateAsyncScope();
var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
return await bootstrapper.RunAsync(email, fullName, password, confirmDatabase, CancellationToken.None);

static string? ValueAfter(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Strip tool flags and their values before the host builder sees them — its
// command-line provider chokes on switches it does not recognise.
static string[] StripToolArgs(string[] args)
{
    var skipNext = false;
    var kept = new List<string>();
    foreach (var arg in args)
    {
        if (skipNext) { skipNext = false; continue; }
        if (arg is "--email" or "--name" or "--confirm-database") { skipNext = true; continue; }
        kept.Add(arg);
    }

    return [.. kept];
}

// The only Console.* in backend/src, and a deliberate exception to CLAUDE.md's
// "inject ILogger<T>" rule: this is an interactive terminal prompt with masked
// echo. A logger cannot write a partial line, cannot suppress the newline, and
// would route the prompt to whatever sink is configured rather than the
// operator's TTY. Do not "fix" this in a hygiene sweep.
static string? ReadPassword()
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine();
    }

    Console.Write("Admin password (input hidden): ");
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
            continue;
        }

        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }

    Console.WriteLine();
    return new string([.. chars]);
}
