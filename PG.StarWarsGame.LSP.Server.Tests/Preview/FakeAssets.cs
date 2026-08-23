// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>Resolves only the asset names it was handed, from the model directory.</summary>
internal sealed class FakeAssets(params string[] resolvable) : IGameAssetResolver
{
    private readonly HashSet<string> _resolvable =
        new(resolvable.Select(r => "Data/Art/Models/" + r), StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, byte[]> _content = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Gives one asset a real root chunk, so classification has something to read.
    /// </summary>
    public FakeAssets WithRootChunk(string name, uint chunkType)
    {
        _content["Data/Art/Models/" + name] = BitConverter.GetBytes(chunkType);
        return this;
    }

    public GameAssetTiers Tiers => new(1, false, false, 0);

    public GameAssetLocation? Locate(string gameRelativePath)
    {
        return _resolvable.Contains(gameRelativePath)
            ? new GameAssetLocation(gameRelativePath, gameRelativePath, GameAssetTier.Workspace)
            : null;
    }

    public byte[]? Read(string gameRelativePath)
    {
        if (Locate(gameRelativePath) is null)
            return null;

        return _content.GetValueOrDefault(gameRelativePath, EmptyModel);
    }

    /// <summary>
    ///     The smallest thing <see cref="AloModelReader" /> accepts: a skeleton declaring no bones.
    /// </summary>
    /// <remarks>
    ///     Every test that makes an asset resolvable means "a normal model is here", and the builder
    ///     both classifies by root chunk and parses for proxies. A four-byte stub passed the first
    ///     and failed the second, which showed up as a spurious warning in an unrelated test.
    /// </remarks>
    private static byte[] EmptyModel
    {
        get
        {
            // Bone count: a fixed 128-byte record whose first uint32 is the count.
            var boneCount = new byte[8 + BoneCountChunkSize];
            BitConverter.GetBytes(0x201u).CopyTo(boneCount, 0);
            BitConverter.GetBytes((uint)BoneCountChunkSize).CopyTo(boneCount, 4);

            var skeleton = new byte[8 + boneCount.Length];
            BitConverter.GetBytes(0x200u).CopyTo(skeleton, 0);
            // High bit marks a container.
            BitConverter.GetBytes((uint)boneCount.Length | 0x80000000u).CopyTo(skeleton, 4);
            boneCount.CopyTo(skeleton, 8);

            return skeleton;
        }
    }

    private const int BoneCountChunkSize = 128;
}
