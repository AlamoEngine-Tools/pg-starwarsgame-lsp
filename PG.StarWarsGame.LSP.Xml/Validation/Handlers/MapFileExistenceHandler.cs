// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A named map that is not there is an Error, not a Warning (issue #132).
/// </summary>
/// <remarks>
///     Measured in the 2018 build: <c>GameModeClass::Load_Named_Map</c> opens the name as given, and
///     on failure retries it against <c>Get_Map_Path</c> with the extension forced to <c>.ted</c> -
///     so the engine is forgiving about how the map is spelled. When that second open also fails it
///     calls <c>Assert_Handler("false", "GameMode.cpp", 0x8db)</c> and returns false. Nothing stands
///     in for it: the hardcoded <c>_Desert_L5_01.ted</c> and <c>_Space_Temperate1.ted</c> defaults
///     belong to the EMPTY-name path in <c>GameModeManagerClass::Transition_To_Sub_Mode</c>, which a
///     misspelled name never reaches.
///     So unlike a missing model or texture, this does not degrade the battle - there is no battle.
/// </remarks>
public sealed class MapFileExistenceHandler : AssetFileExistenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.MapFileExistence;

    protected override ReferenceKind TargetKind => ReferenceKind.MapFile;
    protected override string AssetNoun => "Map";
    protected override IReadOnlyList<string> AllowedExtensions => [".ted"];
    protected override XmlDiagnosticSeverity MissingSeverity => XmlDiagnosticSeverity.Error;
}