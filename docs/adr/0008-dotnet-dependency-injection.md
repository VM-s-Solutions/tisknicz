---
id: 0008
title: .NET dependency injection via Microsoft.Extensions.DependencyInjection
status: accepted
date: 2026-05-21
deciders: [Architect, user]
supersedes: [0006]
---

# 0008 — .NET dependency injection via Microsoft.Extensions.DependencyInjection

## Context

ADR 0006 chose `tsyringe` for per-request DI on a Next.js + Supabase stack. That stack was abandoned by ADR 0007. The backend is now a .NET 10 solution. The frontend has no business logic to wire — pages call the NSwag-generated API client directly. The DI question only matters on the backend.

## Decision

Use the **built-in `Microsoft.Extensions.DependencyInjection`** container that ships with ASP.NET Core. No third-party DI library. Wiring lives in extension methods inside `Makables.Config`, called by every API host's `Program.cs`. This is the Cleansia pattern.

### Lifetime conventions

| Lifetime | What it's for |
|---|---|
| **Scoped** | Default for everything per request: `DbContext`, repositories, MediatR handlers, validators, services that depend on `IUserSessionProvider`. One instance per HTTP request, disposed at request end. |
| **Singleton** | Configuration objects, `IClock`, `ILogger<T>`, HTTP client factories, NodaTime-style providers, the MediatR `ISender` infrastructure. Anything stateless and thread-safe. |
| **Transient** | Reserved for lightweight, allocation-cheap objects that should not retain any state between calls. Rare. |

### Wiring layout

```
Makables.Config/
├── Extensions/
│   ├── AddMakablesInfrastructure.cs   # registers DbContext, repositories, blob/queue clients
│   ├── AddMakablesAuth.cs             # JWT validation, IAuthService, password hasher
│   ├── AddMakablesCors.cs             # per-host CORS policy
│   ├── AddMakablesMediator.cs         # MediatR + pipeline behaviors + FluentValidation validators
│   ├── AddMakablesClients.cs          # Comgate, Packeta, ARES, Resend, Mapbox typed HttpClients
│   └── AddMakablesRateLimiting.cs     # per-host rate limit policies
├── Middleware/
│   ├── RequestLoggingMiddleware.cs
│   ├── ErrorHandlingMiddleware.cs
│   └── UseMakablesMiddleware.cs       # combined middleware pipeline registration
└── ServiceCollectionExtensions.cs     # AddMakablesAll() — convenience aggregator
```

### Each API host's `Program.cs`

Every host follows the same shape — only its audience-specific CORS and auth policy differ.

```csharp
// Makables.Web.Customer/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();              // .NET Aspire defaults: health, OpenTelemetry, service discovery
builder.Services.AddMakablesInfrastructure(builder.Configuration);
builder.Services.AddMakablesAuth(builder.Configuration, audience: "customer");
builder.Services.AddMakablesCors("customer");
builder.Services.AddMakablesMediator();
builder.Services.AddMakablesClients(builder.Configuration);
builder.Services.AddMakablesRateLimiting("customer");
builder.Services.AddControllers();
builder.Services.AddOpenApi();             // NSwag/Swashbuckle producing the OpenAPI spec

var app = builder.Build();

app.UseMakablesMiddleware();               // request logging, error handling
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();                          // /openapi/v1.json for NSwag client generation

app.Run();
```

### Constructor injection only

Handlers, validators, services, and controllers receive their dependencies via constructor parameters. No service locator. No `IServiceProvider` injection except in framework integration points (e.g. a custom `IMediator` adapter).

```csharp
public class Handler(
    IOrderRepository orderRepository,
    IProductRepository productRepository,
    ICountryConfigurationRepository countryConfigRepository,
    IPricingService pricingService,
    IUserSessionProvider userSessionProvider,
    ILogger<Handler> logger
) : ICommandHandler<Command, Response>
{
    public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken ct)
    {
        // happy-path business logic; dependencies via parameters
    }
}
```

### Token / interface conventions

Every infrastructure capability has an interface in `Core.Domain` or `Core.AppServices.Abstractions`. The implementation lives in `Infra.*`. Wiring registers the implementation against the interface:

```csharp
// AddMakablesInfrastructure.cs
services.AddDbContext<MakablesDbContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("Postgres")));

services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MakablesDbContext>());

services.AddScoped<IOrderRepository, OrderRepository>();
services.AddScoped<IProductRepository, ProductRepository>();
services.AddScoped<IMakerRepository, MakerRepository>();
services.AddScoped<ICountryConfigurationRepository, CountryConfigurationRepository>();
services.AddScoped<IUserRepository, UserRepository>();

services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<IBlobStorageClient, AzureBlobStorageClient>();
```

