# Patterns catalog

> Single in-repo reference for the architectural patterns Makables uses. Agents must **not** reach outside the repo to consult external projects. If a pattern needs an update, edit this file and write a superseding ADR.

The stack is dual:

- **`/backend/`** — .NET 10, Clean Architecture, CQRS via MediatR, EF Core, Postgres, custom auth, Azure Blob/Functions. Multiple per-audience API hosts.
- **`/frontend/`** — Next.js 16 App Router, Server Components, Tailwind 4. **Pure presentation layer.** Calls the backend through an NSwag-generated TypeScript client.

The vast majority of the patterns below apply to the **backend**. The frontend has a small section of its own.

---

## A — Backend patterns (.NET)

### A.1 Layered architecture

Clean Architecture, four logical layers. Dependencies flow inward only.

```
Web.Customer / Web.Maker / Web.Admin / Web.Public / Functions
        │
        ▼
   Makables.Config                                    ← shared startup, middleware
        │
        ├──► Makables.Core.AppServices                ← CQRS handlers, validators, services
        │           │
        │           ▼
        │     Makables.Core.Domain                    ← entities, value objects, repo interfaces, BusinessResult
        │
        ├──► Makables.Infra.Database                  ← EF Core DbContext, migrations, repositories
        ├──► Makables.Infra.Clients                   ← Comgate, Packeta, ARES, Resend, Mapbox HttpClients
        ├──► Makables.Infra.Azure.Storage.Blobs       ← Blob storage wrapper
        └──► Makables.Infra.Common                    ← shared infra utilities
```

**Rules:**
- `Core.Domain` references **no** third-party packages (no EF Core, no MediatR, no FluentValidation). Entities, value objects, repo interfaces, `BusinessResult`, `AppError`, `Money`, error codes. Pure C#.
- `Core.AppServices` references `Core.Domain` plus MediatR, FluentValidation. Never references `Infra.*`.
- `Infra.*` references `Core.Domain` (to implement interfaces) and the relevant SDKs. Never referenced by `Core.*`.
- `Web.*` references `Config`, `Core.AppServices`, and indirectly `Core.Domain`. Hosts never reference `Infra.*` directly — everything wires through `Makables.Config` extension methods.

**Verification:** project references in `.csproj` files. CI fails if `Core.Domain.csproj` lists any package other than the BCL.

---

### A.2 Feature-folder layout

One file per use case under `Core.AppServices/Features/<Entity>/<UseCase>.cs`. The Cleansia pattern. The file contains the `Command`/`Query`, the `Response`, the `Validator`, and the `Handler` — all nested in a static class named after the use case.

```
Makables.Core.AppServices/
└── Features/
    ├── Orders/
    │   ├── CreateOrder.cs
    │   ├── AcceptOrder.cs
    │   ├── ShipOrder.cs
    │   ├── DeliverOrder.cs
    │   ├── GetOrderDetails.cs
    │   ├── GetPagedOrders.cs
    │   ├── DTOs/
    │   │   ├── OrderListItem.cs
    │   │   └── OrderDetail.cs
    │   └── Filters/
    │       └── OrderFilter.cs
    ├── Makers/
    ├── Products/
    ├── Payouts/
    ├── Invoices/
    └── ...
```

One use case = one file. Splitting into separate handler/validator files is not the pattern.

---

### A.3 CQRS — Command vs Query

Marker interfaces in `Core.AppServices.Abstractions`:

```csharp
// Commands mutate. Return BusinessResult (with or without TResponse).
public interface ICommand : IRequest<BusinessResult> { }
public interface ICommand<TResponse> : IRequest<BusinessResult<TResponse>> { }

// Queries read. Return BusinessResult<TResponse>.
public interface IQuery<TResponse> : IRequest<BusinessResult<TResponse>> { }

// Handlers
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, BusinessResult>
    where TCommand : ICommand { }

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, BusinessResult<TResponse>>
    where TCommand : ICommand<TResponse> { }

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, BusinessResult<TResponse>>
    where TQuery : IQuery<TResponse> { }
```

**Rules:**
- Commands go through the `UnitOfWorkPipelineBehavior` (auto-commit on success). Queries do not.
- Commands always have a `Validator`. Queries have one **only when** they have parameters needing existence checks.
- HTTP mapping: commands → POST/PUT/PATCH/DELETE; queries → GET (or POST when the filter is too complex for query strings, e.g. paged list endpoints).
- Paged list queries use plain `IRequest<PagedData<T>>` instead of `IQuery<T>` — they don't need a validator and return `PagedData<T>` directly.

---

### A.4 `BusinessResult<T>` — no exceptions for expected failures

Replace `throw` with `BusinessResult`. Exceptions are reserved for **truly unexpected** failures (programmer errors, infrastructure crashes).

