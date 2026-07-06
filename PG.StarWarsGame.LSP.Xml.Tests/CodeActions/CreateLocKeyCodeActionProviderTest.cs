// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

public sealed class CreateLocKeyCodeActionProviderTest
{
    private static readonly DocumentUri TestUri = DocumentUri.From("file:///test.xml");
    private static readonly LspRange TestRange = new(new Position(1, 0), new Position(1, 10));

    private static XmlCodeActionContext Ctx(Diagnostic d)
    {
        return new XmlCodeActionContext(TestUri, d);
    }

    private static CreateLocKeyCodeActionProvider Provider(ILspConfigurationProvider? config = null)
    {
        return new CreateLocKeyCodeActionProvider(config ?? new FakeLspConfigurationProvider());
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public void LocalisationFlagOff_ReturnsEmptyDespiteCreateLocKeyData()
    {
        var d = new Diagnostic
        {
            Range = TestRange,
            Data = JToken.FromObject(new { createLocKey = "TEXT_NEW_KEY" })
        };
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });

        var results = Provider(config).Handle(Ctx(d)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DiagnosticWithCreateLocKey_ReturnsCreateAction()
    {
        var d = new Diagnostic
        {
            Range = TestRange,
            Data = JToken.FromObject(new { createLocKey = "TEXT_NEW_KEY" })
        };
        var provider = Provider();

        var results = provider.Handle(Ctx(d)).ToList();

        var action = Assert.Single(results).CodeAction!;
        Assert.Equal("Create localisation key 'TEXT_NEW_KEY'", action.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.NotNull(action.Command);
        Assert.Equal("aet-eaw-edit.lsp.createLocalisationKey", action.Command!.Name);
    }

    [Fact]
    public void DiagnosticWithoutCreateLocKey_ReturnsEmpty()
    {
        var d = new Diagnostic { Range = TestRange, Data = null };
        var provider = Provider();

        var results = provider.Handle(Ctx(d)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void DiagnosticWithUnrelatedData_ReturnsEmpty()
    {
        var d = new Diagnostic
        {
            Range = TestRange,
            Data = JToken.FromObject(new { fix = "something_else" })
        };
        var provider = Provider();

        var results = provider.Handle(Ctx(d)).ToList();

        Assert.Empty(results);
    }
}