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

        // Validator system — add IXmlValueValidator implementations here to register new validators
        services.AddSingleton<IXmlValueValidatorRegistry, XmlValueValidatorRegistry>();
        services.AddSingleton<IXmlValueValidator, BooleanValueValidator>();
        services.AddSingleton<IXmlValueValidator, FloatValueValidator>();
        services.AddSingleton<IXmlValueValidator, ShaderVersionHexValidator>();
        services.AddSingleton<IXmlValueValidator, VendorIdHexValidator>();
        services.AddSingleton<IXmlValueValidator, DynamicEnumValueValidator>();
        services.AddSingleton<IXmlValueValidator, FloatVector3Validator>();
        services.AddSingleton<IXmlValueValidator, FloatVector4Validator>();
        services.AddSingleton<IXmlValueValidator, RgbaValidator>();

        // Proposal system — add IXmlValueProposalProvider implementations here to register new providers
        services.AddSingleton<IXmlValueProposalRegistry, XmlValueProposalRegistry>();
        services.AddSingleton<IXmlValueProposalProvider, BooleanValueProposalProvider>();

        return services;
    }
}