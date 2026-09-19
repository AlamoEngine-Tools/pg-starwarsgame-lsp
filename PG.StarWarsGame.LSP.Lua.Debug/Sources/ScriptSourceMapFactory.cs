// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <inheritdoc />
public sealed class ScriptSourceMapFactory : IScriptSourceMapFactory
{
    private readonly IFileHelper _fileHelper;

    public ScriptSourceMapFactory(IFileHelper fileHelper)
    {
        _fileHelper = fileHelper;
    }

    public IScriptSourceMap Create(IReadOnlyList<string> sourceRoots)
    {
        return new ScriptSourceMap(_fileHelper, sourceRoots);
    }
}