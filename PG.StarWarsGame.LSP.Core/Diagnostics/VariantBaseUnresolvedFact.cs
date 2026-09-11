// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a variant names a base in <c>Variant_Of_Existing_Type</c> that does not resolve
///     to any object.
/// </summary>
/// <remarks>
///     <para>
///         The engine's one completely silent variant failure.
///         <c>GameObjectTypeManagerClass::Overlay_Object_Type</c> gives up and returns false, and
///         the caller ignores the return value - no assert, no warning, nothing in the log. The
///         object still loads, carrying only the tags the author wrote, so everything it expected
///         to inherit is simply missing and the only symptom is a unit behaving like a blank slate.
///     </para>
///     <para>
///         Distinct from <see cref="VariantCycleFact" />, which describes a chain that loops rather
///         than one that goes nowhere.
///     </para>
/// </remarks>
/// <param name="ObjectId">The variant whose base is missing.</param>
/// <param name="BaseId">The base name as written.</param>
public sealed record VariantBaseUnresolvedFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string BaseId
) : XmlFact(DocumentUri, Line, Column, Length);
