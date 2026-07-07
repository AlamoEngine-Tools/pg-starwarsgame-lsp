// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.CodeLens;
using PG.StarWarsGame.LSP.Xml.Commands;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Variants;

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
        services.AddSingleton<IXmlDiagnosticsHandler, DeprecatedTagHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ContextNamePairHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ContextNameListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DamageNonzeroHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PresenceInducedAnimationsHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, Audio3dProviderNameHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AudioParamIntHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AudioFileFormatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, BooleanValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, MapFileFormatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ModelFileFormatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, TextureFileFormatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, CableRenderModeHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DynamicEnumValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector2Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector3Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector3ListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatVector4Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UintHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, IntListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, IntValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, NormalizedFloatHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PositionLabelHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PrerequisiteExpressionHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DisallowedOrOperatorHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ProjectileCategoryHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, NameReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, TypeReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FactionReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SFXEventReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SpeechEventReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, MusicEventReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SfxEventHudReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, Type35Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, Type36Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, Type37Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, Type38Handler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AbilityDefinitionSubObjectListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, GuiActivatedAbilityDefinitionSubObjectListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, NameReferenceListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, TypeReferenceListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ConditionalSfxEventHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DeathCloneSpecHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, HardPointTypeToTextureMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, LocalisationToTextureMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AbilitySfxMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, HardPointSfxMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AbilityModMultiplierHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AbilityModFlagHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, MovieFrameTriggerHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, CommandBarPropertyHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PerFactionValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PerFactionPlanetHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PerFactionIntMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, PerFactionObjectListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ForceDeploymentListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UnitSpawnTableHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, UnitSpawnProbabilityTableHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, TupleListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ConditionalSpeechEventHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, FloatTupleListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, IntFloatTupleListHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, InaccuracyMapHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, DamageToArmorModHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, RgbaValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ShaderVersionHexHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SfxCountHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, SfxPercentageHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ShipClassTypeHandler>();
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

        // Localisation handlers
        services.AddSingleton<IXmlDiagnosticsHandler, LocalisationKeyExistenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, LocalisationKeyListExistenceHandler>();

        // Asset-file existence handlers (shared asset-file catalog)
        services.AddSingleton<IXmlDiagnosticsHandler, TextureFileExistenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, ModelFileExistenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, AudioFileExistenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, MapFileExistenceHandler>();

        // Story handlers
        services.AddSingleton<IXmlDiagnosticsHandler, DeprecatedEventTypeHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, EventTypeNotesHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamRequiredHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamNotesHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamValueHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamEnumHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamReferenceHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, StoryParamUnknownSlotHandler>();

        // Cross-tag validation handler
        services.AddSingleton<IXmlDiagnosticsHandler, SquadronOffsetsMismatchHandler>();

        // Shared parse source: one HAP parse per (document, content) reused by indexing,
        // diagnostics, and every request handler. Capacity from ServerOptions.ParseCacheCapacity.
        services.AddSingleton<IXmlParseCache>(sp => new XmlParseCache(
            sp.GetRequiredService<IDocumentTextSource>(),
            sp.GetRequiredService<ServerOptions>().ParseCacheCapacity,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<XmlParseCache>>()));

        // Fact producers
        services.AddSingleton<IXmlStructuralValidator, XmlStructuralValidator>();
        services.AddSingleton<IXmlCrossTagRule, SquadronOffsetsRule>();
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
        services.AddSingleton<IXmlCompletionProvider, LocalisationKeyCompletionProvider>();
        services.AddSingleton<IXmlCompletionProvider, AssetFileCompletionProvider>();

        // boneName completion helper (resolved into BoneNameValueCompletionStrategy via DI)
        services.AddSingleton<BoneNameCompletionHelper>();

        // Tag-name completion strategies — add IXmlTagNameCompletionStrategy implementations here
        services.AddSingleton<IXmlTagNameCompletionStrategyRegistry, XmlTagNameCompletionStrategyRegistry>();
        services.AddSingleton<IXmlTagNameCompletionStrategy, StoryEventTagNameStrategy>();
        services.AddSingleton<IXmlTagNameCompletionStrategy, StandardTagNameStrategy>();

        // Tag-value completion strategies — add IXmlTagValueCompletionStrategy implementations here
        services.AddSingleton<IXmlTagValueCompletionStrategyRegistry, XmlTagValueCompletionStrategyRegistry>();
        services.AddSingleton<IXmlTagValueCompletionStrategy, StoryParamValueCompletionStrategy>();
        services.AddSingleton<IXmlTagValueCompletionStrategy, BoneNameValueCompletionStrategy>();
        services.AddSingleton<IXmlTagValueCompletionStrategy, StandardValueCompletionStrategy>();
        services.AddSingleton<IXmlTagValueCompletionStrategy, TupleValueCompletionStrategy>();

        // Code action providers — add IXmlCodeActionProvider implementations here to register new providers
        services.AddSingleton<IXmlCodeActionRegistry, XmlCodeActionRegistry>();
        services.AddSingleton<IXmlCodeActionProvider, FixSuggestionCodeActionProvider>();
        services.AddSingleton<IXmlCodeActionProvider, CreateLocKeyCodeActionProvider>();
        services.AddSingleton<IXmlCodeActionProvider, SquadronSyncCodeActionProvider>();
        services.AddSingleton<IXmlCodeActionProvider, RemoveRedundantOverrideCodeActionProvider>();
        services.AddSingleton<IXmlCodeActionProvider, RemoveEarlierDuplicatesCodeActionProvider>();

        // Hover strategies — add IXmlHoverStrategy implementations here to register new strategies
        services.AddSingleton<IXmlHoverStrategyRegistry, XmlHoverStrategyRegistry>();
        services.AddSingleton<IXmlHoverStrategy, ReferenceHoverStrategy>();
        services.AddSingleton<IXmlHoverStrategy, AssetHoverStrategy>();
        services.AddSingleton<IXmlHoverStrategy, TagNameHoverStrategy>();

        // Code lens providers — add IXmlCodeLensProvider implementations here to register new providers
        services.AddSingleton<IXmlCodeLensRegistry, XmlCodeLensRegistry>();
        services.AddSingleton<IXmlCodeLensProvider, ReferencesCodeLensProvider>();
        services.AddSingleton<IXmlCodeLensProvider, VariantCodeLensProvider>();
        services.AddSingleton<IXmlCodeLensProvider, OverrideCodeLensProvider>();

        // Inlay hint providers — add IXmlInlayHintProvider implementations here to register new providers
        services.AddSingleton<IXmlInlayHintRegistry, XmlInlayHintRegistry>();
        services.AddSingleton<IXmlInlayHintProvider, LocalisationKeySingleValueInlayHintProvider>();
        services.AddSingleton<IXmlInlayHintProvider, LocalisationKeyMultiValueInlayHintProvider>();
        services.AddSingleton<IXmlInlayHintProvider, VariantInlayHintProvider>();

        // Variant inheritance (Variant_Of_Existing_Type) — workspace tag source feeds the
        // EffectiveObjectResolver; shadows shipped-game baseline tags.
        services.AddSingleton<IVariantTagSource, WorkspaceVariantTagSource>();
        services.AddSingleton<IXmlVariantFactProducer, XmlVariantFactProducer>();
        services.AddSingleton<IXmlDiagnosticsHandler, VariantCycleHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, VariantIgnoredOverrideHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, VariantRedundantOverrideHandler>();

        // Layer-shadow and cross-type shadow warnings
        services.AddSingleton<IXmlLayerShadowFactProducer, XmlLayerShadowFactProducer>();
        services.AddSingleton<IXmlDiagnosticsHandler, CrossLayerShadowHandler>();
        services.AddSingleton<IXmlDiagnosticsHandler, CrossTypeShadowHandler>();

        return services;
    }
}