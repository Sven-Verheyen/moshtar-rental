using Moshtar.Domain.Customers;

namespace Moshtar.Application.Reservations;

/// <summary>
/// Reserveren geweigerd omdat de btw-databank het EU-btw-nummer van de zakelijke klant niet kent: het is ongeldig. Een
/// onbereikbare btw-databank houdt een reservatie nooit tegen.
/// </summary>
public sealed class UnknownVatNumberException() : ArgumentException(CustomerDetails.UnknownVatNumber);
