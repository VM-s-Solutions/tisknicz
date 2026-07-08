namespace Makables.Core.Domain.Shipping;

/// <summary>
/// T-0146. The maker-side recipient details for a reverse (customer →
/// maker) shipment — resolved by the caller (<c>GenerateReturnLabel.Handler</c>)
/// from <c>Maker.RegisteredAddressId</c> (or the pickup address when
/// <c>Maker.PersonalPickupEnabled</c>) and passed to
/// <see cref="IShippingCarrier.CreateReturnShipmentAsync"/> so the adapter
/// stays decoupled from the <c>Makers</c>/<c>Addresses</c> aggregates —
/// same "adapter reads only what it's given" discipline as
/// <see cref="Payments.IPaymentProvider"/>.
/// </summary>
public sealed record ReturnRecipient(
    string Name,
    string Email,
    string Phone,
    string Street,
    string HouseNumber,
    string City,
    string Zip,
    string CountryCodeIso);
