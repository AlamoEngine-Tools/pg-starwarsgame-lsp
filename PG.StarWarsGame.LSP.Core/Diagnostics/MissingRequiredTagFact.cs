// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: an object omits a tag its type cannot work without.
/// </summary>
/// <remarks>
///     <para>
///         Absence is not something a value check can see, so this is produced by a cross-tag rule
///         that has the object's whole child set.
///     </para>
///     <para>
///         An empty tag counts as missing. The engine's complaint is that the value "has not been
///         set", and a bone name present but blank is as unset as one that was never written.
///     </para>
/// </remarks>
/// <param name="OwningType">The type that requires the tag, for the message.</param>
/// <param name="TagName">The tag that is missing or empty.</param>
public sealed record MissingRequiredTagFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string OwningType,
    string TagName) : XmlFact(DocumentUri, Line, Column, Length);
