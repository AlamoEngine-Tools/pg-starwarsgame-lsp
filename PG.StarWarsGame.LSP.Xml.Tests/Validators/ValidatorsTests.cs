using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Validators;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validators;

// ── helpers ──────────────────────────────────────────────────────────────────

file static class TagOf
{
    public static XmlTagDefinition Make(string name, XmlValueType type, TagSemanticType semanticType = TagSemanticType.Default)
    {
        return new XmlTagDefinition { Tag = name, ValueType = type, SemanticType = semanticType };
    }
}

// ── FloatValueValidator ───────────────────────────────────────────────────────

public sealed class FloatValueValidatorTests
{
    private static readonly FloatValueValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Max_Speed", XmlValueType.Float);

    [Theory]
    [InlineData("1.23")]
    [InlineData("1.23f")]
    [InlineData("1.23F")]
    [InlineData("-5")]
    [InlineData("1500")]
    [InlineData("0.0")]
    [InlineData(".5")]
    [InlineData("-40.0")]
    public void Valid_float_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("")]
    [InlineData("1.0ff")]
    public void Invalid_float_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── ShaderVersionHexValidator ─────────────────────────────────────────────────

public sealed class ShaderVersionHexValidatorTests
{
    private static readonly ShaderVersionHexValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("PixelShaderVersionHEX", XmlValueType.ShaderVersionHex);

    [Theory]
    [InlineData("0x0200")]
    [InlineData("0x0000")]
    [InlineData("0x0100")]
    [InlineData("0xDEAD")]
    [InlineData("0Xabcd")]
    public void Valid_hex_literals_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("0200")]
    [InlineData("0xGGGG")]
    [InlineData("0x")]
    [InlineData("")]
    [InlineData("0x 200")]
    public void Invalid_hex_literals_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── VendorIdHexValidator ──────────────────────────────────────────────────────

public sealed class VendorIdHexValidatorTests
{
    private static readonly VendorIdHexValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("VendorIDHEX", XmlValueType.VendorIdHex);

    [Theory]
    [InlineData("0x8086")]
    [InlineData("0x10DE")]
    [InlineData("0x0000")]
    public void Valid_vendor_hex_passes(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("8086")]
    [InlineData("0x")]
    [InlineData("")]
    public void Invalid_vendor_hex_fails(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── DynamicEnumValueValidator ─────────────────────────────────────────────────

public sealed class DynamicEnumValueValidatorTests
{
    private static readonly DynamicEnumValueValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("MovementClass", XmlValueType.DynamicEnumValue);
    private static readonly XmlTagDefinition FlagTag = TagOf.Make("CategoryMask", XmlValueType.DynamicEnumValue, TagSemanticType.FlagList);

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Build Pad")]
    [InlineData("Galactic_Automatic")]
    [InlineData("1x1")]
    public void Valid_single_enum_identifiers_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    [InlineData("Build Pad")]
    public void Valid_flag_list_identifiers_pass(string value)
    {
        Assert.True(Sut.Validate(value, FlagTag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    [InlineData("Infantry | Vehicle | Air")]
    public void Pipe_on_single_enum_tag_fails(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Infantry|")]
    [InlineData("|Infantry")]
    public void Invalid_flag_list_identifiers_fail(string value)
    {
        Assert.False(Sut.Validate(value, FlagTag).IsValid);
    }
}

// ── PrerequisiteExpressionValidator ──────────────────────────────────────────

public sealed class PrerequisiteExpressionValidatorTests
{
    private static readonly PrerequisiteExpressionValidator Sut = new();
    private static readonly XmlTagDefinition Tag =
        TagOf.Make("Required_Special_Structures", XmlValueType.GameObjectTypeReferenceList, TagSemanticType.PrerequisiteExpression);

    [Theory]
    [InlineData("U_Ground_Barracks")]
    [InlineData("StructA | StructB")]
    [InlineData("StructA, StructB")]
    [InlineData("StructA StructB")]
    [InlineData("StructA | StructB, StructC | StructD")]
    [InlineData("StructA | StructB StructC | StructD")]
    [InlineData("A | B, C")]
    public void Valid_expressions_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("|StructA")]
    [InlineData("StructA|")]
    [InlineData("StructA | | StructB")]
    [InlineData("StructA,,StructB")]
    public void Invalid_expressions_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── FloatVector3Validator ─────────────────────────────────────────────────────

public sealed class FloatVector3ValidatorTests
{
    private static readonly FloatVector3Validator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Name_Adjust", XmlValueType.FloatVector3);

    [Theory]
    [InlineData("-50.0, -150.0, 10.0")]
    [InlineData("100 0 0")]
    [InlineData("0,1,0")]
    [InlineData("1.2, 1.2, 1.2")]
    [InlineData("1.0f, 2.0f, 3.0f")]
    [InlineData("0 0 0")]
    public void Valid_float3_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("1 2")]
    [InlineData("a b c")]
    [InlineData("")]
    [InlineData("1 2 3 4")]
    public void Invalid_float3_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── FloatVector4Validator ─────────────────────────────────────────────────────

public sealed class FloatVector4ValidatorTests
{
    private static readonly FloatVector4Validator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("Shield_Normal_Color", XmlValueType.FloatVector4);

    [Theory]
    [InlineData("0.0, 1.0, 1.0, 0.0")]
    [InlineData("0.2 0.7 0.8 1.0")]
    [InlineData("0.2, 0.7, 0.8, 1.0")]
    [InlineData("1.0f, 0.5f, 0.0f, 1.0f")]
    public void Valid_float4_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("1 2 3")]
    [InlineData("a b c d")]
    [InlineData("")]
    [InlineData("1 2 3 4 5")]
    public void Invalid_float4_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}

// ── RgbaValidator ─────────────────────────────────────────────────────────────

public sealed class RgbaValidatorTests
{
    private static readonly RgbaValidator Sut = new();
    private static readonly XmlTagDefinition Tag = TagOf.Make("GUI_Cycle_Color", XmlValueType.RGBA);

    [Theory]
    [InlineData("255 64 64 255")]
    [InlineData("239, 9, 9, 255")]
    [InlineData("0 0 0")]
    [InlineData("0,255,0,255")]
    [InlineData("128,0,0,128")]
    [InlineData("255 255 255 180")]
    public void Valid_rgba_values_pass(string value)
    {
        Assert.True(Sut.Validate(value, Tag).IsValid);
    }

    [Theory]
    [InlineData("256 0 0 255")]
    [InlineData("-1 0 0 255")]
    [InlineData("abc")]
    [InlineData("1 2")]
    [InlineData("")]
    [InlineData("1 2 3 4 5")]
    public void Invalid_rgba_values_fail(string value)
    {
        Assert.False(Sut.Validate(value, Tag).IsValid);
    }
}