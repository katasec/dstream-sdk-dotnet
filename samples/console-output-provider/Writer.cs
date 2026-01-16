using System.Text.Json;
using Katasec.DStream.Abstractions;
using Katasec.DStream.SDK.Core;

namespace ConsoleOutputProvider;

// Core data processing logic - handles WriteAsync implementation
public partial class OutputProvider
{
    private static int _messageCount = 0;
    
    // Core data processing implementation
    public async Task WriteAsync(IEnumerable<Envelope> batch, IPluginContext ctx, CancellationToken ct)
    {
        await Console.Error.WriteLineAsync($"[ConsoleOutputProvider] Processing batch of {batch.Count()} envelopes with format '{Config.OutputFormat}'");
        
        foreach (var envelope in batch)
        {
            if (ct.IsCancellationRequested) break;
            
            _messageCount++;
            await OutputFormattedEnvelopeAsync(envelope, _messageCount);
        }
    }
    
    private async Task OutputFormattedEnvelopeAsync(Envelope envelope, int messageCount)
    {
        var format = Config.OutputFormat?.ToLower() ?? "simple";
        
        switch (format)
        {
            case "json":
                var json = JsonSerializer.Serialize(new { envelope.Payload, envelope.Meta });
                await Console.Out.WriteLineAsync(json);
                break;
                
            case "structured":
                var structured = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
                await Console.Out.WriteLineAsync($"--- Message #{messageCount} ---");
                await Console.Out.WriteLineAsync(structured);
                break;
                
            default: // "simple"
                await Console.Out.WriteLineAsync($"Message #{messageCount}: {JsonSerializer.Serialize(envelope.Payload)}");
                break;
        }
    }
}
