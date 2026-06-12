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

**Frontend consumption.** Both validation shapes (multi-field via `details: ValidationDetail[]` AND single-field via top-level `field`+`code` with `details: null`) are collapsed into `ApiError.fields: Record<string, readonly string[]>` of display copy by `apiFetch`'s `parseErrorResponse`. The forms render the entry directly under the matching input. Both `application/json` and `application/problem+json` content types are parsed — framework-level errors (ASP.NET model-binding 400s, framework 404) still resolve to typed `ApiError` instead of falling through to a text branch. See B.17.

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

**`[ProducesResponseType]` is mandatory on every action**, not optional. NSwag emits `Promise<void>` for any `IActionResult`-returning action without a typed response attribute, so a missing annotation silently strips the return type from the generated TypeScript client. Land the attribute alongside the action:

- **`200` (`typeof(TResponse)`)** for the happy path with the handler's response DTO.
- **`401` (`typeof(Error)`)** when the action is `[Authorize]`.
- **`404` (`typeof(Error)`)** when the handler can return `NotFound` via `HandleResult`.
- **`409` (`typeof(Error)`)** when the handler can return `Conflict`.
- **No `400 → Error`** on `[FromBody]` or multipart actions: under `[ApiController]` the framework rejects malformed input with `ValidationProblemDetails` (RFC 7807) **before** `HandleResult` runs — declaring a single `Error` shape there would lie about half the 400 surface. The handler's own FluentValidation 400 is `Error`-shaped, but declaring it would mislead generated clients about the framework path. Same lesson as `CatalogController.GetMakers` (T-0046b) and the Maker `ProductController` mutations (T-0049b).

---

### A.7 Feature file structure — full example (T-0063 CreateOrder)

Single static class containing nested `Command` / `Response` / `Validator` / `Handler`. The shape below is the **shipped** T-0063 implementation — earlier drafts of this section assumed an async `MustAsync` existence check in the Validator and a clock-year argument on `IOrderNumberGenerator.NextAsync`; both were dropped (T-0062 made the generator TZ-aware, T-0063 moved existence checks to the handler so the Validator stays sync + stateless).

#### Command shape

```csharp
public sealed record Command(
    string ProductId,
    int Quantity,                       // == 1 at MVP per T-0061 Q4
    ShippingMethod ShippingMethod,
    string? ZasilkovnaPickupPointId,    // required iff ZasilkovnaPickupPoint
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string? CustomerNotes
) : ICommand<Response>;

public sealed record Response(
    string OrderId,
    string OrderNumber,         // M-{CC}-{YYYY}{NNNN}
    long TotalPriceMinor,
    string Currency);
```

No attachments in the Command (T-0064 owns the multipart upload at `POST /api/v1/orders/{id}/attachments`). No payment URL (T-0065 is a follow-up call from the order page).

#### Validator shape — sync, stateless

```csharp
public sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(c => c.ProductId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
            .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);

        RuleFor(c => c.Quantity)
            .Equal(1).WithErrorCode(BusinessErrorMessage.OrderInvalidQuantity);

        RuleFor(c => c.ShippingMethod)
            .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

        When(c => c.ShippingMethod == ShippingMethod.ZasilkovnaPickupPoint, () =>
        {
            RuleFor(c => c.ZasilkovnaPickupPointId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
        });

        RuleFor(c => c.CustomerName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
            .MinimumLength(2).WithErrorCode(BusinessErrorMessage.MinLength)
            .MaximumLength(100).WithErrorCode(BusinessErrorMessage.MaxLength);

        RuleFor(c => c.CustomerEmail)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
            .EmailAddress().WithErrorCode(BusinessErrorMessage.InvalidEmailFormat)
            .MaximumLength(254).WithErrorCode(BusinessErrorMessage.MaxLength);

        RuleFor(c => c.CustomerPhone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
            .Matches(CzechPhoneRegex.Pattern())
                .WithErrorCode(BusinessErrorMessage.InvalidPhoneFormat);

        When(c => c.CustomerNotes is not null, () =>
        {
            RuleFor(c => c.CustomerNotes!)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        });
    }
}

internal static partial class CzechPhoneRegex
{
    [GeneratedRegex(@"^(\+420\s?)?[6-9]\d{2}\s?\d{3}\s?\d{3}$")]
    public static partial Regex Pattern();
}
```

Note `WithErrorCode` (not `WithMessage`) — `Error.Code` is what the frontend i18n catalogue keys off; the human message is rendered on the FE from the catalogue. No DB-backed `MustAsync` existence checks: the handler owns `ProductNotFound` / `MakerDeactivated` etc. because they overlap with TOCTOU-window concerns the validator can't see.

#### Handler shape — 8-step happy-path, no SaveChanges

