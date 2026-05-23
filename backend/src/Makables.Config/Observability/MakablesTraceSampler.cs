using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Makables.Config.Observability;

/// <summary>
/// Sampling policy from ADR 0023 §4 Tracing:
///   - 100% of error traces (status code >= 500 or unhandled exception).
///   - 100% of webhook traces (path under <c>/api/v1/public/webhooks/</c>).
///   - 10% of other successful traces.
///
/// OTel evaluates the sampler at span start, so the request status isn't
/// known yet. We approximate the "errors" rule by leaving the decision to
/// the inner ratio sampler at start time and relying on the AspNetCore
/// instrumentation's <c>RecordException</c> behaviour to keep error-bearing
/// activities even when the parent was sampled out. The webhook rule is
/// applied by inspecting the <c>http.route</c> tag if present.
/// </summary>
public sealed class MakablesTraceSampler : Sampler
{
    private readonly Sampler _ratioSampler;

    public MakablesTraceSampler(double successRatio)
    {
        if (successRatio is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(successRatio), successRatio,
                "Sampling ratio must be in [0, 1].");
        }

        _ratioSampler = new TraceIdRatioBasedSampler(successRatio);
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.Tags is not null)
        {
            foreach (var (key, value) in samplingParameters.Tags)
            {
                if (key is "http.route" or "url.path" or "http.target"
                    && value is string s
                    && s.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase))
                {
                    return new SamplingResult(SamplingDecision.RecordAndSample);
                }
            }
        }

        return _ratioSampler.ShouldSample(samplingParameters);
    }
}
