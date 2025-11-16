namespace Katasec.DStream.Abstractions;

// ---------- Infrastructure lifecycle management ----------
public interface IInfrastructureProvider : IProvider
{
    Task<InfrastructureResult> InitializeAsync(CancellationToken ct);
    Task<InfrastructureResult> DestroyAsync(CancellationToken ct);
    Task<InfrastructureResult> GetStatusAsync(CancellationToken ct);
    Task<InfrastructureResult> PlanAsync(CancellationToken ct);
}