```csharp
public class BusinessResult
{
    public bool IsSuccess { get; }
    public Error? Error { get; }

    protected BusinessResult(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static BusinessResult Success() => new(true, null);
    public static BusinessResult Failure(Error error) => new(false, error);
    public static BusinessResult<T> Success<T>(T value) => new(true, null, value);
    public static BusinessResult<T> Failure<T>(Error error) => new(false, error, default);
    public static BusinessResult ValidationFailure(IEnumerable<ValidationFailure> failures) =>
        new(false, Error.Validation(failures));
}

public class BusinessResult<T> : BusinessResult
{
    public T? Value { get; }

    internal BusinessResult(bool isSuccess, Error? error, T? value) : base(isSuccess, error)
    {
        Value = value;
    }
}
```

### `Error` and `ErrorType`

```csharp
public sealed record Error(string Field, string Code, ErrorType Type = ErrorType.Validation, object? Details = null)
{
    public static Error Validation(IEnumerable<ValidationFailure> failures) =>
        new(failures.First().PropertyName, "validation.failed", ErrorType.Validation, failures);

    public static Error NotFound(string entity) =>
        new(entity, $"{entity.ToLowerInvariant()}.notFound", ErrorType.NotFound);

    public static Error Conflict(string field, string code) =>
        new(field, code, ErrorType.Conflict);

    public static Error Forbidden() =>
        new(string.Empty, "auth.forbidden", ErrorType.Forbidden);

    public static Error Unauthorized() =>
        new(string.Empty, "auth.required", ErrorType.Unauthorized);

    public static Error Transient(string code, object? details = null) =>
        new(string.Empty, code, ErrorType.Transient, details);

    public static Error Permanent(string code, object? details = null) =>
        new(string.Empty, code, ErrorType.Permanent, details);

    public static Error Configuration(string code) =>
        new(string.Empty, code, ErrorType.Configuration);
}

public enum ErrorType
{
    // Request-level (map to HTTP status codes)
    Validation,    // 400
    Unauthorized,  // 401
    Forbidden,     // 403
    NotFound,      // 404
    Conflict,      // 409
    // Integration-level (drive retry decisions)
    Transient,     // 503 (retry)
    Permanent,     // 422 (do not retry)
    Configuration, // 500 (alert ops)
    Unknown        // 500 (limited retry)
}
```

### Centralized error codes

```csharp
// Makables.Core.AppServices/Common/BusinessErrorMessage.cs
public static class BusinessErrorMessage
{
    // Auth
    public const string AuthRequired             = "auth.required";
    public const string AuthForbidden            = "auth.forbidden";
    // Order
    public const string OrderNotFound            = "order.notFound";
    public const string OrderAlreadyAccepted     = "order.alreadyAccepted";
    public const string OrderInvalidTransition   = "order.invalidTransition";
    // Product
    public const string ProductNotFound          = "product.notFound";
    public const string ProductNotActive         = "product.notActive";
    // Country / config
    public const string CountryNotServiced       = "country.notServiced";
    public const string CountryConfigMissing     = "country.configMissing";
    // Payment
    public const string PaymentGatewayUnavailable    = "payment.gatewayUnavailable";
    public const string PaymentVerificationFailed    = "payment.verificationFailed";
    // Shipping
    public const string ShippingCarrierUnavailable   = "shipping.carrierUnavailable";
    // Validation
    public const string Required                 = "validation.required";
    public const string MinLength                = "validation.minLength";
    public const string MaxLength                = "validation.maxLength";
    public const string InvalidEnumValue         = "validation.invalidEnumValue";
    public const string InvalidEmailFormat       = "validation.invalidEmail";
    // ...
}
```

Every code is a dot-notation string. The frontend's i18n catalog must have a 1:1 key for every code. L10n agent enforces this parity.

---

### A.5 MediatR pipeline behaviors

Two behaviors run on every request, in order:

| Order | Behavior | Applies to | Purpose |
|---|---|---|---|
| 1 | `ValidationPipelineBehavior` | All requests | Runs all `IValidator<TRequest>` instances; on failure returns `BusinessResult.ValidationFailure(...)` without calling the handler |
| 2 | `UnitOfWorkPipelineBehavior` | Commands only | After the handler succeeds, calls `IUnitOfWork.SaveChangesAsync()`; on failure does nothing (no commit) |

```csharp
public class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : BusinessResult
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            return (TResponse)(object)BusinessResult.ValidationFailure(failures);

        return await next();
    }
}

public class UnitOfWorkPipelineBehavior<TRequest, TResponse>(IUnitOfWork uow)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand
    where TResponse : BusinessResult
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();
        if (response.IsSuccess) await uow.SaveChangesAsync(ct);
        return response;
    }
}
```

**Critical rule:** handlers **never** call `SaveChangesAsync()` themselves. The pipeline behavior does it. Calling `SaveChangesAsync()` in a handler is a review-blocker.

---

### A.6 Controller base class + `HandleResult`

Every controller inherits from `MakablesApiController`. The base provides `HandleResult` which maps `BusinessResult` to the right HTTP status code.

