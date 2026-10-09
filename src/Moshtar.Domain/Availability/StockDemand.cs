using Moshtar.Domain.Common;

namespace Moshtar.Domain.Availability;

/// <summary>Een aantal exemplaren van één artikel dat in een periode bezet is of gevraagd wordt.</summary>
public readonly record struct StockDemand(Guid RentalItemId, int Quantity, DateRange Period);

/// <summary>Op deze dag is er voor dit artikel te weinig voorraad.</summary>
public readonly record struct StockShortage(Guid RentalItemId, DateOnly Day, int Stock, int Required);
