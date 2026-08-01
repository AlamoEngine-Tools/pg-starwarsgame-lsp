// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     One staged edit to a keyed translation file, addressed by key.
///     <para>
///         A translation file is a lookup table: the key is the identity, and the order rows happen
///         to sit in carries no meaning the user can see. Addressing by key means there is no
///         position to drift, so this vocabulary needs no equivalent of
///         <see cref="LocEditCommandDto.ExpectedKey" /> - there is nothing to desync.
///     </para>
///     <para>
///         Credits files cannot use this: they repeat keys and their order is the content. They keep
///         <see cref="LocEditCommandDto" />.
///     </para>
/// </summary>
/// <param name="Kind">
///     <c>setValue</c>, <c>addEntry</c>, <c>deleteEntry</c>, <c>renameKey</c> or
///     <c>addLanguage</c>.
/// </param>
/// <param name="Key">The entry being edited. Absent only for <c>addLanguage</c>.</param>
/// <param name="NewKey">The replacement name for <c>renameKey</c>.</param>
/// <param name="Language">Column for <c>setValue</c>, or the language being added.</param>
/// <param name="Value">New value for <c>setValue</c>.</param>
/// <param name="Values">Values for every language on an <c>addEntry</c>.</param>
public sealed record LocKeyedCommandDto(
    string Kind,
    string? Key = null,
    string? NewKey = null,
    string? Language = null,
    string? Value = null,
    IReadOnlyList<LocValueDto>? Values = null);

/// <summary>
///     The outcome of translating a keyed batch into positional commands.
/// </summary>
/// <param name="Success">Whether every command resolved.</param>
/// <param name="Commands">
///     The positional equivalent, or null when translation failed - a partially translated batch is
///     never returned, because applying half of one would leave the file in a state the user never
///     asked for.
/// </param>
/// <param name="FailedIndex">0-based position of the offending command in the batch.</param>
/// <param name="Error">What went wrong, phrased for the user.</param>
/// <param name="ResultingKeys">
///     The keys the file will hold once the batch lands, in row order. Lets the validator inspect
///     the outcome without composing the file and parsing it back - which is also what makes a
///     compiled <c>.dat</c> validatable, since it has no composed text to re-read.
/// </param>
public sealed record KeyedTranslationResult(
    bool Success,
    IReadOnlyList<LocEditCommandDto>? Commands = null,
    int? FailedIndex = null,
    string? Error = null,
    IReadOnlyList<string>? ResultingKeys = null)
{
    public static KeyedTranslationResult Ok(
        IReadOnlyList<LocEditCommandDto> commands, IReadOnlyList<string> resultingKeys)
    {
        return new KeyedTranslationResult(true, commands, ResultingKeys: resultingKeys);
    }

    public static KeyedTranslationResult Fail(int commandIndex, string error)
    {
        return new KeyedTranslationResult(false, null, commandIndex, error);
    }
}
