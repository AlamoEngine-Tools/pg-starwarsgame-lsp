// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Decides which language a single-language localisation file holds.
///     <para>
///         A format that can hold several languages names them inside the file - CSV in its header
///         row, XML in a <c>Language</c> attribute. A format that holds one cannot, so the name
///         carries it instead: <c>mastertextfile_english.dat</c>, <c>mastertextfile_german.properties</c>.
///         That is the same convention Java's <c>ResourceBundle</c> uses, and the same one the
///         engine's own files follow.
///     </para>
///     <para>
///         This was previously DAT-only. <c>.properties</c> had no language source at all, so six read
///         and write paths hardcoded <see cref="ILanguageService.Default" /> while a seventh used the
///         configured locale - which meant the editor grid and the localisation index could disagree
///         about what language a file held.
///     </para>
/// </summary>
public static class LocalisationFileNameLanguageResolver
{
    /// <summary>
    ///     Whether a file in this format carries its language in its name.
    ///     <para>
    ///         The inverse of "can hold more than one language", and deliberately expressed that way:
    ///         one predicate decides both, so the set of single-language formats is listed once.
    ///     </para>
    /// </summary>
    public static bool CarriesLanguageInFileName(string extension)
    {
        return !LocalisationDocumentEditor.SupportsMultipleLanguages(extension);
    }

    /// <summary>
    ///     The language named by the file's <c>_&lt;LANGUAGE&gt;</c> suffix, if it has a resolvable one.
    ///     Matching is case-insensitive, so <c>_english</c>, <c>_ENGLISH</c> and <c>_English</c> are
    ///     the same language.
    /// </summary>
    public static bool TryResolve(
        string path, ILanguageService langService, out IAlamoLanguageDefinition? language)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var underscoreIdx = name.LastIndexOf('_');
        if (underscoreIdx < 0 || underscoreIdx == name.Length - 1)
        {
            language = null;
            return false;
        }

        var identifier = name[(underscoreIdx + 1)..];
        return langService.TryGetByIdentifier(identifier, out language);
    }

    /// <summary>
    ///     The file's language: its name if that names one, otherwise <paramref name="fallback" />.
    ///     <para>
    ///         <paramref name="resolvedFromFileName" /> is false when the fallback was used, so a
    ///         caller can tell the user which language it assumed. Files predating the convention are
    ///         not refused - that would break every project that has one - but the assumption is not
    ///         silent either, since an invisible language is the problem this resolver exists to remove.
    ///     </para>
    /// </summary>
    public static IAlamoLanguageDefinition Resolve(
        string path, ILanguageService langService, IAlamoLanguageDefinition fallback,
        out bool resolvedFromFileName)
    {
        if (TryResolve(path, langService, out var fromName) && fromName is not null)
        {
            resolvedFromFileName = true;
            return fromName;
        }

        resolvedFromFileName = false;
        return fallback;
    }

    /// <summary>
    ///     The game language the workspace is configured for, or the service default when the
    ///     configured identifier cannot be resolved.
    ///     <para>
    ///         Takes <see cref="LocalisationConfig.Language" /> - an Alamo identifier - and never
    ///         <see cref="LspConfiguration.Locale" />, which is ISO 639-1 and names the language the
    ///         LSP writes its own text in. Passing a locale here resolves nothing and silently yields
    ///         the default, which is exactly how the two settings stayed confused for so long.
    ///     </para>
    /// </summary>
    public static IAlamoLanguageDefinition Configured(
        ILanguageService langService, LocalisationConfig config)
    {
        return langService.TryGetByIdentifier(config.Language, out var language) && language is not null
            ? language
            : langService.Default;
    }
}