```csharp
[ApiController]
public abstract class MakablesApiController : ControllerBase
{
    private ISender? _sender;
    protected ISender Mediator => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected IActionResult HandleResult(BusinessResult result)
    {
        if (result.IsSuccess) return Ok();
        return result.Error!.Type switch
        {
            ErrorType.Validation     => BadRequest(result.Error),
            ErrorType.Unauthorized   => Unauthorized(result.Error),
            ErrorType.Forbidden      => Forbid(),
            ErrorType.NotFound       => NotFound(result.Error),
            ErrorType.Conflict       => Conflict(result.Error),
            ErrorType.Transient      => StatusCode(503, result.Error),
            ErrorType.Permanent      => UnprocessableEntity(result.Error),
            ErrorType.Configuration  => StatusCode(500, result.Error),
            _                        => StatusCode(500, result.Error),
        };
    }

    protected IActionResult HandleResult<T>(BusinessResult<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        return HandleResult((BusinessResult)result);
    }
}
```

Controllers stay thin:

```csharp
[Route("api/orders")]
public class OrdersController : MakablesApiController
{
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrder.Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrder.Command command, CancellationToken ct)
        => HandleResult(await Mediator.Send(command, ct));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OrderDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
        => HandleResult(await Mediator.Send(new GetOrderDetails.Query(id), ct));

    [HttpPost("get-paged")]
    [ProducesResponseType(typeof(PagedData<OrderListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaged([FromBody] GetPagedOrders.Request request, CancellationToken ct)
        => Ok(await Mediator.Send(request, ct));
}
```

---

### A.7 Feature file structure — full example

```csharp
// Makables.Core.AppServices/Features/Orders/CreateOrder.cs
using FluentValidation;
using MediatR;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Repositories;

namespace Makables.Core.AppServices.Features.Orders;

public class CreateOrder
{
    public record Command(
        string ProductId,
        int Quantity,
        ShippingMethod ShippingMethod,
        string? ZasilkovnaBranchId,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? Notes,
        IReadOnlyList<string> Attachments
    ) : ICommand<Response>;

    public record Response(string OrderId, string OrderNumber, long TotalPriceMinor, string Currency);

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IProductRepository productRepository)
        {
            RuleFor(x => x.ProductId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(BusinessErrorMessage.Required)
                .MustAsync(productRepository.ExistsAsync).WithMessage(BusinessErrorMessage.ProductNotFound);

            RuleFor(x => x.Quantity).GreaterThan(0).WithMessage(BusinessErrorMessage.Required);

            RuleFor(x => x.CustomerName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(BusinessErrorMessage.Required)
                .MinimumLength(2).WithMessage(BusinessErrorMessage.MinLength)
                .MaximumLength(100).WithMessage(BusinessErrorMessage.MaxLength);

            RuleFor(x => x.CustomerEmail)
                .NotEmpty().WithMessage(BusinessErrorMessage.Required)
                .EmailAddress().WithMessage(BusinessErrorMessage.InvalidEmailFormat);

            RuleFor(x => x.ShippingMethod).IsInEnum().WithMessage(BusinessErrorMessage.InvalidEnumValue);

            When(x => x.ShippingMethod == ShippingMethod.Zasilkovna, () =>
            {
                RuleFor(x => x.ZasilkovnaBranchId)
                    .NotEmpty().WithMessage(BusinessErrorMessage.Required);
            });
        }
    }

    public class Handler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICountryConfigurationRepository countryConfigRepository,
        IPricingService pricingService,
        IUserSessionProvider userSessionProvider,
        IOrderNumberGenerator orderNumberGenerator,
        IClock clock
    ) : ICommandHandler<Command, Response>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken ct)
        {
            // Validator already confirmed productId exists — we can dereference.
            var product = (await productRepository.GetByIdAsync(command.ProductId, ct))!;
            var config = await countryConfigRepository.GetByCodeAsync(product.CountryCode, ct)
                         ?? throw new InvalidOperationException(
                            $"CountryConfiguration missing for {product.CountryCode}");

            var pricing = pricingService.Compute(product, command.Quantity, command.ShippingMethod, config);
            var customerId = userSessionProvider.GetUserId()!;
            var orderNumber = await orderNumberGenerator.NextAsync(product.CountryCode, clock.UtcNow.Year, ct);

            var order = Order.Create(
                orderNumber,
                customerId,
                product,
                command.Quantity,
                command.CustomerName,
                command.CustomerEmail,
                command.CustomerPhone,
                command.ShippingMethod,
                command.ZasilkovnaBranchId,
                pricing,
                clock.UtcNow);

            orderRepository.Add(order);

            // UnitOfWorkPipelineBehavior commits.
            return BusinessResult.Success(new Response(
                order.Id, order.OrderNumber, pricing.TotalPriceMinor, pricing.Currency));
        }
    }
}
```

