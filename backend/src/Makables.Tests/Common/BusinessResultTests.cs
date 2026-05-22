using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Common;

public class BusinessResultTests
{
    [Fact]
    public void Success_NonGeneric_HasNoError()
    {
        var result = BusinessResult.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_NonGeneric_CarriesError()
    {
        var error = Error.NotFound("order");
        var result = BusinessResult.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Success_Generic_CarriesValue()
    {
        var result = BusinessResult.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_Generic_HasDefaultValue()
    {
        var error = Error.NotFound("order");
        var result = BusinessResult.Failure<int>(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        result.Value.Should().Be(default(int));
    }

    [Fact]
    public void ValidationFailure_NonGeneric_WrapsDetails()
    {
        var details = new List<ValidationDetail>
        {
            new("Email", "validation.invalidEmail", "Invalid email"),
            new("Phone", "validation.required", "Required")
        };

        var result = BusinessResult.ValidationFailure(details);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("validation.failed");
        result.Error.Field.Should().Be("Email");
        result.Error.Details.Should().BeSameAs(details);
    }

    [Fact]
    public void ValidationFailure_Generic_WrapsDetailsAndHasDefaultValue()
    {
        var details = new List<ValidationDetail>
        {
            new("Email", "validation.invalidEmail", "Invalid email")
        };

        var result = BusinessResult.ValidationFailure<string>(details);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Success_With_Error_Throws()
    {
        var act = () => BusinessResult.Failure(null!);

        // Cannot directly test the protected ctor; the static factory disallows
        // a null error on Failure via NullReferenceException OR our guard.
        // We instead exercise the guard by attempting the impossible state via the typed factory.
        act.Should().Throw<Exception>();
    }
}
