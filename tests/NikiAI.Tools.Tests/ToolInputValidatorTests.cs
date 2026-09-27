using NikiAI.Core.Tools;
using NikiAI.Tools;
using Xunit;

namespace NikiAI.Tools.Tests;

public class ToolInputValidatorTests
{
    private const string SimpleSchema = """
    {
      "type": "object",
      "required": ["name", "age"],
      "properties": {
        "name": { "type": "string", "minLength": 2, "maxLength": 50 },
        "age": { "type": "integer", "minimum": 1, "maximum": 120 },
        "created_at": { "type": "string", "format": "date-time" },
        "is_active": { "type": "boolean" }
      }
    }
    """;

    [Fact]
    public void Validate_ValidInput_ReturnsTrue()
    {
        var json = """
        {
          "name": "Alice",
          "age": 30,
          "created_at": "2026-09-20T12:00:00Z",
          "is_active": true
        }
        """;

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_MissingRequiredProperty_ReturnsFalse()
    {
        var json = """{ "name": "Bob" }""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("Missing required property 'age'", error);
    }

    [Fact]
    public void Validate_EmptyRequiredString_ReturnsFalse()
    {
        var json = """{ "name": "   ", "age": 25 }""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("cannot be empty or whitespace", error);
    }

    [Fact]
    public void Validate_TypeMismatch_ReturnsFalse()
    {
        var json = """{ "name": 12345, "age": 25 }""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("expected type 'string'", error);
    }

    [Fact]
    public void Validate_StringMinLengthViolation_ReturnsFalse()
    {
        var json = """{ "name": "A", "age": 25 }""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("must be at least 2", error);
    }

    [Fact]
    public void Validate_NumericRangeViolation_ReturnsFalse()
    {
        var json = """{ "name": "Charlie", "age": 150 }""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("exceeds maximum 120", error);
    }

    [Fact]
    public void Validate_InvalidDateTimeFormat_ReturnsFalse()
    {
        var json = """
        {
          "name": "David",
          "age": 40,
          "created_at": "not-a-date"
        }
        """;

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("must be a valid ISO 8601 date-time string", error);
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsFalse()
    {
        var json = """{ "name": "Eve", age: 20 """;

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("Malformed JSON input", error);
    }

    [Fact]
    public void Validate_ArrayRootJson_ReturnsFalse()
    {
        var json = """[1, 2, 3]""";

        var valid = ToolInputValidator.Validate(json, SimpleSchema, out var error);

        Assert.False(valid);
        Assert.Contains("must be a JSON object", error);
    }

    [Fact]
    public void ValidateOrThrow_ThrowsOnInvalid()
    {
        var json = """{ "name": "Frank" }""";

        var ex = Assert.Throws<ToolValidationException>(() =>
            ToolInputValidator.ValidateOrThrow("test_tool", json, SimpleSchema));

        Assert.Equal("test_tool", ex.ToolId);
        Assert.Contains("Missing required property 'age'", ex.ValidationError);
    }
}
