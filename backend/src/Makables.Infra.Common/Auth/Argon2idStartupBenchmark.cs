using System.Diagnostics;
using Makables.Core.Domain.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Common.Auth;

/// <summary>
/// Runs once at host startup: hashes a throwaway password with the
/// configured Argon2id parameters and logs the parameters + measured ms.
/// Per ADR 0012 §Password policy ("~100 ms per hash on App Service B2;
/// reviewed yearly") + reviewer T-0021 MAJOR M-4 — SecOps audits need
/// evidence of the actual cost, not just the configured numbers.
///
/// Fire-and-forget on the .NET thread pool so startup latency isn't
/// affected; the log entry shows up in App Insights once the hash finishes.
/// </summary>
public sealed class Argon2idStartupBenchmark(
    IPasswordHasher hasher,
    IOptions<Argon2idOptions> options,
    ILogger<Argon2idStartupBenchmark> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var opts = options.Value;
                var sw = Stopwatch.StartNew();
                _ = hasher.Hash("__startup-benchmark-throwaway__");
                sw.Stop();

                logger.LogInformation(
                    "Argon2id benchmark: m={MemoryKib} KiB, t={Iterations}, p={Parallelism}, salt={SaltBytes} B, hash={HashBytes} B took {ElapsedMs} ms",
                    opts.MemorySizeKib,
                    opts.Iterations,
                    opts.DegreeOfParallelism,
                    opts.SaltSizeBytes,
                    opts.HashSizeBytes,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Argon2id startup benchmark failed.");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
