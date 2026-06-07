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

    public bool HasDirectories => !_directories.IsEmpty;

    public bool IsEaWXmlFile(string fileUri)
    {
        var normalized = _fileHelper.NormalizeUri(fileUri);
        if (!normalized.StartsWith("file:///", StringComparison.Ordinal)) return false;

        // TODO: implement proper AI XML parsing — AI files use a different format that
        //       requires a dedicated parser; exclude them until that parser exists.
        if (normalized.Contains("/ai/", StringComparison.Ordinal)) return false;

        return _directories.Any(dir => normalized.StartsWith(dir, StringComparison.Ordinal));
    }

    public void AddDirectory(string absolutePath)
    {
        var uri = absolutePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? _fileHelper.NormalizeUri(absolutePath)
            : _fileHelper.PathToFileUri(absolutePath);
        var prefix = uri.TrimEnd('/') + '/';
        ImmutableInterlocked.Update(ref _directories, d => d.Add(prefix));
    }

    public void SetDirectories(IEnumerable<string> absolutePaths)
    {
        var next = ImmutableHashSet<string>.Empty;
        foreach (var path in absolutePaths)
        {
            var uri = path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? _fileHelper.NormalizeUri(path)
                : _fileHelper.PathToFileUri(path);
            next = next.Add(uri.TrimEnd('/') + '/');
        }

        Volatile.Write(ref _directories, next);
    }
}