// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     Turning a reticle's NAME into the image the client draws.
/// </summary>
/// <remarks>
///     <para>
///         Measured against a live server 2026-08-23: chunk R1 shipped the name map correctly and
///         then resolved zero images, every time, on a workspace that has all fifteen files on disk.
///         The reason is that it asked the ICON catalog, which scans a project's own
///         <c>SourceRoots</c> - empty unless a <c>pgproj</c> declares them - and is the path for art
///         a modder is drawing, not for the game's own shipped textures.
///     </para>
///     <para>
///         A reticle is a TEXTURE. It resolves the way every other texture does, through the game
///         asset resolver's tier chain, out of <c>Data/Art/Textures/</c> - which is what
///         <c>GetModelTextureHandler</c> has always done.
///     </para>
/// </remarks>
public sealed class PreviewReticleIconTest
{
    /// <summary>The shipped reticle format, built here so the test carries no binary fixture.</summary>
    /// <remarks>
    ///     Verified on two real files: <c>DDS </c>, 64x64, one mip, uncompressed 32-bit,
    ///     <c>ALPHAPIXELS|RGB</c>, masks R <c>00ff0000</c> / G <c>0000ff00</c> / B <c>000000ff</c> /
    ///     A <c>ff000000</c> - plain BGRA - and 16512 bytes, which is 128 + 64*64*4.
    /// </remarks>
    private static byte[] ReticleDds(int size = 64)
    {
        var bytes = new byte[128 + size * size * 4];
        var header = bytes.AsSpan();

        "DDS "u8.CopyTo(header);
        Write(header, 4, 124); // header size
        Write(header, 8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000); // caps|height|width|pixelformat|mipmapcount
        Write(header, 12, size); // height
        Write(header, 16, size); // width
        Write(header, 20, size * 4); // pitch
        Write(header, 28, 1); // mip count
        Write(header, 76, 32); // pixel format size
        Write(header, 80, 0x1 | 0x40); // ALPHAPIXELS | RGB
        Write(header, 88, 32); // bits per pixel
        Write(header, 92, 0x00ff0000); // red mask
        Write(header, 96, 0x0000ff00); // green mask
        Write(header, 100, 0x000000ff); // blue mask
        Write(header, 104, unchecked((int)0xff000000)); // alpha mask
        Write(header, 108, 0x1000); // caps: texture

        // A recognisable pixel so a decode that silently produced an empty surface is still caught.
        for (var i = 128; i < bytes.Length; i += 4)
        {
            bytes[i] = 0x20; // B
            bytes[i + 1] = 0x40; // G
            bytes[i + 2] = 0x80; // R
            bytes[i + 3] = 0xff; // A
        }

        return bytes;

        static void Write(Span<byte> target, int offset, int value)
        {
            BitConverter.TryWriteBytes(target[offset..], value);
        }
    }

    /// <summary>An uncompressed 32-bit BGRA TGA, for the mod-replaced case.</summary>
    private static byte[] ReticleTga(int size = 64)
    {
        var bytes = new byte[18 + size * size * 4];

        bytes[2] = 2; // uncompressed true-colour
        BitConverter.TryWriteBytes(bytes.AsSpan(12), (short)size); // width
        BitConverter.TryWriteBytes(bytes.AsSpan(14), (short)size); // height
        bytes[16] = 32; // bits per pixel
        bytes[17] = 8; // eight alpha bits

        for (var i = 18; i < bytes.Length; i += 4)
        {
            bytes[i] = 0x20;
            bytes[i + 1] = 0x40;
            bytes[i + 2] = 0x80;
            bytes[i + 3] = 0xff;
        }

        return bytes;
    }

    [Fact]
    public void AReticleName_ResolvesOutOfTheGameTextureDirectory()
    {
        var assets = new FakeTextureAssets
        {
            ["Data/Art/Textures/I_Hard_Point_Reticle_Weapons.dds"] = ReticleDds()
        };

        var icons = PreviewReticleIcons.Resolve(assets, ["I_Hard_Point_Reticle_Weapons"]);

        Assert.True(icons.TryGetValue("I_Hard_Point_Reticle_Weapons", out var uri));
        Assert.StartsWith("data:image/png;base64,", uri, StringComparison.Ordinal);

        // A real PNG, not an empty string dressed up as one.
        var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        Assert.True(png.Length > 100);
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], png[..4]);
    }

    [Fact]
    public void TheNameIsWrittenWithoutAnExtension_SoEveryTextureFormatIsTried()
    {
        // GameConstants names `I_Hard_Point_Reticle_Weapons` with no suffix at all. The shipped
        // files are .dds; a mod replacing one may well use .tga, exactly as it may for any other
        // texture the engine loads by bare name.
        var assets = new FakeTextureAssets
        {
            ["Data/Art/Textures/I_Hard_Point_Reticle_Engines.tga"] = ReticleTga()
        };

        Assert.True(PreviewReticleIcons.Resolve(assets, ["I_Hard_Point_Reticle_Engines"])
            .ContainsKey("I_Hard_Point_Reticle_Engines"));
    }

    [Fact]
    public void AnUnresolvableName_IsLeftOutRatherThanFailingTheScene()
    {
        // A scene reads perfectly well without reticles, and a workspace with no game directory
        // configured has no textures at all. Dropping the entry is the quiet omission the DTO
        // documents; throwing would cost the reader the whole model.
        var icons = PreviewReticleIcons.Resolve(new FakeTextureAssets(), ["I_Nothing_Here"]);

        Assert.Empty(icons);
    }

    [Fact]
    public void OneIconResolvedOnce_HoweverManyTypesNameIt()
    {
        // Thirteen hardpoint types share five artwork families, so the same name arrives repeatedly.
        var assets = new FakeTextureAssets
        {
            ["Data/Art/Textures/I_Hard_Point_Reticle_Weapons.dds"] = ReticleDds()
        };

        var icons = PreviewReticleIcons.Resolve(assets,
            ["I_Hard_Point_Reticle_Weapons", "i_hard_point_reticle_weapons"]);

        Assert.Single(icons);
        Assert.Equal(1, assets.Reads);
    }

    private sealed class FakeTextureAssets : Dictionary<string, byte[]>, IGameAssetResolver
    {
        public FakeTextureAssets() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public int Reads { get; private set; }

        public GameAssetTiers Tiers => new(0, false, false, 0);

        public GameAssetLocation? Locate(string gameRelativePath)
        {
            return ContainsKey(gameRelativePath)
                ? new GameAssetLocation(gameRelativePath, gameRelativePath, GameAssetTier.Workspace)
                : null;
        }

        public byte[]? Read(string gameRelativePath)
        {
            if (!TryGetValue(gameRelativePath, out var bytes))
                return null;

            Reads++;
            return bytes;
        }
    }
}
