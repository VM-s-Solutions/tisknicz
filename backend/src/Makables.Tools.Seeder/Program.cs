using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Infra.Common.Auth;
using Makables.Infra.Database;
using Makables.Infra.Database.Interceptors;
using Makables.Tools.Seeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Tool flags — stripped before the rest of the args reach the host
// builder (its command-line configuration provider would choke on
// value-less switches).
var reset = args.Contains("--reset");
var allowRemote = args.Contains("--allow-remote");
var migrate = args.Contains("--migrate");
var hostArgs = args.Where(a => a is not ("--reset" or "--allow-remote" or "--migrate")).ToArray();

var builder = Host.CreateApplicationBuilder(hostArgs);

builder.Services.AddOptions<Argon2idOptions>()
    .Bind(builder.Configuration.GetSection(Argon2idOptions.SectionName));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<SeedClock>();
builder.Services.AddSingleton<IClock>(sp => sp.GetRequiredService<SeedClock>());
builder.Services.AddSingleton<IUserSessionProvider, SeedUserSessionProvider>();
builder.Services.AddScoped<AuditableSaveChangesInterceptor>();

builder.Services.AddDbContext<MakablesDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Connection string 'Postgres' is not configured. " +
            "Set ConnectionStrings:Postgres in appsettings or environment.");
    }

    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<AuditableSaveChangesInterceptor>());
});

builder.Services.AddScoped<DevDataSeeder>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var seeder = scope.ServiceProvider.GetRequiredService<DevDataSeeder>();
return await seeder.RunAsync(reset, allowRemote, migrate, CancellationToken.None);
