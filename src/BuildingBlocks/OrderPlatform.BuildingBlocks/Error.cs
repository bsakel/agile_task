namespace OrderPlatform.BuildingBlocks;

/// <summary>
/// A business or technical failure with a stable, machine-readable code (ADR-0020 error codes).
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
}

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    BusinessRule,
}
