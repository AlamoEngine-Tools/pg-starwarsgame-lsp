// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: <c>Variant_Of_Existing_Type</c> written on an object type that has no variant
///     machinery.
/// </summary>
/// <remarks>
///     <para>
///         Variant derivation exists for <c>GameObjectType</c> and nothing else -
///         <c>Overlay_Object_Type</c> and <c>Overlay_Types</c> are the only derivation path in the
///         engine, and the tag has exactly one row in the parser table, under that type. Every
///         other class parses its own tags and warns about whatever it does not recognise:
///         <c>HardPointDataClass::Parse_Database_Entry() - Unprocessed entry
///         'Variant_Of_Existing_Type' in object 'HP_Whatever'.</c>
///     </para>
///     <para>
///         So this failure does at least tell you about itself, unlike an unresolvable base - but
///         only in a log file, and the object loads anyway with the tag ignored. There is no
///         inheritance for hardpoints; two that should share a setup have to be written out
///         separately.
///     </para>
///     <para>
///         Needs its own rule because the tag resolver falls back to a flat lookup across every
///         type, so a real tag on the wrong element resolves and the unknown-tag rule never sees it.
///     </para>
/// </remarks>
/// <param name="TagName">The variant tag as authored.</param>
/// <param name="TypeName">The object type the document is registered for, e.g. <c>HardPoint</c>.</param>
public sealed record VariantTagNotSupportedFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName,
    string TypeName) : XmlFact(DocumentUri, Line, Column, Length);
