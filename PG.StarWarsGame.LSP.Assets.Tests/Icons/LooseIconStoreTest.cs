// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Icons;

namespace PG.StarWarsGame.LSP.Assets.Tests.Icons;

/// <summary>
///     The project's raw icon sources, decoded only when something asks for one.
/// </summary>
/// <remarks>
///     Building the catalog used to decode every source image up front. On a mod shipping 1655 loose
///     icons across 21 MB that is a whole workspace's art turned into PNG so that a card can show
///     two of them. The names are what the catalog needs eagerly - they decide which layer answers,
///     and which sources are missing from the mega texture; the pixels are needed one at a time.
/// </remarks>
public sealed class LooseIconStoreTest
{
    private static LooseIcon Icon(string name)
    {
        return new LooseIcon(name, $"C:/mod/art/{name}.tga", ".tga");
    }

    private static Dictionary<string, LooseIcon> Catalog(params string[] names)
    {
        return names.ToDictionary(n => n, Icon, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The point of the whole exercise.</summary>
    [Fact]
    public void Names_AreAvailableWithoutDecodingAnything()
    {
        var decodes = 0;
        var store = new LooseIconStore(Catalog("a", "b", "c"), _ =>
        {
            decodes++;
            return [1];
        });

        Assert.Equal(3, store.Names.Count);
        Assert.Contains("b", store.Names);
        Assert.Equal(0, decodes);
    }

    [Fact]
    public void TryGet_DecodesOnlyTheIconAskedFor()
    {
        var decoded = new List<string>();
        var store = new LooseIconStore(Catalog("a", "b", "c"), icon =>
        {
            decoded.Add(icon.Name);
            return [1];
        });

        Assert.True(store.TryGet("b", out var png));
        Assert.Equal<byte[]>([1], png);
        Assert.Equal(["b"], decoded);
    }

    /// <summary>
    ///     A card draws the same chrome on every object, so the second request must not pay again.
    /// </summary>
    [Fact]
    public void TryGet_DecodesEachIconOnce()
    {
        var decodes = 0;
        var store = new LooseIconStore(Catalog("a"), _ =>
        {
            decodes++;
            return [1];
        });

        Assert.True(store.TryGet("a", out _));
        Assert.True(store.TryGet("a", out _));

        Assert.Equal(1, decodes);
    }

    [Fact]
    public void TryGet_UnknownName_DecodesNothing()
    {
        var decodes = 0;
        var store = new LooseIconStore(Catalog("a"), _ =>
        {
            decodes++;
            return [1];
        });

        Assert.False(store.TryGet("missing", out var png));
        Assert.Null(png);
        Assert.Equal(0, decodes);
    }

    /// <summary>
    ///     A BMP source, or a corrupt file, fails to decode. Retrying it on every card would pay the
    ///     cost repeatedly for an answer that cannot change.
    /// </summary>
    [Fact]
    public void TryGet_AFailedDecode_IsNotRetried()
    {
        var decodes = 0;
        var store = new LooseIconStore(Catalog("broken"), _ =>
        {
            decodes++;
            return null;
        });

        Assert.False(store.TryGet("broken", out _));
        Assert.False(store.TryGet("broken", out _));

        Assert.Equal(1, decodes);
        Assert.Equal(["broken"], store.Undecodable);
    }

    /// <summary>
    ///     Nothing has been asked for yet, so nothing is known to be broken - the list reports what
    ///     was actually attempted rather than implying the rest are fine.
    /// </summary>
    [Fact]
    public void Undecodable_IsEmptyUntilSomethingIsAskedFor()
    {
        var store = new LooseIconStore(Catalog("broken"), _ => null);

        Assert.Empty(store.Undecodable);
    }

    /// <summary>Icon names are matched the way every other layer matches them.</summary>
    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var store = new LooseIconStore(Catalog("I_Button_Dp2"), _ => [7]);

        Assert.True(store.TryGet("i_button_dp2", out var png));
        Assert.Equal<byte[]>([7], png);
    }

    /// <summary>
    ///     The already-decoded shape stays supported: the baseline sidecar and most tests hand over
    ///     bytes they already hold, and wrapping them must not make them lazy.
    /// </summary>
    [Fact]
    public void FromDecoded_ServesBytesDirectly()
    {
        var store = LooseIconStore.FromDecoded(
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["a"] = [9] });

        Assert.Equal(["a"], store.Names);
        Assert.True(store.TryGet("A", out var png));
        Assert.Equal<byte[]>([9], png);
    }
}
