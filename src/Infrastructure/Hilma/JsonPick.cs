using System.Globalization;
using System.Text.Json;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Tolerant readers over an unverified JSON shape. The Hilma response shapes have not been
/// confirmed against the live API yet, and legacy vs eForms field names differ, so every read
/// tries a list of candidate names and returns null rather than throwing.
/// </summary>
internal static class JsonPick
{
    /// <summary>Finds the first present property among <paramref name="names"/>, searching nested objects breadth-first.</summary>
    public static JsonElement? Property(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var direct) && direct.ValueKind is not JsonValueKind.Null)
                return direct;
        }

        foreach (var child in element.EnumerateObject())
        {
            if (child.Value.ValueKind is not JsonValueKind.Object) continue;
            var nested = Property(child.Value, names);
            if (nested is not null) return nested;
        }

        return null;
    }

    public static string? String(JsonElement element, params string[] names)
    {
        var found = Property(element, names);
        if (found is null) return null;

        return found.Value.ValueKind switch
        {
            JsonValueKind.String => Trim(found.Value.GetString()),
            JsonValueKind.Number => found.Value.GetRawText(),
            // eForms wraps many values as { "fi": "...", "sv": "..." } or [{ "value": "..." }].
            JsonValueKind.Object => Trim(FirstStringIn(found.Value)),
            JsonValueKind.Array => Trim(found.Value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : FirstStringIn(item))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))),
            _ => null,
        };

        static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        static string? FirstStringIn(JsonElement element) => element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.String)
                .Select(property => property.Value.GetString())
                .FirstOrDefault()
            : null;
    }

    public static DateTimeOffset? Date(JsonElement element, params string[] names)
    {
        var raw = String(element, names);
        if (raw is null) return null;

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    public static decimal? Decimal(JsonElement element, params string[] names)
    {
        var found = Property(element, names);
        if (found is null) return null;

        if (found.Value.ValueKind == JsonValueKind.Number && found.Value.TryGetDecimal(out var number))
            return number;

        var raw = String(element, names);
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Collects string values from every matching name, accepting a bare string, an array, or a
    /// wrapper object such as <c>{ "code": "72000000" }</c>. Unlike the scalar readers this unions
    /// across names rather than taking the first hit — CPV codes routinely arrive split across a
    /// main code and an additional-codes list.
    /// </summary>
    public static List<string> Strings(JsonElement element, params string[] names)
    {
        var values = new List<string?>();
        foreach (var name in names)
        {
            var found = Property(element, name);
            if (found is not null) values.AddRange(Flatten(found.Value));
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static IEnumerable<string?> Flatten(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => [element.GetString()],
            JsonValueKind.Number => [element.GetRawText()],
            JsonValueKind.Object => [String(element, "code", "cpvCode", "id", "value")],
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Flatten),
            _ => [],
        };
    }

    /// <summary>Locates the result array in a search response, whether it is the root or nested under a wrapper property.</summary>
    public static JsonElement? ArrayOf(JsonElement root, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;

        var found = Property(root, names);
        return found?.ValueKind == JsonValueKind.Array ? found : null;
    }
}
