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