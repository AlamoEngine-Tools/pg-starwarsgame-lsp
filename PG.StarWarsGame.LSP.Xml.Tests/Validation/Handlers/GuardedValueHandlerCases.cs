// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Shared discovery of the single-value-type <see cref="IXmlDiagnosticsHandler" />
///     implementations in the Xml assembly: concrete, parameterless handlers whose
///     <see cref="IXmlDiagnosticsHandler.FactType" /> is <see cref="XmlTagValueFact" /> and
///     whose <see cref="IXmlDiagnosticsHandler.HandledValueType" /> is non-null.
///     Reused by the value-type gate characterization and the later base-class refactor.
/// </summary>
internal static class GuardedValueHandlerCases
{
    public static readonly IReadOnlyList<Type> HandlerTypes =
        typeof(BooleanValueHandler).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlDiagnosticsHandler).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Where(IsSingleValueTypeHandler)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    public static IXmlDiagnosticsHandler Create(Type handlerType)
    {
        return (IXmlDiagnosticsHandler)Activator.CreateInstance(handlerType)!;
    }

    private static bool IsSingleValueTypeHandler(Type handlerType)
    {
        var handler = Create(handlerType);
        return handler.FactType == typeof(XmlTagValueFact) && handler.HandledValueType.HasValue;
    }
}