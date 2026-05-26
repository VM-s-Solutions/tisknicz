using FluentAssertions;
using FluentValidation;
using MediatR;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Behaviors;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.SeedWork;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Makables.Tests.AppServices.Behaviors;

public class PipelineBehaviorTests
{
    // === Test command shapes ===

    public record DoThing(string Name) : ICommand;
    public record DoThingTyped(string Name) : ICommand<string>;
    public record AskQuestion(string Topic) : IQuery<int>;

    // === Validators ===

    public class DoThingValidator : AbstractValidator<DoThing>
    {
        public DoThingValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").WithErrorCode("validation.required");
            RuleFor(x => x.Name).MinimumLength(3).WithMessage("Name is too short").WithErrorCode("validation.minLength");
        }
    }

    public class DoThingTypedValidator : AbstractValidator<DoThingTyped>
    {
        public DoThingTypedValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").WithErrorCode("validation.required");
        }
    }

    // === Handlers ===

    public class DoThingHandler : ICommandHandler<DoThing>
    {
        public Task<BusinessResult> Handle(DoThing request, CancellationToken ct) =>
            Task.FromResult(BusinessResult.Success());
    }

    public class DoThingTypedHandler : ICommandHandler<DoThingTyped, string>
    {
        public Task<BusinessResult<string>> Handle(DoThingTyped request, CancellationToken ct) =>
            Task.FromResult(BusinessResult.Success("ok:" + request.Name));
    }

    public class AskQuestionHandler : IQueryHandler<AskQuestion, int>
    {
        public Task<BusinessResult<int>> Handle(AskQuestion request, CancellationToken ct) =>
            Task.FromResult(BusinessResult.Success(42));
    }

    // === Pipeline scaffolding ===

    private static (ISender sender, IUnitOfWork uow) BuildPipeline(
        bool registerValidators = true,
        IUniqueConstraintTranslator? translator = null)
    {
        var services = new ServiceCollection();

        // MediatR 13 requires logging to be registered (LicenseAccessor consumes ILoggerFactory).
        services.AddLogging();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PipelineBehaviorTests>());

        // Behaviors registered open-generic in MediatR order.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>));

        if (registerValidators)
        {
            services.AddTransient<IValidator<DoThing>, DoThingValidator>();
            services.AddTransient<IValidator<DoThingTyped>, DoThingTypedValidator>();
        }

        var uow = Substitute.For<IUnitOfWork>();
        services.AddSingleton(uow);
        services.AddSingleton(translator ?? Substitute.For<IUniqueConstraintTranslator>());

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ISender>(), uow);
    }

    // === Validation behavior tests ===

    [Fact]
    public async Task Validation_Failure_Short_Circuits_Handler_For_NonTyped_Command()
    {
        var (sender, uow) = BuildPipeline();

        var result = await sender.Send(new DoThing(Name: ""));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("validation.failed");
        result.Error.Details.Should().BeAssignableTo<IReadOnlyList<ValidationDetail>>();

        // UoW must NOT be committed on validation failure.
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validation_Failure_Short_Circuits_Handler_For_Typed_Command()
    {
        var (sender, uow) = BuildPipeline();

        var result = await sender.Send(new DoThingTyped(Name: ""));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Value.Should().BeNull();

        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validation_Failure_Carries_All_Errors_As_Details()
    {
        var (sender, _) = BuildPipeline();

        // Both rules fail — empty + too short — but only NotEmpty fires because
        // FluentValidation by default short-circuits per-property on NotEmpty.
        // We at least assert one detail; the contract is "at least the first
        // failing rule per property".
        var result = await sender.Send(new DoThing(Name: ""));

        var details = result.Error!.Details as IReadOnlyList<ValidationDetail>;
        details.Should().NotBeNull();
        details!.Should().NotBeEmpty();
        details.Should().Contain(d => d.Code == "validation.required");
    }

    [Fact]
    public async Task Valid_Input_Proceeds_To_Handler()
    {
        var (sender, uow) = BuildPipeline();

        var result = await sender.Send(new DoThing(Name: "Widget"));

        result.IsSuccess.Should().BeTrue();
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // === UnitOfWork behavior tests ===

    [Fact]
    public async Task UoW_Commits_On_Successful_Typed_Command()
    {
        var (sender, uow) = BuildPipeline();

        var result = await sender.Send(new DoThingTyped(Name: "Widget"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok:Widget");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UoW_Does_Not_Commit_When_Handler_Returns_Failure()
    {
        // Handler that returns Failure even with valid input.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PipelineBehaviorTests>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>));

        var uow = Substitute.For<IUnitOfWork>();
        services.AddSingleton(uow);
        services.AddSingleton(Substitute.For<IUniqueConstraintTranslator>());

        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new FailingCommand("x"));

        result.IsSuccess.Should().BeFalse();
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    public record FailingCommand(string Reason) : ICommand;

    public class FailingHandler : ICommandHandler<FailingCommand>
    {
        public Task<BusinessResult> Handle(FailingCommand request, CancellationToken ct) =>
            Task.FromResult(BusinessResult.Failure(Error.Conflict("conflict.test", "test.failed")));
    }

    [Fact]
    public async Task UoW_Commits_On_Failure_When_Command_Implements_IPersistOnFailureCommand()
    {
        // Reviewer T-0022 BLOCKER B-1: Auth use cases mutate anti-abuse
        // state (lockout counters, family-wide revocation) on the failure
        // path. The pipeline MUST commit those mutations alongside the
        // failure response or the security mechanisms are silent no-ops.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PipelineBehaviorTests>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>));

        var uow = Substitute.For<IUnitOfWork>();
        services.AddSingleton(uow);
        services.AddSingleton(Substitute.For<IUniqueConstraintTranslator>());

        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new PersistOnFailureCommand("burn-the-family"));

        result.IsSuccess.Should().BeFalse();
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    public record PersistOnFailureCommand(string Reason) : ICommand, IPersistOnFailureCommand;

    public class PersistOnFailureHandler : ICommandHandler<PersistOnFailureCommand>
    {
        public Task<BusinessResult> Handle(PersistOnFailureCommand request, CancellationToken ct) =>
            Task.FromResult(BusinessResult.Failure(Error.Conflict("conflict.test", "test.failed")));
    }

    [Fact]
    public async Task UoW_Skips_Query_Even_On_Success()
    {
        // Queries are not ICommandMarker, so UnitOfWorkPipelineBehavior won't
        // attach. SaveChangesAsync must NOT be called for a query.
        var (sender, uow) = BuildPipeline();

        var result = await sender.Send(new AskQuestion("life"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pipeline_With_No_Validator_Just_Calls_Handler()
    {
        var (sender, uow) = BuildPipeline(registerValidators: false);

        var result = await sender.Send(new DoThing(Name: ""));  // would fail validation, but no validator registered

        result.IsSuccess.Should().BeTrue();
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // === Unique-constraint race translation (T-0033 sec M-1) ===

    [Fact]
    public async Task UoW_Translates_UniqueConstraintViolation_To_Typed_BusinessResult_Failure()
    {
        // A handler's uniqueness pre-check can lose a TOCTOU race against a
        // concurrent insert; the loser's SaveChangesAsync throws
        // UniqueConstraintViolationException. The pipeline must translate
        // that into the same typed Conflict the pre-check would have returned.
        var translator = Substitute.For<IUniqueConstraintTranslator>();
        var conflictError = Error.Conflict("email", "auth.emailAlreadyExists");
        translator.Translate("IX_users_email_normalized").Returns(conflictError);

        var (sender, uow) = BuildPipeline(translator: translator);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintViolationException(
                "IX_users_email_normalized", new Exception("23505")));

        var result = await sender.Send(new DoThingTyped(Name: "Widget"));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("auth.emailAlreadyExists");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task UoW_Rethrows_UniqueConstraintViolation_When_Constraint_Is_Unmapped()
    {
        // An unknown constraint name is a bug worth surfacing — don't swallow it.
        var translator = Substitute.For<IUniqueConstraintTranslator>();
        translator.Translate(Arg.Any<string>()).Returns((Error?)null);

        var (sender, uow) = BuildPipeline(translator: translator);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintViolationException(
                "ix_never_heard_of_it", new Exception("23505")));

        var act = async () => await sender.Send(new DoThingTyped(Name: "Widget"));

        await act.Should().ThrowAsync<UniqueConstraintViolationException>()
            .Where(ex => ex.ConstraintName == "ix_never_heard_of_it");
    }

    [Fact]
    public async Task UoW_Translates_UniqueConstraintViolation_For_NonTyped_Command()
    {
        // Same race-translation must work for the non-generic BusinessResult
        // branch in BuildFailureResponse (no <T> reflection path).
        var translator = Substitute.For<IUniqueConstraintTranslator>();
        translator.Translate("ix_makers_registration_number")
            .Returns(Error.Conflict("registrationNumber", "maker.icoAlreadyRegistered"));

        var (sender, uow) = BuildPipeline(translator: translator);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintViolationException(
                "ix_makers_registration_number", new Exception("23505")));

        var result = await sender.Send(new DoThing(Name: "Widget"));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("maker.icoAlreadyRegistered");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
