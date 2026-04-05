using System.Text.Json;
using Katasec.DStream.Abstractions;

namespace Katasec.DStream.SDK.Core;

/// <summary>
/// Stdin/Stdout host that bridges existing SDK abstractions to simple stdin/stdout execution
/// Handles all the plumbing so provider authors only focus on business logic
/// 
/// Supports both legacy direct config mode and new command envelope mode for lifecycle management
/// </summary>
public static class StdioProviderHost
{
    /// <summary>
    /// Run a provider with command routing and startup handshake support (init/run/destroy/plan/status).
    /// This is the single entry point for all providers.
    ///
    /// Lifecycle: read command envelope → parse config → validate (IValidatable) → emit handshake → route command.
    /// The startup handshake ({"status":"ready"} or {"status":"error","message":"..."}) on stdout
    /// lets the CLI know whether the provider initialized successfully before data flows begin.
    /// </summary>
    public static async Task RunProviderWithCommandAsync<TProvider, TConfig>(CancellationToken cancellationToken = default)
        where TProvider : ProviderBase<TConfig>, IProvider, new()
        where TConfig : class, new()
    {
        try
        {
            // Setup graceful shutdown
            var cts = cancellationToken == default ? new CancellationTokenSource() : null;
            var effectiveToken = cancellationToken == default ? cts!.Token : cancellationToken;
            
            if (cts != null)
            {
                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    cts.Cancel();
                };
            }

            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Starting service...");

            // Read command envelope from stdin
            var envelopeJson = await Console.In.ReadLineAsync();
            if (string.IsNullOrEmpty(envelopeJson))
            {
                await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] No command envelope received");
                Environment.Exit(1);
            }

            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Received command envelope: {envelopeJson}");

            // Try to parse as command envelope first
            CommandEnvelope<TConfig>? envelope = null;
            TConfig? config = null;
            string command = "run"; // Default command

            try
            {
                envelope = JsonSerializer.Deserialize<CommandEnvelope<TConfig>>(envelopeJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (envelope != null)
                {
                    command = envelope.Command ?? "run";
                    config = envelope.Config ?? new TConfig();
                    await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Parsed command envelope - Command: {command}");
                }
            }
            catch (JsonException)
            {
                // Fallback: try parsing as direct config (backward compatibility)
                await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Command envelope parsing failed, trying direct config...");
                
                try
                {
                    config = JsonSerializer.Deserialize<TConfig>(envelopeJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    }) ?? new TConfig();
                    
                    command = "run"; // Default to run for backward compatibility
                    await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Parsed as direct config - defaulting to 'run' command");
                }
                catch (JsonException ex)
                {
                    await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Failed to parse configuration: {ex.Message}");
                    Environment.Exit(1);
                }
            }

