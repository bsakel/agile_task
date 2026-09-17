using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Json;

/// <summary>
/// <c>{ "amount": "1234.50", "currency": "EUR" }</c>: the amount is a decimal string so clients that parse JSON numbers as
/// floating point cannot lose precision (ADR-0020). JSON numbers are rejected.
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object with 'amount' and 'currency'.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            if (string.Equals(property, "amount", StringComparison.OrdinalIgnoreCase))
            {
                amount = reader.TokenType == JsonTokenType.String
                    && decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                        ? value
                        : throw new JsonException("Money 'amount' must be a decimal string, e.g. \"1234.50\".");
            }
            else if (string.Equals(property, "currency", StringComparison.OrdinalIgnoreCase))
            {
                currency = reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : throw new JsonException("Money 'currency' must be a string.");
            }
            else
            {
                // Unknown fields are ignored (ADR-0020).
                reader.Skip();
            }
        }

        return amount is not null && !string.IsNullOrWhiteSpace(currency)
            ? new Money(amount.Value, currency)
            : throw new JsonException("Money requires both 'amount' and 'currency'.");
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("amount", value.Amount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
