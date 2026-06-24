// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlValidatorCompletenessTest
{
    [Fact]
    [Trait("Category", "FeatureCompleteness")]
    public void AllNonObsoleteXmlValueTypes_HaveAtLeastOneHandler()
    {
        var allTypes = Enum.GetValues<XmlValueType>()
            .Where(v => typeof(XmlValueType)
                .GetField(v.ToString())!
                .GetCustomAttribute<ObsoleteAttribute>() is null)
            .ToHashSet();

        var handlerAssembly = typeof(FloatValueHandler).Assembly;
        var coveredTypes = handlerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IXmlDiagnosticsHandler).IsAssignableFrom(t))
            .Select(t =>
            {
                try
                {
                    return Activator.CreateInstance(t);
                }
                catch
                {
                    return null;
                }
            })
            .OfType<IXmlDiagnosticsHandler>()
            .SelectMany(h => h.HandledValueTypes)
            .ToHashSet();

        var gaps = allTypes.Except(coveredTypes).OrderBy(v => v.ToString()).ToList();
        Assert.True(gaps.Count == 0,
            $"No validator registered for XmlValueType(s): {string.Join(", ", gaps)}");
    }
}