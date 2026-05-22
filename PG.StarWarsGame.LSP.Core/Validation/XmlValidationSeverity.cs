// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Validation;

public enum XmlValidationSeverity
{
    /// <summary>
    ///     Reports an error.
    /// </summary>
    Error = 1,

    /// <summary>
    ///     Reports a warning.
    /// </summary>
    Warning = 2,

    /// <summary>
    ///     Reports information
    /// </summary>
    Information = 3,

    /// <summary>
    ///     Reports a hint.
    /// </summary>
    Hint = 4
}