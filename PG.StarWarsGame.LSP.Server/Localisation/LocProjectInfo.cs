// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// ProjectName/Rank identify which .pgproj layer (root or dependency) this file belongs to —
// Label alone can collide across layers (e.g. two projects both using "MasterTextFile.csv").
public sealed record LocProjectInfo(
    string Label, string FilePath, string ResourceType, string ProjectName, int Rank);