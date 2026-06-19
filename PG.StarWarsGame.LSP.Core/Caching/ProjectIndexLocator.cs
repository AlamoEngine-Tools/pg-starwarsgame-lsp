// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

public static class ProjectIndexLocator
{
    public static string GetAetswgDirectory(string pgprojPath)
    {
        var dir = GetDirectory(pgprojPath);
        return dir + "/.aetswg";
    }

    public static string GetIndexFilePath(string pgprojPath)
    {
        var stem = GetStem(pgprojPath);
        return GetAetswgDirectory(pgprojPath) + "/indices/" + stem + ".msgpack";
    }

    private static string GetDirectory(string path)
    {
        var normalized = path.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? "." : normalized[..idx];
    }

    private static string GetStem(string path)
    {
        var normalized = path.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        var filename = idx < 0 ? normalized : normalized[(idx + 1)..];
        var dotIdx = filename.LastIndexOf('.');
        return dotIdx < 0 ? filename : filename[..dotIdx];
    }
}