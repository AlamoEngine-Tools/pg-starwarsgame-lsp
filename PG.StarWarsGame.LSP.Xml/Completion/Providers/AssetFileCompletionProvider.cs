// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

/// <summary>
///     Completes asset-file references (texture, model, audio, map) from the merged
///     <see cref="Core.Assets.IAssetFileIndex" />, filtered by the extensions allowed for the tag's
///     <see cref="ReferenceKind" /> and prefix-filtered against the token being typed.
/// </summary>
public sealed class AssetFileCompletionProvider : IXmlCompletionProvider
{
    public bool CanHandle(XmlTagDefinition tag)
    {
        return AllowedExtensions(tag.ReferenceKind) is not null;
    }

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        var extensions = AllowedExtensions(tag.ReferenceKind);
        if (extensions is null)
            return [];

        var partial = partialValue.Replace('\\', '/');

        return extensions
            .SelectMany(index.AssetFiles.GetByExtension)
            .Where(path => Matches(path, partial))
            .Select(path =>
            {
                var slash = path.LastIndexOf('/');
                var fileName = slash >= 0 ? path[(slash + 1)..] : path;
                var isPacked = index.AssetFiles.IsPackedAsset(path);
                return new ValueProposal
                {
                    Label = fileName,
                    Detail = path,
                    Description = isPacked ? "packed" : null
                };
            })
            .ToList();
    }

    private static bool Matches(string path, string partial)
    {
        if (partial.Length == 0)
            return true;
        if (path.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            return true;

        var slash = path.LastIndexOf('/');
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;
        return fileName.StartsWith(partial, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? AllowedExtensions(ReferenceKind kind)
    {
        return kind switch
        {
            ReferenceKind.TextureFile => [".tga", ".dds"],
            ReferenceKind.ModelFile => [".alo"],
            ReferenceKind.AudioFile => [".wav", ".mp3"],
            ReferenceKind.MapFile => [".ted"],
            _ => null
        };
    }
}