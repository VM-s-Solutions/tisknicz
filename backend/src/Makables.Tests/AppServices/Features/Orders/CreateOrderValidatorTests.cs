using FluentAssertions;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;

namespace Makables.Tests.AppServices.Features.Orders;

/// <summary>
/// Pins the T-0063 CreateOrder.Validator rules. Synchronous only — no
/// DB-backed existence checks; the handler owns ProductNotFound and the
/// maker-state gates so the validator stays fast.
/// </summary>
public class CreateOrderValidatorTests
{
    private readonly CreateOrder.Validator _sut = new();

    private static CreateOrder.Command ZasilkovnaCommand(
        string? productId = "prod-1",
        int quantity = 1,
        string? pickupPointId = "pp-42",
        string customerName = "Anna Nováková",
        string customerEmail = "anna@example.cz",
        string customerPhone = "+420 723 456 789",
        string? customerNotes = null) =>
        new(
            ProductId: productId!,
            Quantity: quantity,
            ShippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
            ZasilkovnaPickupPointId: pickupPointId,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            CustomerPhone: customerPhone,
            CustomerNotes: customerNotes);

    private static CreateOrder.Command PersonalPickupCommand(string? pickupPointId = null) =>
        new(
            ProductId: "prod-1",
            Quantity: 1,
            ShippingMethod: ShippingMethod.PersonalPickup,
            ZasilkovnaPickupPointId: pickupPointId,
            CustomerName: "Anna Nováková",
            CustomerEmail: "anna@example.cz",
            CustomerPhone: "+420 723 456 789",
            CustomerNotes: null);

    [Fact]
    public void Passes_for_well_formed_zasilkovna_command()
    {
        var result = _sut.Validate(ZasilkovnaCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Passes_for_well_formed_personal_pickup_command()
    {
        var result = _sut.Validate(PersonalPickupCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_blank_productId_with_Required()
    {
        var result = _sut.Validate(ZasilkovnaCommand(productId: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.ProductId)
            && e.ErrorCode == BusinessErrorMessage.Required);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Rejects_quantity_other_than_one_with_OrderInvalidQuantity(int quantity)
    {
        var result = _sut.Validate(ZasilkovnaCommand(quantity: quantity));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.Quantity)
            && e.ErrorCode == BusinessErrorMessage.OrderInvalidQuantity);
    }

    [Fact]
    public void Rejects_blank_zasilkovnaPickupPointId_when_method_is_zasilkovna()
    {
        var result = _sut.Validate(ZasilkovnaCommand(pickupPointId: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.ZasilkovnaPickupPointId)
            && e.ErrorCode == BusinessErrorMessage.Required);
    }

    [Fact]
    public void Allows_null_zasilkovnaPickupPointId_when_method_is_personal_pickup()
    {
        var result = _sut.Validate(PersonalPickupCommand(pickupPointId: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_invalid_email_with_InvalidEmailFormat()
    {
        var result = _sut.Validate(ZasilkovnaCommand(customerEmail: "not-an-email"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.CustomerEmail)
            && e.ErrorCode == BusinessErrorMessage.InvalidEmailFormat);
    }

    [Fact]
    public void Rejects_phone_not_matching_czech_pattern()
    {
        // US number — clearly outside the Czech pattern. Pinned so a
        // future relaxation of the regex doesn't silently widen the
        // accepted set without a deliberate rule update.
        var result = _sut.Validate(ZasilkovnaCommand(customerPhone: "+1 415 555 0123"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.CustomerPhone)
            && e.ErrorCode == BusinessErrorMessage.InvalidPhoneFormat);
    }

    [Theory]
    [InlineData("+420 723 456 789")]
    [InlineData("+420723456789")]
    [InlineData("723 456 789")]
    [InlineData("723456789")]
    [InlineData("+420 612 345 678")]
    public void Accepts_czech_phone_with_or_without_plus420_prefix(string phone)
    {
        var result = _sut.Validate(ZasilkovnaCommand(customerPhone: phone));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_customer_notes_over_2000_chars()
    {
        var tooLong = new string('a', 2001);

        var result = _sut.Validate(ZasilkovnaCommand(customerNotes: tooLong));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(CreateOrder.Command.CustomerNotes)
            && e.ErrorCode == BusinessErrorMessage.MaxLength);
    }
}