**Notes:**
- One file. Command + Response + Validator + Handler nested in a class named after the use case.
- Validator handles all existence checks and field rules. Handler is happy-path only.
- Handler uses `!` on values the validator confirmed exist.
- No `SaveChangesAsync()` call — pipeline does it.
- No `if (countryCode == "CZ")` — config table drives variation.

---

### A.8 Paged query pattern

Paged list endpoints follow a stable contract.

```csharp
// Makables.Core.AppServices/Shared/DTOs/RequestModels/DataRangeRequest.cs
public class DataRangeRequest
{
    public int Offset { get; init; } = 0;
    public int Limit  { get; init; } = 20;
    public SortDescriptor[]? Sort { get; init; }
}

public record SortDescriptor(string Field, bool Ascending);

// Makables.Core.AppServices/Shared/DTOs/ResponseModels/PagedData.cs
public record PagedData<T>(IReadOnlyList<T> Items, int TotalItems, int Offset, int Limit);
```

```csharp
// Makables.Core.AppServices/Features/Orders/GetPagedOrders.cs
public class GetPagedOrders
{
    public class Request : DataRangeRequest, IRequest<PagedData<OrderListItem>>
    {
        public OrderFilter? Filter { get; init; }
    }

    internal class Handler(IOrderRepository orderRepository) : IRequestHandler<Request, PagedData<OrderListItem>>
    {
        public async Task<PagedData<OrderListItem>> Handle(Request request, CancellationToken ct)
        {
            var spec = OrderSpecification.Create(request.Filter);
            var totalItems = await orderRepository.CountAsync(spec, ct);
            var items = await orderRepository
                .GetPagedSort<OrderSort>(request.Offset, request.Limit, spec.SatisfiedBy(), request.Sort.MapToDomain())
                .AsNoTracking()
                .Select(o => o.MapToListItem())
                .ToListAsync(ct);

            return new PagedData<OrderListItem>(items, totalItems, request.Offset, request.Limit);
        }
    }
}
```

- Paged queries use plain `IRequest<PagedData<T>>` (not `IQuery<T>`) — they don't need validators and don't wrap in `BusinessResult<T>`.
- Filter object lives in `Features/<Entity>/Filters/<Entity>Filter.cs`.
- Specification lives in `Core.Domain/Specifications/<Entity>Specification.cs` and builds the LINQ predicate.
- Sort enum lives in `Core.Domain/Sorting/<Entity>Sort.cs`.

---

### A.9 Repository pattern

Interfaces in `Core.Domain/Repositories/`. Implementations in `Infra.Database/Repositories/`. Handlers depend only on the interface.

```csharp
// Core.Domain/Repositories/IOrderRepository.cs
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string id, CancellationToken ct);
    Task<Order?> GetByNumberAsync(string orderNumber, CancellationToken ct);
    Task<bool> ExistsAsync(string id, CancellationToken ct);
    Task<int> CountAsync(ISpecification<Order> spec, CancellationToken ct);
    IQueryable<Order> GetPagedSort<TSort>(int offset, int limit, Expression<Func<Order, bool>> predicate, TSort sort)
        where TSort : BaseSort<Order>;
    void Add(Order order);
}

// Infra.Database/Repositories/OrderRepository.cs
public class OrderRepository(MakablesDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(string id, CancellationToken ct) =>
        db.Orders.AsTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    // ...
}
```

Repositories never call `SaveChangesAsync()`. The `IUnitOfWork` (implemented by `MakablesDbContext`) does it via the pipeline.

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

---

### A.10 DTOs as `record` types; centralized mappers

```csharp
// Makables.Core.AppServices/Features/Orders/DTOs/OrderListItem.cs
public record OrderListItem(
    string Id,
    string OrderNumber,
    OrderStatus Status,
    string CustomerName,
    long TotalPriceMinor,
    string Currency,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn
);
```

Mapping lives in `Mappers/<Entity>Mappers.cs` as extension methods:

```csharp
// Makables.Core.AppServices/Mappers/OrderMappers.cs
public static class OrderMappers
{
    public static OrderListItem MapToListItem(this Order order) =>
        new(order.Id, order.OrderNumber, order.Status, order.CustomerName,
            order.TotalPriceMinor, order.Currency, order.CreatedOn, order.UpdatedOn);

    public static OrderDetail MapToDetail(this Order order) => /* ... */;
}
```

**Forbidden:**
- Static `From(Entity)` factories on DTOs.
- Methods on DTOs.
- Mutating DTO properties after construction.

---

### A.11 `Auditable` base entity

Every transactional entity inherits from `Auditable`. Soft delete by default.

