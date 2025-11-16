namespace Katasec.DStream.Abstractions;

public interface IOutputProvider : IProvider
{
    Task WriteAsync(IEnumerable<Envelope> batch, IPluginContext ctx, CancellationToken ct);
}
