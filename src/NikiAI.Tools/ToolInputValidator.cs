using System.Text.Json;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Focused input validator implementing the documented schema subset:
/// - Validates JSON syntax and root object structure.
/// - Validates required fields presence and non-blank string rules.
/// - Validates property data types (string, integer, number, boolean, array, object).
/// - Validates string minLength/maxLength, numeric minimum/maximum, and date-time format.
/// 
/// Note: This is an explicit, focused schema validation engine tailored for agent tool calls;
/// it does not claim complete draft-07/2020-12 compliance beyond this documented subset.
/// </summary>
public static class ToolInputValidator
{
    public static bool Validate(string? inputJson, string? schemaJson, out string? errorMessage)
    {
        errorMessage = null;

        // If schema is empty or "{}" and input is null/empty or "{}", that's valid
        bool isSchemaEmpty = string.IsNullOrWhiteSpace(schemaJson) || schemaJson.Trim() == "{}";
        if (isSchemaEmpty && (string.IsNullOrWhiteSpace(inputJson) || inputJson.Trim() == "{}"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            errorMessage = "Input payload cannot be null or empty.";
            return false;
        }

        JsonDocument inputDoc;
        try
        {
            inputDoc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException ex)
        {
            errorMessage = $"Malformed JSON input: {ex.Message}";
            return false;
        }

        using (inputDoc)
        {
            if (inputDoc.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Input payload must be a JSON object.";
                return false;
            }

            if (isSchemaEmpty)
            {
                return true;
            }

            JsonDocument schemaDoc;
            try
            {
                schemaDoc = JsonDocument.Parse(schemaJson!);
            }
            catch (JsonException ex)
            {
                errorMessage = $"Malformed schema specification: {ex.Message}";
                return false;
            }

            using (schemaDoc)
            {
                var schemaRoot = schemaDoc.RootElement;
                var inputRoot = inputDoc.RootElement;

                // Validate required properties
                if (schemaRoot.TryGetProperty("required", out var requiredElem) &&
                    requiredElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var reqItem in requiredElem.EnumerateArray())
                    {
                        var propName = reqItem.GetString();
                        if (string.IsNullOrEmpty(propName)) continue;

                        if (!inputRoot.TryGetProperty(propName, out var propValue) ||
                            propValue.ValueKind == JsonValueKind.Null ||
                            propValue.ValueKind == JsonValueKind.Undefined)
                        {
                            errorMessage = $"Missing required property '{propName}'.";
                            return false;
                        }

                        // For required strings, enforce non-empty/non-whitespace
                        if (propValue.ValueKind == JsonValueKind.String)
                        {
                            var strVal = propValue.GetString();
                            if (string.IsNullOrWhiteSpace(strVal))
                            {
                                errorMessage = $"Required property '{propName}' cannot be empty or whitespace.";
                                return false;
                            }
                        }
                    }
                }

                // Validate properties definitions
                if (schemaRoot.TryGetProperty("properties", out var propertiesElem) &&
                    propertiesElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propDef in propertiesElem.EnumerateObject())
                    {
                        var propName = propDef.Name;
                        if (!inputRoot.TryGetProperty(propName, out var actualVal))
                        {
                            continue; // Optional property absent
                        }

                        if (actualVal.ValueKind == JsonValueKind.Null || actualVal.ValueKind == JsonValueKind.Undefined)
                        {
                            continue;
                        }

                        var spec = propDef.Value;

                        // Type check
                        if (spec.TryGetProperty("type", out var typeElem))
                        {
                            var expectedType = typeElem.GetString();
                            if (!IsKindMatchingType(actualVal.ValueKind, expectedType))
                            {
                                errorMessage = $"Property '{propName}' expected type '{expectedType}', but got '{actualVal.ValueKind}'.";
                                return false;
                            }
                        }

                        // String constraints
                        if (actualVal.ValueKind == JsonValueKind.String)
                        {
                            var strVal = actualVal.GetString() ?? string.Empty;

                            if (spec.TryGetProperty("minLength", out var minLenElem) && minLenElem.TryGetInt32(out var minLen))
                            {
                                if (strVal.Length < minLen)
                                {
                                    errorMessage = $"Property '{propName}' length ({strVal.Length}) must be at least {minLen}.";
                                    return false;
                                }
                            }

                            if (spec.TryGetProperty("maxLength", out var maxLenElem) && maxLenElem.TryGetInt32(out var maxLen))
                            {
                                if (strVal.Length > maxLen)
                                {
                                    errorMessage = $"Property '{propName}' length ({strVal.Length}) exceeds maximum of {maxLen}.";
                                    return false;
                                }
                            }

                            if (spec.TryGetProperty("format", out var formatElem))
                            {
                                var format = formatElem.GetString();
                                if (format == "date-time")
                                {
                                    if (!DateTimeOffset.TryParse(strVal, out _))
                                    {
                                        errorMessage = $"Property '{propName}' must be a valid ISO 8601 date-time string.";
                                        return false;
                                    }
                                }
                            }
                        }

                        // Number / Integer constraints
                        if (actualVal.ValueKind == JsonValueKind.Number)
                        {
                            if (spec.TryGetProperty("minimum", out var minElem) && minElem.TryGetDouble(out var minVal))
                            {
                                if (actualVal.GetDouble() < minVal)
                                {
                                    errorMessage = $"Property '{propName}' value ({actualVal.GetDouble()}) is less than minimum {minVal}.";
                                    return false;
                                }
                            }

                            if (spec.TryGetProperty("maximum", out var maxElem) && maxElem.TryGetDouble(out var maxVal))
                            {
                                if (actualVal.GetDouble() > maxVal)
                                {
                                    errorMessage = $"Property '{propName}' value ({actualVal.GetDouble()}) exceeds maximum {maxVal}.";
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    public static void ValidateOrThrow(string toolId, string? inputJson, string? schemaJson)
    {
        if (!Validate(inputJson, schemaJson, out var error))
        {
            throw new ToolValidationException(toolId, error!);
        }
    }

    private static bool IsKindMatchingType(JsonValueKind kind, string? expectedType)
    {
        return (expectedType?.ToLowerInvariant()) switch
        {
            "string" => kind == JsonValueKind.String,
            "integer" => kind == JsonValueKind.Number, // JsonValueKind does not differentiate integer vs float
            "number" => kind == JsonValueKind.Number,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "array" => kind == JsonValueKind.Array,
            "object" => kind == JsonValueKind.Object,
            _ => true // Unspecified or custom type allowed
        };
    }
}