```csharp
// Core.Domain/Common/BaseEntity.cs
public abstract class BaseEntity : IEntity<string>
{
    public string Id { get; protected set; } = Ulid.NewUlid().ToString();
    public bool IsActive { get; protected set; } = true;
}

// Core.Domain/Common/Auditable.cs
public abstract class Auditable : BaseEntity
{
    public string CountryCode { get; protected set; } = default!;  // ISO 3166-1 alpha-2
    public string CreatedBy { get; private set; } = default!;
    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? UpdatedOn { get; private set; }
    public string? DeactivatedBy { get; private set; }
    public DateTimeOffset? DeactivatedOn { get; private set; }

    public Auditable Created(string createdBy, DateTimeOffset createdOn) { CreatedBy = createdBy; CreatedOn = createdOn; return this; }
    public Auditable Updated(string updatedBy, DateTimeOffset updatedOn) { UpdatedBy = updatedBy; UpdatedOn = updatedOn; return this; }
    public Auditable Deactivated(string by, DateTimeOffset on) { DeactivatedBy = by; DeactivatedOn = on; IsActive = false; return this; }
}
```

Audit columns are populated automatically by a `SaveChangesInterceptor` reading `IUserSessionProvider`.

---

### A.12 `CountryConfiguration` — control plane for variation

Per-country settings live in a DB table, **not in code**. Adding a country = inserting a row.

```csharp
// Core.Domain/Configuration/CountryConfiguration.cs
public class CountryConfiguration : Auditable
{
    public string CountryId { get; private set; } = default!;          // FK to countries
    public string DefaultCurrencyCode { get; private set; } = default!;
    public string DefaultLanguageCode { get; private set; } = default!;
    public string DateFormat { get; private set; } = default!;
    public string TimeZoneId { get; private set; } = default!;
    public string PhonePrefix { get; private set; } = default!;

    // Tax / VAT
    public int StandardVatRateBp { get; private set; }                  // 2100 = 21%
    public int? ReducedVatRateBp { get; private set; }
    public InvoicingMode InvoicingMode { get; private set; } = InvoicingMode.None;

    // Business identifiers
    public string TaxIdLabel { get; private set; } = default!;          // "DIČ"
    public string? TaxIdFormat { get; private set; }                    // regex
    public string VatIdLabel { get; private set; } = default!;
    public string? VatIdFormat { get; private set; }
    public bool VatIdRequired { get; private set; }
    public string RegistrationNumberLabel { get; private set; } = default!;  // "IČO"
    public string? RegistrationNumberFormat { get; private set; }
    public bool RegistrationNumberRequired { get; private set; } = true;

    // Provider defaults
    public string DefaultPaymentProvider { get; private set; } = default!;   // "comgate"
    public string DefaultShippingCarrier { get; private set; } = default!;   // "packeta"
    public string DefaultRegistry { get; private set; } = default!;          // "ares"
    public string DefaultEmailProvider { get; private set; } = default!;     // "resend"

    public string? LegalRequirementsJson { get; private set; }

    public static CountryConfiguration Create(/* ... */) => new() { /* ... */ };

    public CountryConfiguration UpdateVatRates(int standardBp, int? reducedBp) { /* ... */ return this; }
    public CountryConfiguration UpdateInvoicingMode(InvoicingMode mode) { /* ... */ return this; }
    public CountryConfiguration UpdatePaymentProvider(string code) { /* ... */ return this; }
    // ...
}
```

**Country.IsServiced vs IsActive:** two flags on `Country`. `IsActive` = visible in admin pickers; `IsServiced` = open for business. CZ launches with both `true`.

**Code never branches on country directly.** Look up the config:

```csharp
// ❌ WRONG
if (countryCode == "CZ") vatRate = 0.21m;

// ✅ RIGHT
var config = await countryConfigRepository.GetByCodeAsync(countryCode, ct);
var vatRate = config.StandardVatRateBp / 10000m;
```

---

### A.13 Enforcement-mode pattern

For things that vary per country in **non-trivial ways** (not just a number), use an enum mode column on `CountryConfiguration` and branch on the mode in the relevant service. New mode = new branch + new adapter. Existing modes never change.

```csharp
public enum InvoicingMode
{
    None,                    // No VAT, no fiscal reporting. Maker is not a VAT payer.
    StandardVat,             // Maker is VAT-registered; invoice carries VAT lines.
    ReverseCharge,           // B2B intra-EU reverse charge (future).
    StrictFiscalReporting    // EET 2.0 / DE TSE / AT RKSV — receipt held until signature.
}

public class InvoiceService(/* deps */)
{
    public async Task<BusinessResult<Invoice>> IssueAsync(Order order, CancellationToken ct)
    {
        var config = await countryConfigRepository.GetByCodeAsync(order.CountryCode, ct);
        return config.InvoicingMode switch
        {
            InvoicingMode.None                  => await IssueWithoutVatAsync(order, ct),
            InvoicingMode.StandardVat           => await IssueWithVatAsync(order, ct),
            InvoicingMode.ReverseCharge         => await IssueReverseChargeAsync(order, ct),
            InvoicingMode.StrictFiscalReporting => await IssueWithFiscalSignatureAsync(order, ct),
            _                                   => throw new ArgumentOutOfRangeException()
        };
    }
}
```

**Where this applies in Makables:**
- `InvoicingMode` for VAT and fiscal reporting variation
- `ShippingMode` if a country needs label-by-API vs print-at-home vs courier-call workflows
- `RegistryMode` if a country's company registry needs OAuth (Germany) vs anonymous (CZ ARES)

