// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

/// <summary>
///     Guards the fix for the silently-empty model-bone catalog: the vendored ALO loader derives the
///     model's directory from its stream's path and throws "Unable to get file path from Stream" on a
///     <see cref="MemoryStream" />, so every model failed to load and the whole catalog came out empty.
///     The skeleton reader must therefore be handed a path-bearing <see cref="FileSystemStream" />.
/// </summary>
public sealed class BoneNameExtractorStreamTest
{
    [Fact]
    public void CreateBoneLoader_HandsSkeletonReaderAFileBackedStreamWithThePath()
    {
        const string path = @"C:\models\rv_tank.alo";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        });

        Stream? captured = null;
        var loader = BoneNameExtractor.CreateBoneLoader(fs, stream =>
        {
            captured = stream;
            return ["Turret_00"];
        });

        var bones = loader(path);

        Assert.NotNull(captured);
        // A MemoryStream would reintroduce the bug; a FileSystemStream carries the model's path.
        var fileStream = Assert.IsAssignableFrom<FileSystemStream>(captured);
        Assert.Equal(path, fileStream.Name);
        Assert.Contains("Turret_00", bones!);
    }

    [Fact]
    public void CreateBoneLoader_UnionsSkeletonBonesWithMeshNamesFromBytes()
    {
        // Mesh names come from the raw bytes via the shim; skeleton bones from the (stubbed) loader.
        // Here the bytes are not a real ALO, so the shim contributes nothing - the skeleton set stands.
        const string path = @"C:\models\x.alo";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })
        });

        var loader = BoneNameExtractor.CreateBoneLoader(fs, _ => ["root", "turret"]);

        var bones = loader(path);

        Assert.Equal(["root", "turret"], bones);
    }
}
