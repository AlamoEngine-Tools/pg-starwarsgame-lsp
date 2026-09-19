// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     The command bar's per-faction button art is a LIST of textures, one per faction.
/// </summary>
/// <remarks>
///     <para>
///         Found while fixing #124. Asset values used to be split on space whatever the tag's type,
///         so a tag holding two filenames and a tag holding one name that contains a space behaved
///         identically - and both happened to work. Once the split follows the declared type, a
///         mis-typed tag starts reporting its second filename as missing.
///     </para>
///     <para>
///         Measured across both corpora: <c>Disabled_Texture_Name</c> ships two space-separated
///         filenames 52 times (rebel and empire art), exactly like its siblings
///         <c>Blank_Texture_Name</c> (124), <c>Selected_Texture_Name</c> (212) and
///         <c>Mouse_Over_Texture_Name</c> (224), all of which were already lists. It was the only one
///         declared as a single name. <c>Flash_Texture_Name</c> ships none and stays single.
///     </para>
/// </remarks>
public sealed class EawSchemaCommandBarTextureTagTest
{
    [Theory]
    [InlineData("Blank_Texture_Name")]
    [InlineData("Selected_Texture_Name")]
    [InlineData("Mouse_Over_Texture_Name")]
    [InlineData("Disabled_Texture_Name")]
    public void PerFactionButtonArt_IsAList(string tagName)
    {
        Assert.Equal(XmlValueType.NameReferenceList, Tag(tagName).ValueType);
    }

    [Fact]
    public void PerFactionButtonArt_StillResolvesAgainstTextureFiles()
    {
        Assert.Equal(ReferenceKind.TextureFile, Tag("Disabled_Texture_Name").ReferenceKind);
    }

    private static RawTagDefinition Tag(string name)
    {
        var tags = YamlSchemaParser.ParseTagFile(
            File.ReadAllText(Find("tags", "CommandBarComponent.yaml")));
        var tag = tags.FirstOrDefault(t => string.Equals(t.Tag, name, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(tag);
        return tag!;
    }

    private static string Find(string folder, string file)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(EawSchemaCommandBarTextureTagTest).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw", folder, file);
                if (File.Exists(candidate)) return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate schema/eaw/{folder}/{file}");
    }
}