```csharp
public sealed class Handler(
    IUserSessionProvider session,
    IProductRepository products,
    IMakerRepository makers,
    IPricingService pricing,
    IOrderNumberGenerator orderNumbers,
    IOrderRepository orders,
    IIdGenerator ids,
    ILogger<Handler> logger
) : IRequestHandler<Command, BusinessResult<Response>>
{
    public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken ct)
    {
        // 1. Resolve customer identity (backstop — [Authorize] should already have 401'd).
        var customerUserId = session.GetUserId();
        if (string.IsNullOrEmpty(customerUserId))
            return BusinessResult.Failure<Response>(Error.Unauthorized());

        // 2. Load product (TOCTOU pre-check).
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
            return BusinessResult.Failure<Response>(
                Error.NotFound("productId", BusinessErrorMessage.ProductNotFound));
        if (!product.IsActive)
            return BusinessResult.Failure<Response>(
                Error.Conflict("productId", BusinessErrorMessage.ProductNotActive));

        // 3. Defence-in-depth on maker state (user decision Q4 — every customer-facing
        //    money-bearing flow defends even when the frontend gates).
        var maker = await makers.GetByIdAsync(product.MakerId, ct);
        if (maker is null || !maker.IsActive)
            return BusinessResult.Failure<Response>(
                Error.Conflict("makerId", BusinessErrorMessage.MakerDeactivated));
        if (!maker.IsVerified)
            return BusinessResult.Failure<Response>(
                Error.Conflict("makerId", BusinessErrorMessage.MakerNotVerified));
        if (command.ShippingMethod == ShippingMethod.PersonalPickup && !maker.PersonalPickupEnabled)
            return BusinessResult.Failure<Response>(
                Error.Conflict("shippingMethod", BusinessErrorMessage.MakerPersonalPickupDisabled));

        // 4. Pricing — surface every IPricingService failure verbatim.
        var pricingResult = await pricing.ComputeForProductAsync(
            command.ProductId, command.ShippingMethod, ct);
        if (!pricingResult.IsSuccess)
            return BusinessResult.Failure<Response>(pricingResult.Error!);
        var breakdown = pricingResult.Value!;

        // 5. Reserve order number — T-0062 TZ-aware contract: no `int year` argument.
        var orderNumber = await orderNumbers.NextAsync(product.CountryCode, ct);

        // 6. Build aggregate (Order.Create re-trims defensively; we trim locally
        //    so the snapshot is canonical in logs and downstream serialisation).
        var order = Order.Create(
            id: ids.Next(),
            orderNumber: orderNumber,
            customerUserId: customerUserId,
            makerId: maker.Id,
            productId: product.Id,
            contactName: command.CustomerName.Trim(),
            contactEmail: command.CustomerEmail.Trim(),
            contactPhone: command.CustomerPhone.Trim(),
            productPriceAmountMinor: breakdown.ProductPrice.AmountMinor,
            shippingPriceAmountMinor: breakdown.ShippingPrice.AmountMinor,
            platformFeeAmountMinor: breakdown.PlatformFee.AmountMinor,
            makerPayoutAmountMinor: breakdown.MakerPayout.AmountMinor,
            totalAmountMinor: breakdown.TotalPrice.AmountMinor,
            currency: breakdown.TotalPrice.Currency,
            vatRateBp: breakdown.VatRateBp,
            shippingMethod: command.ShippingMethod,
            zasilkovnaPickupPointId: command.ZasilkovnaPickupPointId?.Trim(),
            countryCode: product.CountryCode,
            customerNotes: command.CustomerNotes?.Trim());

        // 7. Persist — UnitOfWorkPipelineBehavior commits.
        await orders.AddAsync(order, ct);

        logger.LogInformation(
            "Order {OrderId} ({OrderNumber}) created in PendingPayment for customer {CustomerId}.",
            order.Id, order.OrderNumber, customerUserId);

        // 8. Return the four fields the frontend needs.
        return BusinessResult.Success(new Response(
            order.Id, order.OrderNumber, order.TotalAmountMinor, order.Currency));
    }
}
```

#### Controller shape — JSON, audience-bound, wrapper for OpenAPI

```csharp
// Makables.Web.Customer/Controllers/OrdersController.cs
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController : MakablesApiController
{
    public sealed record CreateOrderRequest(
        string ProductId, int Quantity, ShippingMethod ShippingMethod,
        string? ZasilkovnaPickupPointId, string CustomerName, string CustomerEmail,
        string CustomerPhone, string? CustomerNotes);

    // Controller-level wrapper dodges the OpenAPI schema-name collision —
    // every Features/*/Xxx.Response would emit as "Response" and NSwag picks
    // whichever wins (same pattern as Maker.ProductController T-0049b).
    public sealed record CreateOrderResponse(
        string OrderId, string OrderNumber, long TotalPriceMinor, string Currency);

    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateOrder.Command(
            body.ProductId, body.Quantity, body.ShippingMethod,
            body.ZasilkovnaPickupPointId, body.CustomerName, body.CustomerEmail,
            body.CustomerPhone, body.CustomerNotes), ct);

        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreateOrderResponse(
                result.Value!.OrderId, result.Value.OrderNumber,
                result.Value.TotalPriceMinor, result.Value.Currency)))
            : HandleResult(BusinessResult.Failure<CreateOrderResponse>(result.Error!));
    }
}
```

The customer host wires `RequireEmailConfirmedMiddleware` in `Program.cs` AFTER `UseAuthentication`/`UseAuthorization` — every authenticated customer endpoint inherits the 403 gate without per-action plumbing (skip-list: `/api/v*/auth/*`).

