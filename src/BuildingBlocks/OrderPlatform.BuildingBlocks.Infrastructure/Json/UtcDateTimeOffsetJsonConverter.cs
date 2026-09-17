using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Json;

/// <summary>
/// Timestamps are written in UTC as ISO 8601 with a <c>Z</c> suffix (<c>2026-09-17T10:15:00Z</c>). Input with any offset is
/// accepted and normalised to UTC; input without an offset is rejected because its meaning is ambiguous (ADR-0020).
/// </summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();

        return text is not null
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            && HasExplicitOffset(text)
                ? value.ToUniversalTime()
                : throw new JsonException("Timestamps must be ISO 8601 with an offset, e.g. \"2026-09-17T10:15:00Z\".");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));

    private static bool HasExplicitOffset(string text)
    {
        var timeStart = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        if (timeStart < 0)
        {
            return false;
        }

        var time = text.AsSpan(timeStart);
        return time.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || time.LastIndexOfAny('+', '-') > 0;
    }
}