---

### A.14 Error classification → retry policy

Every external-call error is classified. The classification drives the retry policy.

| `ErrorType` | When | Retry? | Schedule |
|---|---|---|---|
| `Transient` | network blip, 5xx, timeout, rate limit | yes | exponential: 1m, 2m, 5m, 15m, 1h, 6h, then daily up to 10 attempts |
| `Permanent` | bad input, business rule violation (e.g., VAT ID mismatch) | no | escalate to admin |
| `Configuration` | missing/expired credentials, wrong endpoint | no | escalate to SecOps |
| `Unknown` | unclassified | limited (3) | then escalate |

```csharp
public static class RetrySchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6),
        TimeSpan.FromDays(1), TimeSpan.FromDays(1), TimeSpan.FromDays(1), TimeSpan.FromDays(1)
    ];

    public static DateTimeOffset? ComputeNextRetryAt(int attemptCount, DateTimeOffset now) =>
        attemptCount >= Delays.Length ? null : now + Delays[attemptCount];
}
```

Tables that need retry tracking get columns: `RetryCount INT`, `LastRetryAt TIMESTAMPTZ`, `NextRetryAt TIMESTAMPTZ`, `LastErrorType TEXT`, `LastErrorCode TEXT`, `AcknowledgedBy TEXT`.

Background functions (Azure Functions, timer-triggered) sweep tables for rows where `NextRetryAt <= now()` and retry.

---

### A.15 Provider adapter pattern (keyed services)

Every external service is reached through an interface in `Core.Domain` (or `Core.AppServices.Abstractions`), implemented in `Infra.Clients`. Selection is by `CountryConfiguration` lookup, **not** by `if (country == "CZ")`.

```csharp
public interface IPaymentProvider
{
    string Code { get; }
    Task<BusinessResult<PaymentSession>> CreatePaymentAsync(Order order, CancellationToken ct);
    Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(string providerRef, CancellationToken ct);
    Task<BusinessResult<WebhookPayload>> VerifyWebhookAsync(HttpRequest request, CancellationToken ct);
}
```

Implementations registered with **keyed services** by provider code:

```csharp
services.AddKeyedScoped<IPaymentProvider, ComgatePaymentProvider>("comgate");
// services.AddKeyedScoped<IPaymentProvider, StripePaymentProvider>("stripe");  // when added
```

Handlers depend on a factory, not a concrete adapter:

```csharp
public interface IPaymentProviderFactory
{
    Task<IPaymentProvider> ResolveAsync(string countryCode, CancellationToken ct);
}

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

Same shape for `IShippingCarrier`, `ICompanyRegistry`, `IEmailProvider`, `IAddressGeocoder`.

---

### A.16 Per-audience API hosts

Four API hosts on different routes (Cleansia parity):

| Project | Audience | Auth policy | Notes |
|---|---|---|---|
| `Makables.Web.Customer` | Authenticated customers (role `customer`) | JWT, audience `customer` | Public catalog reads via Public host |
| `Makables.Web.Maker` | Authenticated makers (role `maker`) | JWT, audience `maker` | Maker dashboard, products, orders, payouts |
| `Makables.Web.Admin` | Admins (role `admin`) | JWT, audience `admin`, stricter rate limit, IP allowlist in production | All admin operations |
| `Makables.Web.Public` | Unauthenticated | None for catalog reads; signed webhooks; cron secret | Webhooks (Comgate, Packeta), ARES proxy, cron endpoints, public catalog |

All four share `Makables.Core.*`, `Makables.Config`, `Makables.Infra.*`. Each host's `Program.cs` is essentially the same — Cleansia pattern — calling `AddMakablesXxx()` extension methods.

---

### A.17 Authentication (custom)

We own auth end-to-end. No third-party IdP.

**Components:**
- `User` entity in `Core.Domain.Users` with `Email`, `PasswordHash` (Argon2id), `EmailConfirmedAt`, `Role` (`customer | maker | admin`), `CountryCode`, audit columns
- `RefreshToken` entity with `UserId`, `TokenHash`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenId`
- `IAuthService` interface in `Core.Domain.Authentication`
- `AuthService` impl in `Infra.Common.Authentication` — Argon2 hashing, JWT issuance (HS256 with key rotation), refresh-token rotation
- `IPasswordHasher` interface; `Argon2PasswordHasher` impl with parameters tuned for ~100ms per hash
- `IEmailVerificationService` — sends magic links and confirmation emails
- JWT validation middleware in `Makables.Config/AddMakablesAuth.cs`; uses `ClaimsPrincipal` with claims: `sub`, `email`, `role`, `country_code`, `aud` (one of `customer | maker | admin`)
- Access token TTL: 15 minutes. Refresh token TTL: 30 days, rotated on every use, family-revoked on reuse detection.
- Refresh tokens stored only as hashes (SHA-256 of the random opaque token).
- Refresh tokens delivered as HttpOnly, Secure, SameSite=Strict cookies on `.makables.cz`.