**Notes:**
- One file, single static class. `Command` + `Response` + `Validator` + `Handler` nested.
- Validator is **sync and stateless** — no DB lookups, no async checks. Existence checks (`ProductNotFound`, `MakerDeactivated`, …) belong in the handler where the TOCTOU window is unavoidable anyway.
- Handler runs the 8 steps in order; every expected failure returns `BusinessResult.Failure`, never throws.
- No `SaveChangesAsync()` — `UnitOfWorkPipelineBehavior` commits.
- No `if (countryCode == "CZ")` — `IPricingService` reads `CountryConfiguration`; the order-number generator is TZ-aware.
- `customerUserId` only ever comes from `IUserSessionProvider` (IDOR shield).
- Defence-in-depth on maker state: handler returns typed `MakerDeactivated` / `MakerNotVerified` / `MakerPersonalPickupDisabled` failures even though the frontend gates.

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
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);
```

Mapping lives in `Mappers/<Entity>Mappers.cs` as extension methods:

```csharp
// Makables.Core.AppServices/Mappers/OrderMappers.cs
public static class OrderMappers
{
    public static OrderListItem MapToListItem(this Order order) =>
        new(order.Id, order.OrderNumber, order.Status, order.CustomerName,
            order.TotalPriceMinor, order.Currency, order.CreatedAt, order.UpdatedAt);

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
    public DateTimeOffset CreatedAt { get; protected internal set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; protected internal set; }
    public DateTimeOffset? UpdatedAt { get; protected internal set; }
    public string? DeactivatedBy { get; protected internal set; }
    public DateTimeOffset? DeactivatedAt { get; protected internal set; }

