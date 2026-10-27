// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>
///     Builds an <see cref="XmlDiagnosticsPublisher" /> for a test that is about one part of it.
/// </summary>
/// <remarks>
///     The publisher's dependencies are all required (#305). A test names the ones it is about; the
///     rest are the real producers over the test's own schema and tag source, a shared text source
///     and parse cache over the test's host, and the quietest doubles for everything else: no
///     suppressions, no layers, no story graph problems, no icons, no model textures, no debounce.
/// </remarks>
internal static class XmlPublisherFixture
{
    public static XmlDiagnosticsPublisher Create(
        IDiagnosticsSink sink,
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlDiagnosticsHandlerRegistry handlerRegistry,
        IXmlDocumentFactProducer documentProducer,
        IXmlIndexFactProducer indexProducer,
        IStoryFactProducer storyProducer,
        ILogger<XmlDiagnosticsPublisher> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper,
        ILspConfigurationProvider? configProvider = null,
        IStoryChainProblemStore? storyChainProblems = null,
        IVariantTagSource? variantTagSource = null,
        IMessageCatalog? messages = null)
    {
        var tagSource = variantTagSource ?? new FakeVariantTagSource();
        var textSource = new DocumentTextSource(workspaceHost, fileHelper, NullLogger<DocumentTextSource>.Instance);
        return new XmlDiagnosticsPublisher(
            sink,
            indexService,
            workspaceHost,
            schema,
            handlerRegistry,
            documentProducer,
            indexProducer,
            storyProducer,
            logger,
            fileTypeRegistry,
            fileHelper,
            new XmlVariantFactProducer(schema, tagSource),
            new XmlLayerShadowFactProducer(),
            textSource,
            new XmlParseCache(textSource, 16, NullLogger<XmlParseCache>.Instance),
            configProvider ?? new FakeLspConfigurationProvider(),
            storyChainProblems ?? new StoryChainProblemStore(),
            new FakeStoryGraphDiagnosticsSource(),
            new XmlHardpointFactProducer(schema, tagSource),
            new XmlDamageStageFactProducer(schema, tagSource),
            tagSource,
            new FakeGlobalSuppressionStore(),
            new ProjectLayerMap(fileHelper),
            TestServerOptions.Immediate,
            new FakeIconRepackStatusProvider(),
            new FakeModelTextureIndex(),
            new FakeIconNameIndex(),
            messages ?? TestCatalogues.English);
    }
}