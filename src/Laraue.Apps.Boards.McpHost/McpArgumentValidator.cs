using System.Text.Json;

namespace Laraue.Apps.Boards.McpHost;

/// <summary>
/// Checks a tool call's arguments against the tool's own input schema (required properties and their
/// JSON types) before the tool runs. The SDK's argument binding fails with a bare "An error occurred
/// invoking '...'" - dropping which argument was missing or malformed - so <see cref="McpToolCallFilter"/>
/// runs this first and reports the problem per argument, like a field error of a REST request.
/// Only the top-level properties are checked - nested values are left to the binder.
/// </summary>
public static class McpArgumentValidator
{
    /// <returns>Argument name to its errors; empty when the arguments are valid.</returns>
    public static Dictionary<string, string?[]> Validate(
        JsonElement inputSchema,
        IDictionary<string, JsonElement>? arguments)
    {
        var errors = new Dictionary<string, string?[]>();

        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return errors;
        }

        if (inputSchema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(x => x.GetString()).OfType<string>())
            {
                if (arguments is null || !arguments.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    errors[name] = ["Is required."];
                }
            }
        }

        if (arguments is null
            || !inputSchema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return errors;
        }

        foreach (var (name, value) in arguments)
        {
            if (errors.ContainsKey(name) || !properties.TryGetProperty(name, out var property))
            {
                continue;
            }

            var error = ValidateType(property, value);
            if (error is not null)
            {
                errors[name] = [error];
            }
        }

        return errors;
    }

    private static string? ValidateType(JsonElement property, JsonElement value)
    {
        if (!property.TryGetProperty("type", out var type))
        {
            return null;
        }

        var allowedTypes = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToArray()
            : type.GetString() is { } single ? [single] : [];

        if (allowedTypes.Length == 0 || allowedTypes.Any(x => Matches(x, value)))
        {
            return IsValidFormat(property, value) ? null : $"Must be a valid {property.GetProperty("format").GetString()}.";
        }

        var allowed = string.Join(" or ", allowedTypes.Where(x => x != "null"));

        return $"Must be of type {(allowed.Length > 0 ? allowed : "null")}, got {Describe(value.ValueKind)}.";
    }

    private static bool Matches(string schemaType, JsonElement value)
    {
        return schemaType switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number
                         && (value.TryGetInt64(out _) || value.TryGetDecimal(out var number) && number == decimal.Truncate(number)),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        };
    }

    private static bool IsValidFormat(JsonElement property, JsonElement value)
    {
        return value.ValueKind != JsonValueKind.String
               || !property.TryGetProperty("format", out var format)
               || format.GetString() != "uuid"
               || Guid.TryParse(value.GetString(), out _);
    }

    private static string Describe(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            _ => "null",
        };
    }
}
