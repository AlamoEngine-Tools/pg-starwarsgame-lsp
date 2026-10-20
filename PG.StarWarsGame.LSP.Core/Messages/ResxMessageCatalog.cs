// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Resources;

namespace PG.StarWarsGame.LSP.Core.Messages;

/// <summary>
///     A message catalogue read from <c>.resx</c> resources, with satellite assemblies per language.
///     <para>
///         The locale picks the TEXT only. Values are formatted invariant, so a number reads the way
///         the author's XML holds it - <c>0.5</c>, never <c>0,5</c> - in every language. A locale
///         without a translation, or one no culture knows, falls back to English.
///     </para>
/// </summary>
public sealed class ResxMessageCatalog : IMessageCatalog
{
    private static readonly ResourceManager ServerResources = new(
        "PG.StarWarsGame.LSP.Core.Resources.ServerMessages", typeof(ResxMessageCatalog).Assembly);

    private readonly CultureInfo _culture;
    private readonly ResourceManager _resources;

    public ResxMessageCatalog(ResourceManager resources, string locale)
    {
        _resources = resources;
        _culture = CultureFor(locale);
    }

    public string Format(string key, params object?[] args)
    {
        var text = _resources.GetString(key, _culture)
                   ?? throw new KeyNotFoundException($"The message catalogue carries no '{key}'.");
        return string.Format(CultureInfo.InvariantCulture, text, args);
    }

    /// <summary>The server's own catalogue in the given locale.</summary>
    public static ResxMessageCatalog Server(string locale)
    {
        return new ResxMessageCatalog(ServerResources, locale);
    }

    private static CultureInfo CultureFor(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}