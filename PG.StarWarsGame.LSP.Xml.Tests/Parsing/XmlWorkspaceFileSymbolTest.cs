// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Parsing;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Parsing;

/// <summary>
///     A story plot-manifest / thread document is indexed as a navigable workspace-file symbol
///     keyed by its xml-relative path, so a <c>workspaceFile</c> reference (a campaign
///     <c>*_Story_Name</c> or a manifest <c>Active_Plot</c>) resolves to it for go-to / rename.
/// </summary>
public sealed class XmlWorkspaceFileSymbolTest
{
    private const string XmlDir = "file:///ws/data/xml";

    private static async Task<DocumentIndex> ParseAsync(string uri, string[] types, bool withContext = true)
    {
        var fileHelper = new FileHelper(new MockFileSystem());
        var registry = new FileTypeRegistry();
        if (types.Length > 0)
            registry.RegisterFile(fileHelper.NormalizeUri(uri), types.ToImmutableArray());

        IEaWXmlContext? ctx = null;
        if (withContext)
        {
            var context = new EaWXmlContext(fileHelper);
            context.SetDirectories([XmlDir]);
            ctx = context;
        }

        var parser = new XmlGameDocumentParser(fileHelper, new EmptySchemaProvider(), registry,
            NullLogger<XmlGameDocumentParser>.Instance, eaWXmlContext: ctx);
        return await parser.ParseAsync(uri, "<Story_Mode_Plots/>", 1, CancellationToken.None);
    }

    [Fact]
    public async Task PlotManifestDoc_EmitsWorkspaceFileSymbol_AtFileTop()
    {
        var index = await ParseAsync(XmlDir + "/story_plots_rebel.xml", ["StoryPlotManifest"]);

        var symbol = Assert.Single(index.Symbols, s => s.Kind == GameSymbolKind.WorkspaceFile);
        Assert.Equal("storyplotmanifest:story_plots_rebel.xml", symbol.Id);
        Assert.Equal("StoryPlotManifest", symbol.TypeName);
        var origin = Assert.IsType<FileOrigin>(symbol.Origin);
        Assert.Equal(0, origin.Line);
        Assert.Equal(0, origin.Column);
    }

    [Fact]
    public async Task ThreadDocInSubdir_KeyedByRelativePath()
    {
        var index = await ParseAsync(XmlDir + "/conquests/story_rebel_act_i.xml", ["StoryParser"]);

        var symbol = Assert.Single(index.Symbols, s => s.Kind == GameSymbolKind.WorkspaceFile);
        Assert.Equal("storyparser:conquests/story_rebel_act_i.xml", symbol.Id);
    }

    [Fact]
    public async Task WithoutXmlContext_NoWorkspaceFileSymbol()
    {
        var index = await ParseAsync(XmlDir + "/story_plots_rebel.xml", ["StoryPlotManifest"], withContext: false);

        Assert.DoesNotContain(index.Symbols, s => s.Kind == GameSymbolKind.WorkspaceFile);
    }

    [Fact]
    public async Task UntypedDoc_NoWorkspaceFileSymbol()
    {
        var index = await ParseAsync(XmlDir + "/random.xml", []);

        Assert.DoesNotContain(index.Symbols, s => s.Kind == GameSymbolKind.WorkspaceFile);
    }
}
