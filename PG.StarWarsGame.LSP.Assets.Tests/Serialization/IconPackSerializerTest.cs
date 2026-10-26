// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;
using PG.StarWarsGame.LSP.Assets.Serialization;

namespace PG.StarWarsGame.LSP.Assets.Tests.Serialization;

public sealed class IconPackSerializerTest
{
    private static readonly Dictionary<string, byte[]> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["I_BUTTON_LUKE.TGA"] = [1, 2, 3],
        ["I_SA_BERSERKER.TGA"] = [4, 5]
    };

    private static readonly DateTimeOffset BuiltAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    [Fact]
    public void RoundTrip_PreservesIconsAndProvenance()
    {
        var bytes = IconPackSerializer.Serialize(Icons, "abc123", BuiltAt);

        var pack = IconPackSerializer.Deserialize(bytes);

        Assert.NotNull(pack);
        Assert.Equal(2, pack.Icons.Count);
        Assert.Equal([1, 2, 3], pack.Icons["I_BUTTON_LUKE.TGA"]);
        Assert.Equal("abc123", pack.SourceManifestHash);
        Assert.Equal(BuiltAt, pack.BuiltAt);
    }

    [Fact]
    public void RoundTrip_LookupIsCaseInsensitive()
    {
        var pack = IconPackSerializer.Deserialize(IconPackSerializer.Serialize(Icons, "h", BuiltAt));

        Assert.True(pack!.Icons.ContainsKey("i_button_luke.tga"));
    }

    [Fact]
    public void Serialize_EmptyPack_RoundTrips()
    {
        var pack = IconPackSerializer.Deserialize(
            IconPackSerializer.Serialize(new Dictionary<string, byte[]>(), "h", BuiltAt));

        Assert.NotNull(pack);
        Assert.Empty(pack.Icons);
    }

    // Matches BaselineSerializer's contract: a pack we cannot interpret degrades to "no icons"
    // rather than throwing, so a bad sidecar never takes the server down with it.
    [Fact]
    public void Deserialize_VersionMismatch_ReturnsNull()
    {
        var dto = new SerializedIconPack
        {
            Icons = [new SerializedIcon { Name = "I_X.TGA", Png = [9] }],
            SchemaVersion = SerializedIconPack.CurrentSchemaVersion + 1,
            SourceManifestHash = "h"
        };

        Assert.Null(IconPackSerializer.Deserialize(MessagePackSerializer.Serialize(dto, cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void Deserialize_Garbage_ReturnsNull()
    {
        Assert.Null(IconPackSerializer.Deserialize([0xFF, 0x00, 0x13, 0x37]));
    }

    // Written in ordinal order, so two builds of the same icons are one file.
    [Fact]
    public void Serialize_WritesTheIconsInOrdinalOrder()
    {
        string[] names = ["I_ZETA.TGA", "I_ALPHA.TGA", "I_MIKE.TGA", "I_BRAVO.TGA", "I_YANKEE.TGA", "I_CHARLIE.TGA"];
        var icons = names.ToDictionary(n => n, _ => new byte[] { 1 });

        var dto = MessagePackSerializer.Deserialize<SerializedIconPack>(IconPackSerializer.Serialize(icons, "h", BuiltAt), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(names.Order(StringComparer.Ordinal), dto.Icons.Select(i => i.Name));
    }

    // A rebuild that changes nothing changes no byte, build time included.
    [Fact]
    public void SerializeOver_TheSameIcons_KeepsThePreviousFile()
    {
        var previous = IconPackSerializer.Serialize(Icons, "abc123", BuiltAt);

        Assert.Equal(previous, IconPackSerializer.SerializeOver(Icons, "abc123", BuiltAt.AddDays(1), previous));
    }

    [Fact]
    public void SerializeOver_ChangedIcons_IsANewBuild()
    {
        var previous = IconPackSerializer.Serialize(Icons, "abc123", BuiltAt);
        var changed = new Dictionary<string, byte[]>(Icons) { ["I_NEW.TGA"] = [7] };

        var pack = IconPackSerializer.Deserialize(
            IconPackSerializer.SerializeOver(changed, "abc123", BuiltAt.AddDays(1), previous));

        Assert.Equal(BuiltAt.AddDays(1), pack!.BuiltAt);
        Assert.Equal(3, pack.Icons.Count);
    }

    // The sidecar sits beside the baseline, following the same convention as .manifest.json.
    [Theory]
    [InlineData("baseline-foc", "baseline-foc.icons")]
    [InlineData(@"C:\cache\baseline-eaw", @"C:\cache\baseline-eaw.icons")]
    public void SidecarPathFor_AppendsIconsSuffix(string baseline, string expected)
    {
        Assert.Equal(expected, IconPackSerializer.SidecarPathFor(baseline));
    }
}