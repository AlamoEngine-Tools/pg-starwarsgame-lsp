// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Validation;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Completion.Providers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Validators;

namespace PG.StarWarsGame.LSP.Xml;

public static class XmlLanguageServiceExtensions
{
    public static IServiceCollection AddXmlLanguageServices(this IServiceCollection services)
    {
        services.AddSingleton<XmlDiagnosticsPublisher>();
        services.AddSingleton<StoryParserDiagnosticCollector>();

        // Validator system — add IXmlValueValidator implementations here to register new validators
        services.AddSingleton<IXmlValueValidatorRegistry, XmlValueValidatorRegistry>();
        services.AddSingleton<IXmlValueValidator, Audio3dProviderNameValidator>();
        services.AddSingleton<IXmlValueValidator, AudioParamIntValidator>();
        services.AddSingleton<IXmlValueValidator, BooleanValueValidator>();
        services.AddSingleton<IXmlValueValidator, CableRenderModeValidator>();
        services.AddSingleton<IXmlValueValidator, DynamicEnumValueValidator>();
        services.AddSingleton<IXmlValueValidator, FactionReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, FloatListValidator>();
        services.AddSingleton<IXmlValueValidator, FloatValueValidator>();
        services.AddSingleton<IXmlValueValidator, FloatVector2Validator>();
        services.AddSingleton<IXmlValueValidator, FloatVector3ListValidator>();
        services.AddSingleton<IXmlValueValidator, FloatVector3Validator>();
        services.AddSingleton<IXmlValueValidator, FloatVector4Validator>();
        services.AddSingleton<IXmlValueValidator, GameObjectTypeReferenceListValidator>();
        services.AddSingleton<IXmlValueValidator, HardwareUIntValidator>();
        services.AddSingleton<IXmlValueValidator, IntListValidator>();
        services.AddSingleton<IXmlValueValidator, IntValueValidator>();
        services.AddSingleton<IXmlValueValidator, MusicEventReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, NameReferenceListValidator>();
        services.AddSingleton<IXmlValueValidator, NameReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, NormalizedFloatValidator>();
        services.AddSingleton<IXmlValueValidator, PositionLabelValidator>();
        services.AddSingleton<IXmlValueValidator, PrerequisiteExpressionValidator>();
        services.AddSingleton<IXmlValueValidator, ProjectileCategoryValidator>();
        services.AddSingleton<IXmlValueValidator, RgbaValidator>();
        services.AddSingleton<IXmlValueValidator, SFXEventReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, SfxCountValidator>();
        services.AddSingleton<IXmlValueValidator, SfxEventHudReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, SfxPercentageValidator>();
        services.AddSingleton<IXmlValueValidator, ShaderVersionHexValidator>();
        services.AddSingleton<IXmlValueValidator, ShipClassTypeValidator>();
        services.AddSingleton<IXmlValueValidator, SpeechEventReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, TypeReferenceListValidator>();
        services.AddSingleton<IXmlValueValidator, TypeReferenceValidator>();
        services.AddSingleton<IXmlValueValidator, UintValueValidator>();
        services.AddSingleton<IXmlValueValidator, UvSlotIndexValidator>();
        services.AddSingleton<IXmlValueValidator, VendorIdHexValidator>();

        // Proposal system — add IXmlValueProposalProvider implementations here to register new providers
        services.AddSingleton<IXmlValueProposalRegistry, XmlValueProposalRegistry>();
        services.AddSingleton<IXmlValueProposalProvider, BooleanValueProposalProvider>();
        services.AddSingleton<IXmlValueProposalProvider, DynamicEnumValueProposalProvider>();

        return services;
    }
}