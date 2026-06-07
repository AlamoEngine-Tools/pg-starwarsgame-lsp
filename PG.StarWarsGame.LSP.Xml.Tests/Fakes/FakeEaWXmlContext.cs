// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Tests.Fakes;

internal sealed class AllowAllEaWContext : IEaWXmlContext
{
    public bool IsEaWXmlFile(string fileUri)
    {
        return true;
    }

    public bool HasDirectories => true;

    public void AddDirectory(string absolutePath)
    {
    }

    public void SetDirectories(IEnumerable<string> absolutePaths)
    {
    }
}

internal sealed class DenyAllEaWContext : IEaWXmlContext
{
    public bool IsEaWXmlFile(string fileUri)
    {
        return false;
    }

    public bool HasDirectories => false;

    public void AddDirectory(string absolutePath)
    {
    }

    public void SetDirectories(IEnumerable<string> absolutePaths)
    {
    }
}