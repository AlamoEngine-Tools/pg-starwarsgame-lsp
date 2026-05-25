// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server;

public sealed class EaWXmlContext : IEaWXmlContext
{
    private readonly IFileHelper _fileHelper;
    private ImmutableHashSet<string> _directories = ImmutableHashSet<string>.Empty;

    public EaWXmlContext(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    public bool IsEaWXmlFile(string fileUri)
    {
        var path = _fileHelper.FileUriToPath(_fileHelper.NormalizeUri(fileUri));
        if (path is null) return false;
        var lower = path.ToLowerInvariant();

        // TODO: implement proper AI XML parsing — AI files use a different format that
        //       requires a dedicated parser; exclude them until that parser exists.
        var sep = Path.DirectorySeparatorChar;
        if (lower.Contains($"{sep}ai{sep}")) return false;

        return _directories.Any(dir => lower.StartsWith(dir, StringComparison.Ordinal));
    }

    public void AddDirectory(string absolutePath)
    {
        var norm = absolutePath.ToLowerInvariant().TrimEnd(Path.DirectorySeparatorChar, '/')
                   + Path.DirectorySeparatorChar;
        ImmutableInterlocked.Update(ref _directories, d => d.Add(norm));
    }
}
