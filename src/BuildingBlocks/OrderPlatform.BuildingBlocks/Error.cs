namespace OrderPlatform.BuildingBlocks;

/// <summary>
/// A business or technical failure with a stable, machine-readable code (ADR-0020 error codes).
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    /// <summary>
    /// Machine-readable detail a client can act on, written as problem details extension members (RFC 9457, ADR-0020) —
    /// e.g. the state that made a command impossible. <see cref="Message"/> explains the same thing to a human and may
    /// change; these fields are part of the contract, like <see cref="Code"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();

    /// <summary>The same failure with one more detail field.</summary>
    public Error With(string name, object? value) =>
        this with { Details = new Dictionary<string, object?>(Details, StringComparer.Ordinal) { [name] = value } };

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
    public static Error Integration(string code, string message) => new(code, message, ErrorKind.Integration);
}

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    BusinessRule,

    /// <summary>An external system failed or is unreachable (ADR-0014); never the caller's fault.</summary>
    Integration,
}
