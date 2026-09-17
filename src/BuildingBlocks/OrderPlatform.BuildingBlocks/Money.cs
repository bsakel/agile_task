namespace OrderPlatform.BuildingBlocks;

/// <summary>
/// An amount of money. Serialized in the HTTP API as <c>{ "amount": "1234.50", "currency": "EUR" }</c> with the amount as a
/// decimal string (ADR-0020). v1 is single-currency EUR (README §2).
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public const string Eur = "EUR";

    public static Money InEur(decimal amount) => new(amount, Eur);
}
