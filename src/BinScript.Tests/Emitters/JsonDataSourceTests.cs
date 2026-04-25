namespace BinScript.Tests.Emitters;

using System.Text.Json;
using BinScript.Emitters.Json;

public class JsonDataSourceTests
{
    // ─── 1. EnterRoot / ExitRoot ─────────────────────────────────────

    [Fact]
    public void EnterRoot_ExitRoot_BasicNavigation()
    {
        using var ds = new JsonDataSource("""{"a":1}""");
        ds.EnterRoot("Test");
        Assert.Equal(1, ds.ReadInt("a"));
        ds.ExitRoot();
    }

    // ─── 2. ReadInt ──────────────────────────────────────────────────

    [Fact]
    public void ReadInt_ReadsIntegerFromField()
    {
        using var ds = new JsonDataSource("""{"x":42,"y":-7}""");
        ds.EnterRoot("Test");
        Assert.Equal(42, ds.ReadInt("x"));
        Assert.Equal(-7, ds.ReadInt("y"));
        ds.ExitRoot();
    }

    [Fact]
    public void ReadInt_ReadsFromCurrentWhenNumber()
    {
        using var ds = new JsonDataSource("""{"items":[10,20,30]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("items");
        ds.EnterArrayElement(0);
        // When current is a number, ReadInt reads it directly
        Assert.Equal(10, ds.ReadInt(""));
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 3. ReadUInt ─────────────────────────────────────────────────

    [Fact]
    public void ReadUInt_ReadsUnsignedIntegerFromField()
    {
        using var ds = new JsonDataSource("""{"size":4294967295}""");
        ds.EnterRoot("Test");
        Assert.Equal(4294967295UL, ds.ReadUInt("size"));
        ds.ExitRoot();
    }

    [Fact]
    public void ReadUInt_ReadsFromCurrentWhenNumber()
    {
        using var ds = new JsonDataSource("""{"items":[100]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("items");
        ds.EnterArrayElement(0);
        Assert.Equal(100UL, ds.ReadUInt(""));
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 4. ReadFloat ────────────────────────────────────────────────

    [Fact]
    public void ReadFloat_ReadsFloatFromField()
    {
        using var ds = new JsonDataSource("""{"pi":3.14}""");
        ds.EnterRoot("Test");
        Assert.Equal(3.14, ds.ReadFloat("pi"), 5);
        ds.ExitRoot();
    }

    [Fact]
    public void ReadFloat_ReadsFromCurrentWhenNumber()
    {
        using var ds = new JsonDataSource("""{"vals":[1.5]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("vals");
        ds.EnterArrayElement(0);
        Assert.Equal(1.5, ds.ReadFloat(""), 5);
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 5. ReadString ───────────────────────────────────────────────

    [Fact]
    public void ReadString_ReadsStringFromField()
    {
        using var ds = new JsonDataSource("""{"name":"Hello"}""");
        ds.EnterRoot("Test");
        Assert.Equal("Hello", ds.ReadString("name"));
        ds.ExitRoot();
    }

    [Fact]
    public void ReadString_ReadsFromCurrentWhenString()
    {
        using var ds = new JsonDataSource("""{"tags":["alpha","beta"]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("tags");
        ds.EnterArrayElement(1);
        Assert.Equal("beta", ds.ReadString(""));
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 6. ReadBool ─────────────────────────────────────────────────

    [Fact]
    public void ReadBool_ReadsBooleanFromField()
    {
        using var ds = new JsonDataSource("""{"on":true,"off":false}""");
        ds.EnterRoot("Test");
        Assert.True(ds.ReadBool("on"));
        Assert.False(ds.ReadBool("off"));
        ds.ExitRoot();
    }

    [Fact]
    public void ReadBool_ReadsFromCurrentWhenBoolean()
    {
        using var ds = new JsonDataSource("""{"flags":[true,false]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("flags");
        ds.EnterArrayElement(0);
        Assert.True(ds.ReadBool(""));
        ds.ExitArrayElement();
        ds.EnterArrayElement(1);
        Assert.False(ds.ReadBool(""));
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 7. EnterStruct / ExitStruct ─────────────────────────────────

    [Fact]
    public void EnterStruct_NavigatesIntoNestedObject()
    {
        using var ds = new JsonDataSource("""{"outer":{"inner":99}}""");
        ds.EnterRoot("Test");
        ds.EnterStruct("outer", "Outer");
        Assert.Equal(99, ds.ReadInt("inner"));
        ds.ExitStruct();
        ds.ExitRoot();
    }

    [Fact]
    public void EnterStruct_WhenAlreadyOnObject_PushesCurrent()
    {
        // When field doesn't exist on current, push current (array-element case)
        var json = """[{"x":1},{"x":2}]""";
        using var doc = JsonDocument.Parse(json);
        using var ds = new JsonDataSource(doc);

        // Simulate being positioned on an array element that IS the struct
        ds.EnterRoot("Test"); // pushes root (the array)
        ds.EnterArrayElement(0);
        ds.EnterStruct("", "Item");
        Assert.Equal(1, ds.ReadInt("x"));
        ds.ExitStruct();
        ds.ExitArrayElement();
        ds.ExitRoot();
    }

    // ─── 8. EnterArray / GetArrayLength / EnterArrayElement ──────────

    [Fact]
    public void Array_Navigation_LengthAndElements()
    {
        using var ds = new JsonDataSource("""{"items":[10,20,30]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("items");

        Assert.Equal(3, ds.GetArrayLength());

        ds.EnterArrayElement(0);
        Assert.Equal(10, ds.ReadInt(""));
        ds.ExitArrayElement();

        ds.EnterArrayElement(1);
        Assert.Equal(20, ds.ReadInt(""));
        ds.ExitArrayElement();

        ds.EnterArrayElement(2);
        Assert.Equal(30, ds.ReadInt(""));
        ds.ExitArrayElement();

        ds.ExitArray();
        ds.ExitRoot();
    }

    [Fact]
    public void Array_OfStructs_NavigatesCorrectly()
    {
        using var ds = new JsonDataSource("""{"points":[{"x":1,"y":2},{"x":3,"y":4}]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("points");

        Assert.Equal(2, ds.GetArrayLength());

        ds.EnterArrayElement(0);
        ds.EnterStruct("", "Point");
        Assert.Equal(1, ds.ReadInt("x"));
        Assert.Equal(2, ds.ReadInt("y"));
        ds.ExitStruct();
        ds.ExitArrayElement();

        ds.EnterArrayElement(1);
        ds.EnterStruct("", "Point");
        Assert.Equal(3, ds.ReadInt("x"));
        Assert.Equal(4, ds.ReadInt("y"));
        ds.ExitStruct();
        ds.ExitArrayElement();

        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 9. HasField ─────────────────────────────────────────────────

    [Fact]
    public void HasField_ReturnsTrueForExistingField()
    {
        using var ds = new JsonDataSource("""{"present":1}""");
        ds.EnterRoot("Test");
        Assert.True(ds.HasField("present"));
        ds.ExitRoot();
    }

    [Fact]
    public void HasField_ReturnsFalseForMissingField()
    {
        using var ds = new JsonDataSource("""{"present":1}""");
        ds.EnterRoot("Test");
        Assert.False(ds.HasField("absent"));
        ds.ExitRoot();
    }

    // ─── 10. ReadBytes (base64 decoding) ─────────────────────────────

    [Fact]
    public void ReadBytes_DecodesBase64()
    {
        // Base64 of [0xDE, 0xAD, 0xBE, 0xEF]
        var b64 = Convert.ToBase64String(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        using var ds = new JsonDataSource($$"""{"data":"{{b64}}"}""");
        ds.EnterRoot("Test");
        var bytes = ds.ReadBytes("data");
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, bytes);
        ds.ExitRoot();
    }

    [Fact]
    public void ReadBytes_FromCurrentWhenString()
    {
        var b64 = Convert.ToBase64String(new byte[] { 0x01, 0x02 });
        using var ds = new JsonDataSource($$"""{"items":["{{b64}}"]}""");
        ds.EnterRoot("Test");
        ds.EnterArray("items");
        ds.EnterArrayElement(0);
        var bytes = ds.ReadBytes("");
        Assert.Equal(new byte[] { 0x01, 0x02 }, bytes);
        ds.ExitArrayElement();
        ds.ExitArray();
        ds.ExitRoot();
    }

    // ─── 11. EnterBitsStruct / ReadBit / ExitBitsStruct ──────────────

    [Fact]
    public void BitsStruct_ReadBit_Navigation()
    {
        using var ds = new JsonDataSource("""{"flags":{"read":true,"write":false,"exec":true}}""");
        ds.EnterRoot("Test");
        ds.EnterBitsStruct("flags");
        Assert.True(ds.ReadBit("read"));
        Assert.False(ds.ReadBit("write"));
        Assert.True(ds.ReadBit("exec"));
        ds.ExitBitsStruct();
        ds.ExitRoot();
    }

    [Fact]
    public void BitsStruct_ReadBits_ReadsNumericValue()
    {
        using var ds = new JsonDataSource("""{"flags":{"nibble":15}}""");
        ds.EnterRoot("Test");
        ds.EnterBitsStruct("flags");
        Assert.Equal(15UL, ds.ReadBits("nibble", 4));
        ds.ExitBitsStruct();
        ds.ExitRoot();
    }

    // ─── 12. Variant navigation ──────────────────────────────────────

    [Fact]
    public void Variant_ReadVariantType_And_Navigate()
    {
        using var ds = new JsonDataSource("""{"payload":{"_variant":"TypeA","x":42}}""");
        ds.EnterRoot("Test");

        var variantType = ds.ReadVariantType("payload");
        Assert.Equal("TypeA", variantType);

        ds.EnterVariant("payload", "TypeA");
        Assert.Equal(42, ds.ReadInt("x"));
        ds.ExitVariant();

        ds.ExitRoot();
    }

    // ─── 13. Constructor from JsonDocument ───────────────────────────

    [Fact]
    public void Constructor_FromJsonDocument_DoesNotOwnDoc()
    {
        var doc = JsonDocument.Parse("""{"a":1}""");
        using var ds = new JsonDataSource(doc);
        ds.EnterRoot("Test");
        Assert.Equal(1, ds.ReadInt("a"));
        ds.ExitRoot();
        ds.Dispose();

        // Doc should still be usable since JsonDataSource didn't own it
        Assert.Equal(1, doc.RootElement.GetProperty("a").GetInt32());
        doc.Dispose();
    }

    [Fact]
    public void Constructor_FromString_OwnsDoc()
    {
        // Just verify it doesn't throw — the doc is internally managed
        using var ds = new JsonDataSource("""{"b":2}""");
        ds.EnterRoot("Test");
        Assert.Equal(2, ds.ReadInt("b"));
        ds.ExitRoot();
    }

    // ─── 14. Deep nesting ────────────────────────────────────────────

    [Fact]
    public void DeepNesting_StructInStructInStruct()
    {
        using var ds = new JsonDataSource("""{"level1":{"level2":{"value":99}}}""");
        ds.EnterRoot("Test");
        ds.EnterStruct("level1", "L1");
        ds.EnterStruct("level2", "L2");
        Assert.Equal(99, ds.ReadInt("value"));
        ds.ExitStruct();
        ds.ExitStruct();
        ds.ExitRoot();
    }
}
