// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Validation;

public record XmlValidationResult
{
    public required bool IsValid { get; init; }
    public required XmlValidationSeverity Severity { get; init; }
    public required string Message { get; init; }

    public static XmlValidationResult Valid()
    {
        return new XmlValidationResult
            { IsValid = true, Severity = XmlValidationSeverity.Error, Message = string.Empty };
    }

    public static XmlValidationResult Failure(string message)
    {
        return new XmlValidationResult { IsValid = false, Severity = XmlValidationSeverity.Error, Message = message };
    }
}