            if (config == null)
            {
                await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] No valid configuration found");
                Environment.Exit(1);
            }

            // Create provider instance
            var provider = new TProvider();
            var context = new StdioPluginContext();
            provider.Initialize(config, context);

            // Run provider-specific validation if available
            if (provider is IValidatable validatable)
            {
                var validationError = await validatable.ValidateAsync(effectiveToken);
                if (validationError != null)
                {
                    var errorSignal = JsonSerializer.Serialize(new { status = "error", message = validationError });
                    await Console.Out.WriteLineAsync(errorSignal);
                    await Console.Out.FlushAsync();
                    await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Startup validation failed: {validationError}");
                    Environment.Exit(1);
                }
            }

            // Signal to CLI that provider is ready to proceed
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { status = "ready" }));
            await Console.Out.FlushAsync();

            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Executing command: {command}");

            // Route based on command
            switch (command.ToLowerInvariant())
            {
                case "init":
                    await HandleInitCommand<TProvider, TConfig>(provider, context, effectiveToken);
                    break;
                    
                case "destroy":
                    await HandleDestroyCommand<TProvider, TConfig>(provider, context, effectiveToken);
                    break;
                    
                case "plan":
                    await HandlePlanCommand<TProvider, TConfig>(provider, context, effectiveToken);
                    break;
                    
                case "status":
                    await HandleStatusCommand<TProvider, TConfig>(provider, context, effectiveToken);
                    break;
                    
                case "run":
                default:
                    await HandleRunCommand<TProvider, TConfig>(provider, context, effectiveToken);
                    break;
            }

            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Command '{command}' completed successfully.");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Fatal error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    // ---------- Command handlers ----------
    
    private static async Task HandleInitCommand<TProvider, TConfig>(TProvider provider, IPluginContext context, CancellationToken ct)
        where TProvider : ProviderBase<TConfig>, IProvider
        where TConfig : class
    {
        if (provider is IInfrastructureProvider infraProvider)
        {
            var result = await infraProvider.InitializeAsync(ct);
            await WriteInfrastructureResult(result);
        }
        else
        {
            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Provider does not support infrastructure operations");
            Environment.Exit(1);
        }
    }
    
    private static async Task HandleDestroyCommand<TProvider, TConfig>(TProvider provider, IPluginContext context, CancellationToken ct)
        where TProvider : ProviderBase<TConfig>, IProvider
        where TConfig : class
    {
        if (provider is IInfrastructureProvider infraProvider)
        {
            var result = await infraProvider.DestroyAsync(ct);
            await WriteInfrastructureResult(result);
        }
        else
        {
            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Provider does not support infrastructure operations");
            Environment.Exit(1);
        }
    }
    
    private static async Task HandlePlanCommand<TProvider, TConfig>(TProvider provider, IPluginContext context, CancellationToken ct)
        where TProvider : ProviderBase<TConfig>, IProvider
        where TConfig : class
    {
        if (provider is IInfrastructureProvider infraProvider)
        {
            var result = await infraProvider.PlanAsync(ct);
            await WriteInfrastructureResult(result);
        }
        else
        {
            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Provider does not support infrastructure operations");
            Environment.Exit(1);
        }
    }
    
    private static async Task HandleStatusCommand<TProvider, TConfig>(TProvider provider, IPluginContext context, CancellationToken ct)
        where TProvider : ProviderBase<TConfig>, IProvider
        where TConfig : class
    {
        if (provider is IInfrastructureProvider infraProvider)
        {
            var result = await infraProvider.GetStatusAsync(ct);
            await WriteInfrastructureResult(result);
        }
        else
        {
            await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Provider does not support infrastructure operations");
            Environment.Exit(1);
        }
    }
    
    private static async Task HandleRunCommand<TProvider, TConfig>(TProvider provider, IPluginContext context, CancellationToken ct)
        where TProvider : ProviderBase<TConfig>, IProvider
        where TConfig : class
    {
        // Route to appropriate provider type
        switch (provider)
        {
            case IInputProvider inputProvider:
                await HandleInputProviderRun(inputProvider, context, ct);
                break;
                
            case IOutputProvider outputProvider:
                await HandleOutputProviderRun(outputProvider, context, ct);
                break;
                
            default:
                await Console.Error.WriteLineAsync($"[{typeof(TProvider).Name}] Unknown provider type for run command");
                Environment.Exit(1);
                break;
        }
    }
    
    private static async Task HandleInputProviderRun(IInputProvider provider, IPluginContext context, CancellationToken ct)
    {
        await Console.Error.WriteLineAsync("Starting data generation...");
        
        // Read from input provider and emit to stdout
        await foreach (var envelope in provider.ReadAsync(context, ct))
        {
            if (ct.IsCancellationRequested) break;

            // Convert envelope to JSON and write to stdout
            var envelopeDto = new EnvelopeDto
            {
                Data = envelope.Payload,
                Metadata = envelope.Meta?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object?>()
            };

            var json = JsonSerializer.Serialize(envelopeDto, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await Console.Out.WriteLineAsync(json);
            await Console.Out.FlushAsync();
        }
    }
    
    // Batching defaults — matches original production config (0.0.12)
    private const int DefaultBatchSize = 10;
    private static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(5);

    private static async Task HandleOutputProviderRun(IOutputProvider provider, IPluginContext context, CancellationToken ct)
    {
        await Console.Error.WriteLineAsync("Starting data processing...");

        var messageCount = 0;
        var batch = new List<Envelope>(DefaultBatchSize);
        var lastFlush = DateTime.UtcNow;

        // Read stdin with a timeout so we can flush partial batches on the time interval
        while (!ct.IsCancellationRequested)
        {
            // Check if we need a time-based flush before reading the next line
            if (batch.Count > 0 && DateTime.UtcNow - lastFlush >= DefaultFlushInterval)
            {
                await FlushBatch(provider, batch, context, ct);
                messageCount += batch.Count;
                batch.Clear();
                lastFlush = DateTime.UtcNow;
            }

            // Use a timed read: either we get a line or we timeout and check for flush
            var line = await ReadLineWithTimeout(DefaultFlushInterval, ct);

            if (line == null)
            {
                // null means EOF (stdin closed) — flush remaining and exit
                if (batch.Count > 0)
                {
                    await FlushBatch(provider, batch, context, ct);
                    messageCount += batch.Count;
                }
                break;
            }

            if (line == "")
            {
                // Empty string means timeout — loop back to check flush
                continue;
            }

            try
            {
                var envelopeData = JsonSerializer.Deserialize<EnvelopeDto>(line, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                if (envelopeData != null)
                {
                    var envelope = new Envelope(
                        envelopeData.Data ?? new object(),
                        envelopeData.Metadata ?? new Dictionary<string, object?>()
                    );

                    batch.Add(envelope);

                    // Flush on batch size threshold
                    if (batch.Count >= DefaultBatchSize)
                    {
                        await FlushBatch(provider, batch, context, ct);
                        messageCount += batch.Count;
                        batch.Clear();
                        lastFlush = DateTime.UtcNow;
                    }
                }
            }
            catch (JsonException)
            {
                await Console.Error.WriteLineAsync($"Failed to parse JSON: {line}");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Error processing message: {ex.Message}");
            }
        }

        await Console.Error.WriteLineAsync($"Processed {messageCount} messages. Stream ended.");
    }

    private static async Task FlushBatch(IOutputProvider provider, List<Envelope> batch, IPluginContext context, CancellationToken ct)
    {
        await Console.Error.WriteLineAsync($"Flushing batch of {batch.Count} messages...");
        await provider.WriteAsync(batch, context, ct);
    }

    /// <summary>
    /// Read a line from stdin with a timeout. Returns:
    /// - The line string if a line was read
    /// - null if stdin is closed (EOF)
    /// - empty string if the timeout elapsed (caller should retry)
    /// </summary>
    private static async Task<string?> ReadLineWithTimeout(TimeSpan timeout, CancellationToken ct)
    {
        var readTask = Console.In.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeout, ct));

        if (completed == readTask)
            return await readTask; // null means EOF, otherwise the line

        return ""; // timeout — signal caller to check flush
    }
    
    private static async Task WriteInfrastructureResult(InfrastructureResult result)
    {
        // Write infrastructure result to stdout for CLI consumption
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        await Console.Out.WriteLineAsync(json);
        await Console.Out.FlushAsync();
        
        // Also log to stderr for visibility
        await Console.Error.WriteLineAsync($"Infrastructure operation result: {result.Status} - {result.Message}");
        if (result.Resources?.Length > 0)
        {
            await Console.Error.WriteLineAsync($"Resources: {string.Join(", ", result.Resources)}");
        }
    }
}

/// <summary>
/// DTO for serializing/deserializing JSON envelopes in stdin/stdout communication
/// </summary>
internal class EnvelopeDto
{
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// Simple plugin context for stdin/stdout mode
/// </summary>
internal class StdioPluginContext : IPluginContext
{
    public object Logger => new StdioLogger();
}

/// <summary>
/// Simple logger that writes to stderr for stdin/stdout mode
/// </summary>
internal class StdioLogger
{
    public void Info(string message) => Console.Error.WriteLine($"[INFO] {message}");
    public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
    public void Debug(string message) => Console.Error.WriteLine($"[DEBUG] {message}");
}