namespace Katasec.DStream.Abstractions;

public interface IInputProvider : IProvider
{
    IAsyncEnumerable<Envelope> ReadAsync(IPluginContext ctx, CancellationToken ct);
}
