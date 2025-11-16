namespace Katasec.DStream.Abstractions;

// ---------- Infrastructure lifecycle results ----------
public class InfrastructureResult
{
    public string Status { get; set; } = "Unknown";
    public string[]? Resources { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}
