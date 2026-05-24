// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Commands;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml;

public static class XmlLanguageServiceExtensions
{
    public static IServiceCollection AddXmlLanguageServices(this IServiceCollection services)
    {
        services.AddSingleton<XmlDiagnosticsPublisher>();
        services.AddSingleton<IXmlDiagnosticsRevalidator>(sp => sp.GetRequiredService<XmlDiagnosticsPublisher>());
        services.AddSingleton<IXmlFixCache>(sp => sp.GetRequiredService<XmlDiagnosticsPublisher>());
        services.AddSingleton<RevalidateWorkspaceCommandHandler>();
        services.AddSingleton<RevalidateDocumentCommandHandler>();

        // Handler registry
        services.AddSingleton<IXmlDiagnosticsHandlerRegistry, XmlDiagnosticsHandlerRegistry>();

        // XmlTagValueFact handlers (format validators)
        services.AddSingleton<IXmlDiagnosticsHandler, Audio3dProviderNameHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AudioParamIntHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, BooleanValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, CableRenderModeHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DynamicEnumValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector2Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector3Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector3ListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector4Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, HardwareUIntHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, IntListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, IntValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, NormalizedFloatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PositionLabelHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PrerequisiteExpressionHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ProjectileCategoryHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, RgbaValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ShaderVersionHexHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SfxCountHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SfxPercentageHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ShipClassTypeHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UintValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UvSlotIndexHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, VendorIdHexHandler>();

        // XmlStructureFact handler (well-formedness)
        services.AddSingleton<IXmlDiagnosticsHandler, XmlStructureHandler>();

        // XmlDuplicateTagFact + XmlNotesFact handlers (document-level)
        services.AddSingleton<IXmlDiagnosticsHandler, XmlDuplicateTagHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, XmlNotesHandler>();

        // XmlSymbolFact + XmlReferenceFact handlers (index-level)
        services.AddSingleton<IXmlDiagnosticsHandler, DuplicateSymbolHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UnresolvedReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, TypeMismatchHandler>();

        // Story handlers
        services.AddSingleton<IXmlDiagnosticsHandler, DeprecatedEventTypeHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, EventTypeNotesHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamRequiredHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamNotesHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamEnumHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamUnknownSlotHandler>();

        // Fact producers
        services.AddSingleton<IXmlStructuralValidator, XmlStructuralValidator>();
        services.AddSingleton<IXmlDocumentFactProducer, XmlDocumentFactProducer>();
        services.AddSingleton<IXmlIndexFactProducer, XmlIndexFactProducer>();
        services.AddSingleton<IStoryFactProducer, StoryFactProducer>();

        // Proposal system — add IXmlValueProposalProvider implementations here to register new providers
        services.AddSingleton<IXmlValueProposalRegistry, XmlValueProposalRegistry>();
        services.AddSingleton<IXmlValueProposalProvider, BooleanValueProposalProvider>();
        services.AddSingleton<IXmlValueProposalProvider, DynamicEnumValueProposalProvider>();

        services.AddSingleton<StoryParamValueProposalProvider>();

        // Completion registry — add IXmlCompletionProvider implementations here for index-aware completion
        services.AddSingleton<IXmlCompletionRegistry, XmlCompletionRegistry>();
        services.AddSingleton<IXmlCompletionProvider, GameObjectReferenceCompletionProvider>();
        services.AddSingleton<IXmlCompletionProvider, HardcodedSetCompletionProvider>();

        return services;
    }
}