### Provider selection (multi-country, multi-provider)

Adapters that vary per country (`IPaymentProvider`, `IShippingCarrier`, `ICompanyRegistry`, `IEmailProvider`) are not registered against a single interface. They are registered by **provider code** and resolved at runtime by looking up `CountryConfiguration`:

```csharp
// AddMakablesClients.cs
services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>("comgate");
services.AddKeyedScoped<IShippingCarrier, PacketaShippingCarrier>("packeta");
services.AddKeyedScoped<ICompanyRegistry, AresCompanyRegistry>("ares");
services.AddKeyedScoped<IEmailProvider, ResendEmailProvider>("resend");

// A small factory resolves the right one per country:
services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
```

```csharp
public class PaymentProviderFactory(
    IServiceProvider sp,
    ICountryConfigurationRepository countryConfigRepository
) : IPaymentProviderFactory
{
    public async Task<IPaymentProvider> ResolveAsync(string countryCode, CancellationToken ct)
    {
        var config = await countryConfigRepository.GetByCodeAsync(countryCode, ct);
        return sp.GetRequiredKeyedService<IPaymentProvider>(config.DefaultPaymentProvider);
    }
}
```

This means a handler doesn't depend on `ComgatePaymentProvider` — it depends on `IPaymentProviderFactory`. Adding Stripe-CZ later is one keyed registration + one `CountryConfiguration` update; zero handler changes.

### Testing

Tests construct handlers with hand-built mocks. No DI container in unit tests:

```csharp
[Fact]
public async Task CreateOrder_ProductNotFound_ReturnsNotFound()
{
    var orderRepo = Substitute.For<IOrderRepository>();
    var productRepo = Substitute.For<IProductRepository>();
    productRepo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Product?)null);

    var handler = new CreateOrder.Handler(orderRepo, productRepo, /* ... */);
    var result = await handler.Handle(new CreateOrder.Command(/* ... */), default);

    result.IsSuccess.Should().BeFalse();
    result.Error.Code.Should().Be("product.notFound");
}
```

Integration tests use `WebApplicationFactory<Program>` with a test database. The real container resolves real services; only external HTTP clients (Comgate, Packeta) are substituted via `WebApplicationFactory.WithWebHostBuilder(...)`.

## Alternatives considered

- **`tsyringe` on the backend** — N/A. The backend isn't TypeScript anymore.
- **Autofac** — rejected. Powerful (assembly scanning, decorators, modules) but the built-in container has covered every Cleansia need. Adding Autofac trades familiarity for marginal capability.
- **Scrutor for assembly scanning** — viable add-on, deferred. We can add it later if hand-registration of N repositories becomes tedious; not needed at MVP scale.
- **Service locator (inject `IServiceProvider` everywhere)** — rejected. Hides dependencies, defeats unit-test isolation, anti-pattern.

## Consequences

### Positive

- **Zero external dependency** for DI. One less thing to upgrade or audit.
- **Matches Cleansia.** Every wiring pattern the user already knows transfers verbatim.
- **Keyed services solve provider-per-country cleanly** without a custom registry abstraction.
- **`AddServiceDefaults()` (Aspire)** gives us OpenTelemetry, health checks, service discovery for free — same as Cleansia.

### Negative

- **Hand-registration is verbose** at scale. Mitigated by grouping registrations into `AddMakablesInfrastructure` etc., so each host's `Program.cs` is a flat list of `AddMakablesXxx()` calls.
- **No compile-time check that every interface has a registration.** A typo in the interface name only fails at request time. Mitigated by integration tests that resolve every controller via `WebApplicationFactory` — broken registrations fail fast in CI.

## Compliance / verification

- Reviewer checklist: dependencies are injected via constructor parameters only; no `IServiceProvider` injection in handlers.
- Reviewer checklist: every new interface has a corresponding `services.AddScoped` (or `AddSingleton`) registration in the appropriate `AddMakablesXxx.cs` extension method.
- Reviewer checklist: per-country adapters use `AddKeyedScoped` and are resolved via a factory; handlers never reference concrete adapter classes.
- Test convention: integration tests start the host via `WebApplicationFactory<Program>` and resolve every controller through the real container; broken DI fails the test.

## Related

- Supersedes: ADR 0006
- Patterns: `docs/architecture/patterns.md` (to be updated for .NET as part of the pivot)
- ADR 0007 — Stack pivot