**Audience enforcement:** the JWT `aud` claim must match the host's expected audience. A customer JWT cannot be replayed against the maker API even if the user has both roles — the user re-authenticates via the audience-specific login.

**Out of scope for MVP:** OAuth (Google), MFA, SSO. Wrapped behind `IAuthService` so adding them later doesn't ripple through the codebase.

---

### A.18 Money — `long` minor units, currency-aware

```csharp
// Core.Domain/Money/Money.cs
public readonly record struct Money(long AmountMinor, string Currency)
{
    public static Money Of(long amountMinor, string currency) => new(amountMinor, currency);
    public static Money CZK(long minor) => new(minor, "CZK");
    public static Money Zero(string currency) => new(0L, currency);

    public Money Add(Money other) { AssertSameCurrency(other); return new(AmountMinor + other.AmountMinor, Currency); }
    public Money Subtract(Money other) { AssertSameCurrency(other); return new(AmountMinor - other.AmountMinor, Currency); }
    public Money PercentOfBp(int basisPoints) => new((long)Math.Round((double)AmountMinor * basisPoints / 10_000d, MidpointRounding.AwayFromZero), Currency);

    private void AssertSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Currency mismatch: {Currency} vs {other.Currency}");
    }
}
```

**Storage:**
- Every monetary column: `BIGINT NOT NULL` ending in `_minor`.
- Every monetary row: `currency CHAR(3) NOT NULL` column (or inherited from parent).
- VAT rates: `INTEGER` basis points (`2100` = 21%).
- Rounding: half-up (`MidpointRounding.AwayFromZero`).
- CZ display: format strips haléře — `579 Kč`. Other locales render minor units per culture default.

---

### A.19 EF Core global query filters — multi-country + soft delete