    public Auditable MarkCreated(string createdBy, DateTimeOffset createdAt) { CreatedBy = createdBy; CreatedAt = createdAt; return this; }
    public Auditable MarkUpdated(string updatedBy, DateTimeOffset updatedAt) { UpdatedBy = updatedBy; UpdatedAt = updatedAt; return this; }
    public Auditable MarkDeactivated(string by, DateTimeOffset at) { DeactivatedBy = by; DeactivatedAt = at; IsActive = false; return this; }
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

**Frontend display.** `formatCzk(amountMinor, currency)` in `lib/money/formatter.ts` is the display-side mirror — it asserts `currency === 'CZK'` and uses `Intl.NumberFormat('cs-CZ')` for the NBSP-separated whole-CZK output. Callers that may see non-CZK guard at the card boundary and route around the formatter. See B.10.

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
- A pre-commit hook (`scripts/check-api-client-manual-edits.mjs`) blocks manual edits to files in `frontend/src/lib/api-client/`. The hook allows a regen-without-spec-change case: if a generator script edit (`scripts/generate-api.mjs`) is staged in the same commit, the hashes-file requirement is relaxed (a post-process change to the TS without a spec change is legitimate; T-0049c's `FileParameter.fileName?` normaliser is the precedent).
- One generator config per audience: `customer-api.ts`, `maker-api.ts`, `admin-api.ts`, `public-api.ts`.
- Generated DTOs are immutable interfaces with `readonly` fields where the language allows.

**Schema transformers.** OpenAPI's emitter doesn't see the runtime serialiser config; honest wire shapes need transformers registered in `Makables.Config.Extensions.MakablesOpenApiExtensions.AddMakablesOpenApi`, which every host imports via `Program.cs`. Two are live today:

- **Enum schema transformer** (T-0049b) — rewrites every C# enum schema from the emitter's default `{ "type": "integer" }` to `{ "type": "string", "enum": [<names>] }` matching `JsonStringEnumConverter`'s runtime emission. Without it the generated client would type write-DTO enums as `number` while read DTOs come back as `string`. The bug is silent because `JsonStringEnumConverter` accepts both forms.
- **Multipart operation transformer** (T-0049c) — detects `IFormFile` parameters on `multipart/form-data` actions (via `ApiDescription.ParameterDescriptions` CLR-type checks) and rewrites the request body schema in place to the canonical `{ type: "object", properties: { <name>: { type: "string", format: "binary" } }, required: [<name>] }` shape. Without it `[ApiController]`'s `IFormFile` parameter ends up inlined as the full `IFormFile` interface (`contentDisposition`, `headers`, `length`, ...) in the schema, which makes NSwag emit a synthetic `Body` class with all those fields exposed.

Future drift goes here, not as ad-hoc post-process in `generate-api.mjs` — the generator script's regex pipeline is a last resort (kept narrow + ticket-tagged; see `FileParameter` appendix + the `| undefined` strip).

**Frontend consumption convention.** Route code never imports from `lib/api-client/`. Every endpoint gets a hand-written `Result<T, ApiError>`-returning wrapper in `lib/api-client-helpers/` that calls `apiFetch`. See B.16.

---

### A.22 State-machine detour with restore (Disputed + PreDisputeState)

When an exceptional condition must suspend an entity's normal lifecycle without losing its place, the entity enters a **detour state** and records the state it left in a dedicated restore column:

```csharp
public OrderState State { get; private set; }            // = Disputed while detoured
public OrderState? PreDisputeState { get; private set; } // state to restore on resolve
```

**Rules:**
1. The restore column is written only by the detour-open command and cleared only by the resolve command. Nothing else touches it.
2. Invariant: restore column non-null ⇔ `State` is the detour state. Enforce in the entity, assert in tests.
3. While detoured, the normal-transition allow-list rejects lifecycle commands; the blocked-transition error names the sanctioned command (T-0107 discipline, §A.4 error codes).
4. Resolve restores `State = PreDisputeState` by default. Outcome-driven side effects (e.g. a refund) are dispatched as **sanctioned commands** from outcome handlers — the resolve handler orchestrates, it never inlines the side-effect logic. A sanctioned command may then move the entity to a terminal state (e.g. full refund → `Refunded`) through its own allow-listed transition.
5. Idempotency is **asymmetric by design** (T-0106 §C.4): detour **open** is Silent-Success — re-opening an already-detoured entity returns success with the existing detour record's id and no side effects (T-0067/T-0076 precedent). Detour **resolve** is loud — re-resolving a non-detoured (or already-resolved) entity returns a `409` Conflict (first use: `order.dispute.notOpen`). Re-open is idempotent-safe; a silently "succeeding" re-resolve would mask an operator race and risks double money-movement through the outcome's sanctioned command.

First use: `Order.State = Disputed` + `Order.PreDisputeState` (refund-dispute bundle, Q2 lock 2026-06-12). See [extension-points.md §13](./extension-points.md#13-dispute-resolution) for the Dispute entity and its outcome-handler seam.

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

### B.3 Auth on the client — audience-scoped cookies

- Both access and refresh tokens live in **HttpOnly + Secure + SameSite=Strict** cookies set by the backend on login: `makables_access_<audience>` and `makables_refresh_<audience>` where `<audience>` ∈ `customer | maker | admin`. The client never reads either directly (HttpOnly forbids it); the browser attaches them automatically on cross-origin requests via `apiFetch`'s default `credentials: 'include'`.
- Cookie names live in `lib/auth/session.ts` (`ACCESS_COOKIE_PREFIX` / `REFRESH_COOKIE_PREFIX`). A given user can be signed into multiple audiences simultaneously (each cookie name is distinct), and an admin cookie cannot satisfy a maker request (audience enforced per host by the .NET pipeline; see A.16).
- **Server Components** read the matching cookie via `next/headers`'s `cookies()` and forward it as a `Cookie` header — see B.14 (SSR cookie forwarding, ADR 0024). The browser path is unchanged.
- **Auth refresh is not yet wired into `apiFetch`.** A 401 surfaces as `ApiError.type === 'Unauthorized'`; the call site decides how to handle it (the auth pages bounce to `/auth/login`). Automatic refresh-and-retry is on the post-launch roadmap (see `lib/runtime/api-fetch.ts:32` comment); when it lands it goes into the wrapper, not into call sites.

### B.4 Calling the API

Every API call goes through `lib/runtime/api-fetch.ts`:

- **One entry point** — `apiFetch<T>(host, path, options)` returns `Promise<Result<T, ApiError>>`. The generated NSwag clients live alongside in `lib/api-client/*-api.v1.ts` but are never called directly from route code; every endpoint gets a hand-written wrapper at `lib/api-client-helpers/*.ts` that calls `apiFetch` (see B.16).
- **Audience-aware base URL** — `host` ∈ `customer | maker | admin | public` picks the right `.NET` host base URL from env (one host per audience, ADR 0005). Public is the only anonymous host.
- **Cookie auth** — `credentials: 'include'` by default so the browser attaches the audience cookies; in Server Components the same audience cookie is forwarded server-side via `next/headers` (see B.14, ADR 0024).
- **JSON body shorthand** — pass `options.json` and the wrapper serialises + sets `Content-Type: application/json`. Pass `options.body` (raw `BodyInit`) for everything else — including `FormData` for multipart uploads, where the browser writes the boundary itself (see B.15).
- **Error mapping** — both `application/json` (Makables-native `Error`) and `application/problem+json` (RFC 7807 ProblemDetails) responses are parsed; per-field validation details are flattened into `ApiError.fields` so forms can render inline errors (see B.17).

```ts
// route file imports only the helper, never the generated client
import { createProduct } from '@/lib/api-client-helpers/maker-products';

const result = await createProduct(input);
if (!result.success) {
  // result.error: { code, message, type, fields?, correlationId? }
  applyError(result.error.type, result.error.fields);
  return;
}
router.push(`/dashboard/maker/produkty/${result.value.id}`);
```

### B.5 Czech-only UI

All user-facing strings come from `lib/i18n/cs-CZ.ts`. No hardcoded Czech strings in components except where they are visibly tied to the brand (the hero copy "Where Ideas Take Shape." may be hardcoded; navigation, buttons, error messages come from i18n keys).

Every `BusinessErrorMessage` code on the backend has a parallel i18n key in `cs-CZ.ts` (e.g. `order.notFound` on the backend → `'order.notFound'` in the catalog). The L10n agent enforces parity in PRs; route code resolves a backend `ApiError.code` to display copy by calling `t(code)` rather than reading `error.message` raw.

Czech plural-genitive is a real morphology trap — see B.18 for the plural-neutral phrasing convention that survives until `t()` gains `Intl.PluralRules('cs')`.

### B.6 No DB SDK imports

ESLint rule blocks imports of `pg`, `prisma`, `@supabase/*`, `mongodb`, or any DB SDK in `/frontend/src/`. The only data path is `lib/api-client/`.

---

The remaining B sections capture conventions that emerged during Phase 3 (Sprints 5–6). Each one closed an actual incident — the "Why" line names the ticket so future readers can dig.

### B.7 Route-level wrapper is `<section>`, not `<main>`

The root layout already wraps `{children}` in a single `<main role="main">` landmark. Every route file (`page.tsx`, `loading.tsx`, `not-found.tsx`, `error.tsx`) uses `<section>` as its outermost wrapper — nesting a second `<main>` is invalid HTML and an a11y violation (two landmarks of role `main`).

**Why:** caught by T-0047's second Copilot review; the maker-profile page shipped with three nested `<main>` elements before the fix. T-0048 and T-0049 followed the same rule.

```tsx
// frontend/src/app/layout.tsx — owns the single <main>
<body className="...">
  <main className="flex-1">{children}</main>
</body>

// frontend/src/app/(public)/katalog/[slug]/page.tsx — route file uses <section>
return (
  <section className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-10 sm:px-6 lg:px-8">
    <ProfileHeader profile={profile} ratingDisplay={ratingDisplay} />
    ...
  </section>
);
```

**References:** T-0047 (established), T-0048 + T-0049 (followed).

### B.8 URL-state pagination via `searchParams` + `<Link>`

Server Components read `searchParams.page` (and optional `searchParams.pageSize`), clamp via a small `parsePositiveInt` helper, and pass the resolved values to a `Pagination` component that renders `<Link>` (not buttons) so the back-button + share-links work. The link builder only emits `pageSize` when it diverges from the default — canonical URLs stay clean (`?page=2`, not `?page=2&pageSize=24`).

**Why:** every Phase-3 list page needs this (catalog list, maker profile products grid, maker dashboard). T-0046 established the convention; T-0049 extended it with the optional `pageSize` URL state.

```tsx
// frontend/src/app/(maker)/dashboard/maker/produkty/page.tsx
function parsePositiveInt(raw: string, fallback: number, max: number = Number.MAX_SAFE_INTEGER): number {
  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  return Math.min(parsed, max);
}

export default async function MakerProductsPage({ searchParams }: PageProps) {
  const sp = await searchParams;
  const page = parsePositiveInt(readString(sp.page), 1);
  const pageSize = parsePositiveInt(
    readString(sp.pageSize),
    MAKER_PRODUCTS_DEFAULT_PAGE_SIZE,
    MAKER_PRODUCTS_MAX_PAGE_SIZE,
  );
  // ...
}

// frontend/src/app/(maker)/dashboard/maker/produkty/pagination.tsx
const hrefFor = (target: number): string => {
  const sp = new URLSearchParams();
  sp.set('page', String(target));
  // Only emit pageSize when it diverges from the default so canonical
  // URLs stay clean (`?page=2` not `?page=2&pageSize=24`).
  if (pageSize !== defaultPageSize) {
    sp.set('pageSize', String(pageSize));
  }
  return `/dashboard/maker/produkty?${sp.toString()}`;
};
```

**References:** T-0046 (established), T-0049 (pageSize extension).

### B.9 `generateMetadata` branches title only on `NotFound`

In a route's `generateMetadata`, branch the page title only when `error.type === 'NotFound'`. Transient / auth / configuration errors fall back to the bare brand title so a backend blip doesn't tell a search-engine indexer the entity is gone. Pair with `notFound()` from `next/navigation` in the page component and a sibling `not-found.tsx` rendering friendly Czech.

**Why:** caught by T-0047's first Copilot review — the maker profile page would have shown "Výrobce nenalezen" as the document title on any transient backend error, signalling to Googlebot that the maker had been removed.

```tsx
// frontend/src/app/(public)/katalog/[slug]/page.tsx
export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { slug } = await params;
  const result = await getMakerBySlug(slug);
  if (!result.success) {
    const title =
      result.error.type === 'NotFound'
        ? `${t('catalog.maker.not_found.title')} — ${t('catalog.maker.metadata.title_suffix')}`
        : t('catalog.maker.metadata.title_suffix');
    return { title, description: t('catalog.maker.metadata.fallback_description') };
  }
  // happy path: title = company name, description = truncated bio
}

// Page component — call notFound() only on NotFound; render error UI for the rest.
if (!result.success) {
  if (result.error.type === 'NotFound') notFound();
  return <section>{/* friendly Czech error alert */}</section>;
}
```

**References:** T-0047 (established), T-0048 (followed).

### B.10 `formatCzk(amountMinor, currency)` + non-CZK fallback at the card boundary

`formatCzk(amountMinor: number, currency: string)` in `lib/money/formatter.ts` is the display formatter for Czech crowns. It **asserts** `currency === 'CZK'` (throws via `assertCzkCurrency`) — the assertion is a developer-time loud-failure, not a user-facing one. Callers that may receive non-CZK (display-only surfaces) guard the currency themselves and route around the formatter, falling back to the i18n `on_request` copy.

**Why:** centralising the assertion keeps the formatter rigid (dev/CI catches drift); pushing the non-CZK guard to the card boundary keeps the user-facing render forgiving. Caught by T-0047 Copilot review when a single non-CZK product would have 500'd the whole maker-profile route.

```ts
// frontend/src/lib/money/formatter.ts
export function formatCzk(amountMinor: number, currency: string): string {
  assertCzkCurrency(currency);
  const whole = Math.trunc(amountMinor / 100);
  const formatted = new Intl.NumberFormat('cs-CZ', { style: 'decimal', maximumFractionDigits: 0 }).format(whole);
  return `${formatted} Kč`;
}

// Card boundary — guard non-CZK BEFORE calling formatCzk.
function ProductPrice({ item }: ProductCardProps) {
  if (item.priceType === 'OnRequest' || item.priceCurrency !== 'CZK') {
    return <>{t('catalog.product.price.on_request')}</>;
  }
  const formatted = formatCzk(item.priceAmountMinor, item.priceCurrency);
  // ...
}
```

**References:** T-0047 (formatter + boundary pattern); ADR 0003 (money model on the backend).

### B.11 `formatWeight(grams)` — locale-aware weight display

`formatWeight(grams: number)` in `lib/format/weight.ts` renders product weight per the Czech display convention: `<1000g` → `"650 g"` (integer + literal unit); `≥1000g` → `"1,5 kg"` (one decimal, Czech comma via `Intl.NumberFormat('cs-CZ')`). Promoted out of T-0048's inline implementation when T-0049 became the second consumer.

**Why:** locale-aware decimal separator only happens correctly when it flows through `Intl.NumberFormat` — hand-formatting `${grams/1000}` gives a dot, not a comma. Centralising the helper means a future locale change is a one-line edit.

```ts
// frontend/src/lib/format/weight.ts
export function formatWeight(grams: number): string {
  if (grams < 1000) return `${grams} g`;
  const kg = new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(grams / 1000);
  return `${kg} kg`;
}
```

**References:** T-0048 (introduced inline), T-0049 (promoted to `lib/format/`).

### B.12 Shared display-scale constants — the `RATING_BP_PER_STAR` pattern

When the backend ships a denormalised integer (basis points, minor units, etc.) that the frontend scales for display, the conversion constant lives ONCE in the helper module and every consumer divides by the same name. The pattern's anchor is `RATING_BP_PER_STAR = 10_000` in `lib/api-client-helpers/catalog.ts` (mirroring `CatalogQueries.BpPerStar` on the backend).

**Why:** Sprint 6 surfaced a real bug — three frontend call sites divided `ratingAverageBp` by `1000` instead of `10000`, so every rated maker rendered with 5 stars (the `Math.min(5, …)` clamp hid it). Caught by T-0047's third Copilot review; the shared constant closed all three at once and prevents recurrence in T-0048's catalog rating displays.

```ts
// frontend/src/lib/api-client-helpers/catalog.ts
/**
 * Basis-points-per-star scale used by the backend's denormalized
 * Maker.RatingAverageBp field. Mirrors CatalogQueries.BpPerStar
 * (Infra/Catalog) — one star = 10 000 basis points...
 */
export const RATING_BP_PER_STAR = 10_000;

// Consumer 1: frontend/src/app/(public)/katalog/maker-card.tsx
const ratingValue = hasRating ? item.ratingAverageBp / RATING_BP_PER_STAR : 0;

// Consumer 2: frontend/src/app/(public)/katalog/[slug]/page.tsx
const ratingDisplay = (profile.ratingAverageBp / RATING_BP_PER_STAR).toFixed(1);
// ...
<Stars value={profile.ratingAverageBp / RATING_BP_PER_STAR} />
```

**References:** T-0047 Copilot round 3 (the 10× bug + this fix).

### B.13 `truncateForMeta` — shared SEO-description trimmer

`truncateForMeta(text, max = 160)` in `lib/seo/truncate-for-meta.ts` prepares free-form text for `<meta name="description">`: collapse whitespace, trim, and when the input exceeds `max`, cut back to the last word boundary inside the limit. The word-boundary cutoff scales with `max` (`Math.floor(max / 2)`) so a caller passing `max=70` (OG snippet) still gets the same "roughly half" safety margin — not a hard-coded 80.

**Why:** T-0047 and T-0048 both inlined the same helper; T-0048's third Copilot review extracted it to `lib/seo/`. T-0048's fourth round caught the hard-coded threshold and replaced it with the scaling formula.

```ts
// frontend/src/lib/seo/truncate-for-meta.ts
export function truncateForMeta(text: string, max = 160): string {
  const collapsed = text.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  const slice = collapsed.slice(0, max);
  const lastSpace = slice.lastIndexOf(' ');
  const minSpaceIndex = Math.floor(max / 2);
  return (lastSpace > minSpaceIndex ? slice.slice(0, lastSpace) : slice).trimEnd() + '…';
}
```

**References:** T-0048 (promotion + threshold-scales-with-max fix).

### B.14 SSR auth cookie forwarding (ADR 0024)

`apiFetch` is the single chokepoint for backend HTTP. When it detects the Node runtime (`typeof window === 'undefined'`), it reads the audience-scoped cookie pair (`makables_access_<host>` + `makables_refresh_<host>`) from `next/headers`'s `cookies()` and forwards them as a `Cookie` request header. Browser path is unchanged — the browser's cookie jar already handles it. Public host stays anonymous. A caller-supplied `Cookie` header wins.

**Why:** Server Components run on the Node runtime; `credentials: 'include'` is meaningless server-side. Without this, every server render of `/dashboard/maker/*` would hit the Maker host unauthenticated and bounce to the error UI. T-0049 was the first authenticated Server Component on the platform; ADR 0024 captures the convention so every future authenticated SSR page works automatically.

```ts
// frontend/src/lib/runtime/api-fetch.ts
const callerSetCookie = Object.keys(headers).some((h) => h.toLowerCase() === 'cookie');
if (host !== 'public' && typeof window === 'undefined' && !callerSetCookie) {
  const cookieHeader = await readAudienceCookieHeader(host);
  if (cookieHeader) {
    headers['Cookie'] = cookieHeader;
  }
}

async function readAudienceCookieHeader(host: ApiHost): Promise<string | null> {
  try {
    const { cookies } = await import('next/headers'); // dynamic — keeps the module portable
    const store = await cookies();
    const accessName = `${ACCESS_COOKIE_PREFIX}${host}`;
    const refreshName = `${REFRESH_COOKIE_PREFIX}${host}`;
    const parts: string[] = [];
    const access = store.get(accessName);
    if (access) parts.push(`${accessName}=${access.value}`);
    const refresh = store.get(refreshName);
    if (refresh) parts.push(`${refreshName}=${refresh.value}`);
    return parts.length > 0 ? parts.join('; ') : null;
  } catch {
    // Outside a request scope — let the request go unauthenticated; the backend 401 folds to a typed error.
    return null;
  }
}
```

Audience isolation: only the cookie matching the host's audience is forwarded. The Maker host never sees the Customer session cookie even if the user is signed into both. Public host stays anonymous unconditionally.

**References:** T-0049 (first authenticated SSR page), ADR 0024.

### B.15 Multipart uploads through `apiFetch`

Pass `body: formData` to `apiFetch` and **do not** set `Content-Type` — the browser writes the multipart boundary itself. `apiFetch` only injects `application/json` when `options.json` is provided, so raw bodies (including `FormData`) flow through clean.

**Why:** T-0049's image manager is the first multipart consumer; T-0064 (order attachments) is next. The helper layer absorbs the multipart contract so call sites stay one-liners.

```ts
// frontend/src/lib/runtime/api-fetch.ts — body branching
let body = options.body;
if (options.json !== undefined) {
  body = JSON.stringify(options.json);
  headers['Content-Type'] ??= 'application/json';
}
// FormData passes through untouched; browser sets Content-Type with boundary.

// frontend/src/lib/api-client-helpers/maker-products.ts — call site
export async function uploadProductImage(productId: string, file: File): Promise<Result<UploadProductImageResponse, ApiError>> {
  const formData = new FormData();
  formData.append('file', file);
  return apiFetch<UploadProductImageResponse>(
    'maker',
    `${Base}/${encodeURIComponent(productId)}/images`,
    { method: 'POST', body: formData },
  );
}
```

**References:** T-0049 (first multipart consumer); T-0049c (backend operation transformer that gives the multipart parameter a canonical `{ file: binary }` + `required: true` schema so NSwag types it `FileParameter` not `FileParameter | undefined`).

### B.16 Hand-written `Result<T, ApiError>` helpers wrap every endpoint

NSwag generates typed clients in `lib/api-client/`, but the generated client throws on every non-2xx — incompatible with the `Result<T, ApiError>` flow used everywhere else. Every endpoint gets a thin hand-written wrapper in `lib/api-client-helpers/` that calls `apiFetch` directly and returns `Result`. Route code imports only from the helpers, never from `lib/api-client/`.

**Why:** the `Result` shape lets call sites pattern-match on `error.type` for control flow (`if (!result.success && result.error.type === 'NotFound') notFound()`) without try/catch in JSX. The generated client is still useful — the helpers re-export its DTO interfaces as `type` aliases so route code never imports from the generated module either. A pre-commit hook (`scripts/check-api-client-manual-edits.mjs`) blocks manual edits to `lib/api-client/`.

```ts
// frontend/src/lib/api-client-helpers/maker-products.ts (excerpt)
import { apiFetch } from '../runtime/api-fetch';
import type { ApiError, Result } from '../runtime/result';
// Re-export DTO types from the generated client so route code never imports
// from lib/api-client/ directly.
import type {
  IMakerProductListItem,
  ICreateProductRequest,
  // ...
} from '../api-client/maker-api.v1';

export type MakerProductListItem = Readonly<Omit<IMakerProductListItem, 'createdOn'>> & { readonly createdOn: string };

export async function getMyProducts(input: { page?: number; pageSize?: number }): Promise<Result<MakerProductsPage, ApiError>> {
  const params = new URLSearchParams();
  if (input.page !== undefined) params.set('page', String(input.page));
  if (input.pageSize !== undefined) params.set('pageSize', String(input.pageSize));
  return apiFetch<MakerProductsPage>('maker', `${Base}?${params.toString()}`, { method: 'GET' });
}
```

Sibling helpers follow the identical pattern: `auth.ts`, `profile.ts`, `catalog.ts`, `maker-products.ts`. New endpoints land here; the generated client stays as the contract substrate.

**References:** ADR 0022 (NSwag pipeline); T-0035 (auth helpers); T-0046b + T-0049b ([ProducesResponseType] discipline that gives the generated DTOs honest shapes).

### B.17 `parseErrorResponse` — validation flattening + `application/problem+json`

`apiFetch`'s error parser handles two flavours of validation error on the wire AND two error content-types:

- **Multi-field validation** — `details: ValidationDetail[]` (one row per failing field) emitted by the FluentValidation pipeline behavior.
- **Single-field validation** — `Error.Validation(field, code)` with top-level `field` + `code` and `details: null`, emitted by post-validation guards (e.g. `category.notActive`).
- **Both shapes collapse** into `ApiError.fields: Record<string, readonly string[]>` of display copy (the row's `message` wins; falls back to `code`). Forms render the entry directly under the matching input; FluentValidation's PascalCase property names are normalised to camelCase at the form layer to match state keys.
- **Content-type guard** accepts both `application/json` (Makables-native `Error`) AND `application/problem+json` (RFC 7807 `ProblemDetails` / `ValidationProblemDetails`) — framework-level errors that bypass the Makables pipeline (model-binding 400s, framework 404) still resolve to typed `ApiError`.

**Why:** Sprint 6's biggest latent bug — `ApiError.fields` was promised by `result.ts` but `parseErrorResponse` never produced it (the wire shape is `details`, not `fields`). T-0049's product form was the first surface that actually depended on per-field flattening. Adding `application/problem+json` to the guard came one round later.

```ts
// frontend/src/lib/runtime/api-fetch.ts
if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
  // ... parse payload ...
  return {
    code, message, type,
    fields: collectValidationFields(payload.details, payload.field, code, payload.message, type),
    correlationId,
  };
}

function collectValidationFields(details, topField, topCode, topMessage, type): Record<string, readonly string[]> | undefined {
  const grouped: Record<string, string[]> = {};
  let matched = 0;
  if (Array.isArray(details) && details.length > 0) {
    for (const raw of details) {
      if (typeof raw?.field !== 'string') continue;
      const text = pickDisplay(raw.message, raw.code);
      if (text === null) continue;
      (grouped[raw.field] ??= []).push(text);
      matched++;
    }
  }
  // Single-field fallback: top-level field+code when details is empty and type === 'Validation'.
  if (matched === 0 && type === 'Validation' && typeof topField === 'string' && topField !== '') {
    const text = pickDisplay(topMessage, topCode);
    if (text !== null) {
      grouped[topField] = [text];
      matched++;
    }
  }
  return matched > 0 ? grouped : undefined;
}
```

**References:** T-0049 Copilot rounds 2 + 3 (validation flattening); T-0049 Copilot round 7 (problem+json guard).

### B.18 Plural-neutral Czech i18n strings

Czech plural-genitive morphology is unforgiving — `{count} objednávek` reads correctly for 0 and 5+, but `1 objednávek` and `3 objednávek` are ungrammatical. Until `t()` learns `Intl.PluralRules('cs')`, every `{count}` interpolation uses a "Label: N" shape (e.g. `'Objednávek: {count}'`) that's grammatical for any count.

**Why:** caught by T-0047's fourth Copilot review on the maker-profile order count. Documented as a deferred follow-up; the workaround keeps every catalog / dashboard surface launch-quality until the plural picker lands.

```ts
// frontend/src/lib/i18n/cs-CZ.ts (top-of-file convention comment)
// Czech plural-neutral phrasing (T-0047 Copilot review): the count
// interpolation skips the genitive-plural trap (1 → one, 2-4 → few,
// 0/5+ → many, fractional → other). Until t() learns Intl.PluralRules
// every {count} label takes a "Label: N" shape that's grammatical for
// every count.

'catalog.card.orders': 'Objednávek: {count}',
'catalog.pagination.results': 'Výrobců: {count}',
'dashboard.maker.products.card.image_count': 'Fotografií: {count}',
```

**References:** T-0047 round 4 (introduced); deferred carry-over for `Intl.PluralRules` integration.

### B.19 `buildProductImageUrl` — host-anchored blob URL builder

`buildProductImageUrl(blobPath)` in `lib/api-client-helpers/catalog.ts` turns a backend blob path into a renderable URL anchored on the Public host's image controller. Three invariants:

1. **Path normalisation.** Blob paths are stored as `{country}/products/{productId}/{filename}` but the controller route is `/api/v1/files/products/{country}/{productId}/{filename}`. The helper strips the duplicate `products/` segment so callers never have to think about it.
2. **`..` rejection.** Any blob path containing `..` segments returns `null`. Defense-in-depth — `next/image`'s `remotePatterns.hostname` already prevents off-origin fetches, but rejecting the path here keeps the optimizer from emitting a normalised same-host 404.
3. **Null pass-through.** Missing paths return `null` so consumers render a placeholder.

**Why:** every product image surface goes through this — maker profile cards, product detail gallery, maker dashboard. T-0047 introduced it; T-0048 added the `..` guard.

```ts
// frontend/src/lib/api-client-helpers/catalog.ts
export function buildProductImageUrl(blobPath: string | null | undefined): string | null {
  if (!blobPath) return null;
  // Defense-in-depth: reject path traversal.
  if (/(^|\/)\.\.(\/|$)/.test(blobPath)) return null;
  const baseUrl = process.env.NEXT_PUBLIC_API_PUBLIC_BASE_URL?.replace(/\/+$/, '') ?? 'http://localhost:5104';
  const normalised = blobPath.replace(/^\/+/, '').replace(/^([^/]+)\/products\//, '$1/');
  return `${baseUrl}/api/v1/files/products/${normalised}`;
}
```

Consumers pass the URL straight to `next/image` — the existing `images.remotePatterns` config in `next.config.ts` whitelists the host + `/api/v1/files/products/**` prefix so the optimizer accepts it.

**References:** T-0047 (introduced); T-0048 (`..` defense-in-depth).

---

## How to read this catalog

- **Architect** drafts ADRs that **accept**, **adapt**, or **reject** each pattern with rationale.
- **dotnet-backend** implements per the accepted ADRs and section A.
- **frontend** implements per the accepted ADRs and section B.
- **Reviewer** checks PRs against the catalog and the relevant ADRs.
- If a pattern needs to change, write a new ADR that supersedes the old one, then update this file.

**Never** import or read from any project folder outside this repository. This file plus the ADRs are the complete reference.


## Evolution loop

When reviewer flags the same finding ≥3× across PRs (tracked in docs/review/recurring-findings.md), architect promotes it to a new rule here AND, if mechanically catchable, adds a check to scripts/check-consistency.mjs. The loop: Reviewer log → Architect codification → Mechanical enforcement. Sprint 6's B.7–B.19 batch is the working precedent.
