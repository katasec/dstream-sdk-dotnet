namespace Katasec.DStream.Abstractions;

/// <summary>
/// Optional interface for providers that need to validate config and dependencies at startup.
/// If validation fails, the provider signals the CLI with an error before any processing begins.
/// </summary>
public interface IValidatable
{
    /// <summary>
    /// Validate configuration and dependencies (e.g., connection strings, endpoints).
    /// Returns null if valid, or an error message string if validation fails.
    /// </summary>
    Task<string?> ValidateAsync(CancellationToken ct);
}