EF Core's global query filters give us automatic scoping by `CountryCode` and `IsActive`. Every entity that inherits `Auditable` gets two filters configured in `MakablesDbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
        if (typeof(Auditable).IsAssignableFrom(entityType.ClrType))
        {
            // Soft-delete filter
            var param = Expression.Parameter(entityType.ClrType, "e");
            var isActiveProp = Expression.Property(param, nameof(Auditable.IsActive));
            var filter = Expression.Lambda(isActiveProp, param);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
```

Country scoping is **not** enforced at the DB layer (no RLS). It is enforced at the application layer:
1. JWT carries `country_code` claim.
2. Repository methods accept a `CountryCode` parameter where appropriate, or specifications include it.
3. Cross-country admin queries are an explicit opt-out: `IQueryable<T>` `IgnoreQueryFilters()` or a special admin repository method.

This is acceptable because:
- The .NET app is the only writer to the database. No Supabase-style direct client → DB path exists.
- Defense in depth comes from JWT audience + role checks at the controller + repository scoping.
- RLS in Postgres is still available as a hardening step in a later ADR if we want belt-and-braces.

---

### A.20 Idempotent webhooks + Unit of Work

Webhook handlers (Comgate, Packeta) must be safely retryable.

**Pattern:**
1. Verify origin/signature first. On failure → 401 (the caller retries).
2. Look up the resource by `provider_ref` (e.g. `comgate_transaction_id`).
3. If the resource is already in the target state, return 200 with no side effects.
4. Otherwise transition state in a single transaction.
5. Side effects (email send, invoice PDF generation, label queueing) are enqueued via a domain event or a deferred-action queue. They fire **after** the transaction commits. Failures retry independently via the retry-policy pattern.

EF Core's `SaveChanges` is the commit boundary. The Unit of Work pipeline behavior is the single place that calls it. Deferred side effects are queued during the handler and flushed after commit by an interceptor.

---

### A.21 NSwag client generation

The Web hosts emit OpenAPI specs at `/openapi/v1.json`. NSwag generates a TypeScript client into `frontend/src/lib/api-client/`.

**Pipeline:**
1. Backend developer changes a controller signature, DTO, or enum.
2. CI builds the backend, fetches the OpenAPI spec, runs NSwag.
3. If the generated client differs from what's committed, CI fails with a clear "regenerate the API client" message.
4. Developer runs `npm run generate:api` in `/frontend/`, commits the regenerated client.

**Rules:**
- The generated files carry a banner: `// AUTO-GENERATED by NSwag. Do not edit.`
- A pre-commit hook blocks manual edits to files in `frontend/src/lib/api-client/`.
- One generator config per audience: `customer-api.ts`, `maker-api.ts`, `admin-api.ts`, `public-api.ts`.
- Generated DTOs are immutable interfaces with `readonly` fields where the language allows.

---

## B — Frontend patterns (Next.js)

### B.1 The frontend is a pure presentation layer

- **No server-side database access.** Zero references to any DB client (`pg`, `prisma`, `@supabase/*`).
- **No business logic.** Pricing, validation rules, state machines all live in the backend. The frontend only formats, displays, and submits.
- **Server Components by default.** They fetch via the API client; data hydrates on render.
- **`'use client'` only for interactivity.** Forms with local state, modals, file pickers, the 3D hero scene.
- **No `useEffect` for data fetching.** Server Components do it, or a Client Component calls the API client in an event handler.

### B.2 Folder layout

```
frontend/src/
├── app/
│   ├── (public)/                       # /, /katalog, /produkt/[id], /jak-to-funguje, /pro-makery, /vop, /gdpr
│   ├── (auth)/                         # /auth/login, /auth/register, /auth/reset, /auth/verify, /auth/magic
│   ├── (customer)/                     # /dashboard/zakaznik/*, /objednavka/*
│   ├── (maker)/                        # /dashboard/maker/*
│   ├── (admin)/                        # /dashboard/admin/*
│   ├── layout.tsx
│   ├── error.tsx
│   ├── not-found.tsx
│   └── globals.css
├── components/
│   ├── ui/                             # Button, Input, Card, Badge, Modal, Spinner, Alert, Select, Textarea
│   ├── layout/                         # Header, Footer, Sidebar
│   ├── forms/                          # OrderForm, ProductForm, MakerRegistrationForm
│   ├── catalog/                        # MakerCard, ProductCard, CategoryFilter, CitySearch
│   ├── dashboard/                      # OrderTable, OrderActions, OrderMessages, ProductActions
│   └── shared/                         # Rating, FileUpload, ZasilkovnaWidget, HeroScene
└── lib/
    ├── api-client/                     # NSwag-generated. DO NOT EDIT.
    │   ├── customer-api.ts
    │   ├── maker-api.ts
    │   ├── admin-api.ts
    │   └── public-api.ts
    ├── auth/
    │   ├── session.ts                  # reads JWT from cookie / memory, attaches Authorization header
    │   ├── refresh.ts                  # refresh-token rotation
    │   └── guards.ts                   # role helpers for client components
    ├── runtime/
    │   ├── api-fetch.ts                # fetch wrapper: attaches auth, parses errors, returns Result<T>
    │   ├── result.ts                   # client-side Result<T, ApiError> type (mirrors backend BusinessResult)
    │   └── errors.ts                   # ApiError type matching backend Error
    ├── i18n/
    │   └── cs-CZ/                      # translation catalog, key per BusinessErrorMessage code
    └── utils/
        ├── dates.ts                    # cs-CZ formatting (matches backend)
        ├── money.ts                    # formatMoney (strip haléře for CZK display)
        └── validation.ts               # client-side mirrors of common validators (for UX only; server validates authoritatively)
```

### B.3 Auth on the client

- Access token (JWT) lives in **memory** on the client (a module-level variable inside `lib/auth/session.ts`). Not in localStorage. Not in a cookie readable from JS.
- Refresh token lives in an **HttpOnly cookie** set by the backend on login. The client never reads it directly; it only sends it via the refresh endpoint.
- On page load, the client calls `/auth/refresh` to obtain a fresh access token. If refresh fails (expired or revoked), the user is bounced to `/auth/login`.
- Server Components that need authenticated data read the refresh cookie via `cookies()` (Next.js) and exchange for a short-lived access token server-side to call the API. This is a thin server-side helper, not application logic.

### B.4 Calling the API

Every API call goes through `lib/runtime/api-fetch.ts`, which:
- Attaches `Authorization: Bearer <accessToken>`.
- Catches 401, attempts refresh, retries once.
- Maps backend `Error` to a typed `ApiError` and returns `Result<T, ApiError>`.

```ts
// Example client-side call
const result = await customerApi.orders.create({ productId, quantity, ... });
if (!result.ok) {
  toast.error(t(result.error.code));   // i18n key lookup
  return;
}
router.push(`/objednavka/${result.value.orderId}`);
```

### B.5 Czech-only UI

All user-facing strings come from `lib/i18n/cs-CZ/`. No hardcoded Czech strings in components except where they are visibly tied to the brand (the hero copy "Where Ideas Take Shape." may be hardcoded; navigation, buttons, error messages come from i18n keys).

Every `BusinessErrorMessage` code on the backend has a key in `cs-CZ/`. L10n agent enforces parity in PRs.

### B.6 No DB SDK imports

ESLint rule blocks imports of `pg`, `prisma`, `@supabase/*`, `mongodb`, or any DB SDK in `/frontend/src/`. The only data path is `lib/api-client/`.

---

## How to read this catalog

- **Architect** drafts ADRs that **accept**, **adapt**, or **reject** each pattern with rationale.
- **dotnet-backend** implements per the accepted ADRs and section A.
- **frontend** implements per the accepted ADRs and section B.
- **Reviewer** checks PRs against the catalog and the relevant ADRs.
- If a pattern needs to change, write a new ADR that supersedes the old one, then update this file.

**Never** import or read from any project folder outside this repository. This file plus the ADRs are the complete reference.
