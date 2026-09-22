// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Locates the checked-out schema repository (<c>schema/eaw</c> next to the solution) for tests
///     that pin the SHIPPED schema files rather than a fixture.
/// </summary>
internal static class EawSchemaRepo
{
    private static readonly Lazy<string> RootPath = new(Find);

    public static string Root => RootPath.Value;

    public static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Every YAML file under the given sub-directory, as repo-relative paths with '/'.</summary>
    public static IReadOnlyList<string> YamlFiles(string subDirectory)
    {
        var dir = Path.Combine(Root, subDirectory);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetRelativePath(Root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string Find()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(EawSchemaRepo).Assembly.Location)!);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
            {
                var candidate = Path.Combine(dir.FullName, "schema", "eaw");
                if (Directory.Exists(candidate)) return candidate;
                break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("schema/eaw not found - is the schema repository checked out?");
    }
}