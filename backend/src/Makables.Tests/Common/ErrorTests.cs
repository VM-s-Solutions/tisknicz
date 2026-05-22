using FluentAssertions;
using Makables.Core.Domain.Common;

namespace Makables.Tests.Common;

public class ErrorTests
{
    [Fact]
    public void NotFound_Shapes_Code_From_Entity_Name()
    {
        var error = Error.NotFound("order");

        error.Field.Should().Be("order");
        error.Code.Should().Be("order.notFound");
        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void NotFound_Lowercases_The_Entity_Name_In_Code()
    {
        var error = Error.NotFound("Maker");

        error.Code.Should().Be("maker.notFound");
        // Field keeps original casing for callers that want to display it.
        error.Field.Should().Be("Maker");
    }

    [Fact]
    public void Validation_From_Details_Picks_First_Field()
    {
        var details = new List<ValidationDetail>
        {
            new("Email", "validation.invalidEmail", "Invalid email"),
            new("Phone", "validation.required", "Required")
        };

        var error = Error.Validation(details);

        error.Field.Should().Be("Email");
        error.Code.Should().Be("validation.failed");
        error.Type.Should().Be(ErrorType.Validation);
        error.Details.Should().BeSameAs(details);
    }

    [Fact]
    public void Validation_From_Empty_List_Has_Empty_Field()
    {
        var error = Error.Validation(new List<ValidationDetail>());

        error.Field.Should().BeEmpty();
        error.Code.Should().Be("validation.failed");
    }

    [Fact]
    public void Transient_Has_Transient_Type()
    {
        var error = Error.Transient("payment.gatewayUnavailable");

        error.Type.Should().Be(ErrorType.Transient);
        error.Code.Should().Be("payment.gatewayUnavailable");
        error.Field.Should().BeEmpty();
    }

    [Fact]
    public void Configuration_Has_Configuration_Type()
    {
        var error = Error.Configuration("payment.gatewayMisconfigured");

        error.Type.Should().Be(ErrorType.Configuration);
        error.Code.Should().Be("payment.gatewayMisconfigured");
    }

    [Fact]
    public void Forbidden_Has_Default_Code()
    {
        var error = Error.Forbidden();

        error.Type.Should().Be(ErrorType.Forbidden);
        error.Code.Should().Be("auth.forbidden");
    }

    [Fact]
    public void Unauthorized_Has_Default_Code()
    {
        var error = Error.Unauthorized();

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("auth.required");
    }
}
