// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Tests.Fakes;

internal sealed class AllowAllEaWContext : IEaWXmlContext
{
    public bool IsEaWXmlFile(string fileUri) => true;
}

internal sealed class DenyAllEaWContext : IEaWXmlContext
{
    public bool IsEaWXmlFile(string fileUri) => false;
}
