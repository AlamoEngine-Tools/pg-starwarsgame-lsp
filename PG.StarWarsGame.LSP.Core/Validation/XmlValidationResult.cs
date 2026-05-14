namespace PG.StarWarsGame.LSP.Core.Validation;

public enum XmlValidationSeverity
{
    Error,
    Warning
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