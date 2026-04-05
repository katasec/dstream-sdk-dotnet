# DStream .NET SDK

A modern .NET SDK for building **DStream providers** using stdin/stdout communication.
Providers are simple standalone binaries that communicate with the DStream CLI via JSON over stdin/stdout pipes.  
Each provider defines a **Config**, a **Provider class**, and implements either `IInputProvider` or `IOutputProvider`.

## Quick Start

**1. Reference the SDK:**
```xml
<ProjectReference Include="../dstream-dotnet-sdk/sdk/Katasec.DStream.Abstractions/Katasec.DStream.Abstractions.csproj" />
<ProjectReference Include="../dstream-dotnet-sdk/sdk/Katasec.DStream.SDK.Core/Katasec.DStream.SDK.Core.csproj" />
```

**2. Create your provider (top-level statements):**
```csharp
using Katasec.DStream.SDK.Core;

await StdioProviderHost.RunProviderWithCommandAsync<MyProvider, MyConfig>();
```

**That's it!** The SDK handles all the stdin/stdout plumbing, configuration parsing, JSON serialization, startup handshake, command routing, and process lifecycle management.

---

## Provider Basics

1. **Config class**  
   Each provider defines a config model for its settings. Example:

   ```csharp
   public sealed record CounterConfig
   {
       public int Interval { get; init; } = 1000;
       public int MaxCount { get; init; } = 0;
   }
   ```

2. **Provider base**  
   Providers inherit from `ProviderBase<TConfig>`. This gives them access to `Config` (populated by the SDK at runtime).

   ```csharp
   public abstract class ProviderBase<TConfig>
   {
       protected TConfig Config { get; private set; }
       protected IPluginContext Ctx { get; private set; }
       public void Initialize(TConfig config, IPluginContext ctx) { ... }
   }
   ```

3. **Provider interfaces**  
   - `IInputProvider`: produces `Envelope` events via `IAsyncEnumerable<Envelope> ReadAsync()`.  
   - `IOutputProvider`: consumes `Envelope` events via `Task WriteAsync(IEnumerable<Envelope> batch, ...)`.  
   - Each provider implements exactly one interface (input OR output, not both).

4. **Startup validation (optional)**  
   Providers that need to validate config at startup (e.g., test a connection string) implement `IValidatable`:

   ```csharp
   public interface IValidatable
   {
       Task<string?> ValidateAsync(CancellationToken ct);
       // Return null if valid, or an error message if validation fails
   }
   ```

   When validation fails, the SDK emits `{"status":"error","message":"..."}` to the CLI and exits — no data is processed, no checkpoints updated.

---

## Provider Lifecycle

Every provider follows this startup sequence, handled automatically by the SDK:

```
CLI                              Provider (via SDK)
 │                                  │
 │── command envelope (stdin) ─────>│  {"command":"run","config":{...}}
 │                                  │
 │                                  │  1. Parse config
 │                                  │  2. Initialize provider
 │                                  │  3. Run IValidatable.ValidateAsync() if implemented
 │                                  │
 │<── handshake signal (stdout) ────│  {"status":"ready"} or {"status":"error","message":"..."}
 │                                  │
 │   (data relay / infra result)    │  4. Route command (run/init/plan/status/destroy)
```

The handshake ensures the CLI knows whether the provider started successfully before any data flows. This prevents silent failures (e.g., missing connection string) from causing data loss.

### Command routing

The SDK routes commands automatically based on the command envelope:

| Command | Routed to | Use case |
|---------|-----------|----------|
| `run` | `IInputProvider.ReadAsync()` or `IOutputProvider.WriteAsync()` | Normal data flow |
| `init` | `IInfrastructureProvider.InitializeAsync()` | Create infrastructure (queues, topics) |
| `destroy` | `IInfrastructureProvider.DestroyAsync()` | Tear down infrastructure |
| `plan` | `IInfrastructureProvider.PlanAsync()` | Preview infrastructure changes |
| `status` | `IInfrastructureProvider.GetStatusAsync()` | Check infrastructure state |

---

## Example: Counter Input Provider

This provider generates an incrementing counter every `Interval` milliseconds.

```csharp
using System.Runtime.CompilerServices;
using Katasec.DStream.Abstractions;
using Katasec.DStream.SDK.Core;

await StdioProviderHost.RunProviderWithCommandAsync<CounterInputProvider, CounterConfig>();

public class CounterInputProvider : ProviderBase<CounterConfig>, IInputProvider
{
    public async IAsyncEnumerable<Envelope> ReadAsync(IPluginContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var count = 0;
        
        while (!ct.IsCancellationRequested)
        {
            count++;
            
            // Stop if max count reached
            if (Config.MaxCount > 0 && count > Config.MaxCount)
                break;

            // Create counter data
            var data = new { value = count, timestamp = DateTimeOffset.UtcNow };
            var metadata = new Dictionary<string, object?>
            {
                ["seq"] = count,
                ["provider"] = "counter-input-provider"
            };
            
            yield return new Envelope(data, metadata);
            
            await Task.Delay(Config.Interval, ct);
        }
    }
}

public sealed record CounterConfig
{
    public int Interval { get; init; } = 1000;
    public int MaxCount { get; init; } = 0;
}
```

## Example: Output Provider with Validation

```csharp
using Katasec.DStream.Abstractions;
using Katasec.DStream.SDK.Core;

await StdioProviderHost.RunProviderWithCommandAsync<MyOutputProvider, MyConfig>();

public class MyOutputProvider : ProviderBase<MyConfig>, IOutputProvider, IValidatable
{
    public Task<string?> ValidateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Config.ConnectionString))
            return Task.FromResult<string?>("connectionString is required");
        return Task.FromResult<string?>(null);
    }

    public async Task WriteAsync(IEnumerable<Envelope> batch, IPluginContext ctx, CancellationToken ct)
    {
        foreach (var envelope in batch)
        {
            // Write to your destination
        }
    }
}

public sealed record MyConfig
{
    public string ConnectionString { get; init; } = "";
}
```

---

## Running Providers

Providers are standalone binaries that communicate via stdin/stdout:

```bash
# Test input provider directly (command envelope format):
echo '{"command":"run","config":{"interval": 500, "maxCount": 3}}' | ./counter-input-provider

# Test output provider directly:
echo '{"command":"run","config":{"outputFormat": "simple"}}' | ./console-output-provider

# Test full pipeline manually:
echo '{"command":"run","config":{"interval":500,"maxCount":3}}' | ./counter-input-provider 2>/dev/null \
  | tail -n +2 \
  | while IFS= read -r line; do echo "$line"; done \
  | (echo '{"command":"run","config":{"outputFormat":"simple"}}' && cat) \
  | ./console-output-provider
```

Note: The first line of stdout from each provider is the handshake signal (`{"status":"ready"}`), not data. Use `tail -n +2` to skip it when piping manually.

---

## Task Configuration (HCL)

Your providers can be orchestrated by DStream CLI using `dstream.hcl`:

```hcl
task "counter-to-console" {
  type = "providers"

  input {
    provider_path = "./counter-input-provider"
    config {
      interval = 1000
      maxCount = 10
    }
  }

  output {
    provider_path = "./console-output-provider"
    config {
      outputFormat = "simple"
    }
  }
}
```

---

## SDK Architecture

The DStream .NET SDK uses a simple stdin/stdout architecture:

- **`Katasec.DStream.Abstractions`** - Core interfaces (`IInputProvider`, `IOutputProvider`, `IValidatable`, `Envelope`)
- **`Katasec.DStream.SDK.Core`** - Base classes (`ProviderBase<TConfig>`) and `StdioProviderHost`

### Provider Development Flow

1. **Reference SDK**: Add project references to `Abstractions` and `SDK.Core`
2. **Define Config**: Record class for your provider settings
3. **Implement Provider**: Inherit from `ProviderBase<TConfig>` and implement `IInputProvider` or `IOutputProvider`
4. **Add Validation** (optional): Implement `IValidatable` for startup config validation
5. **Bootstrap**: Call `StdioProviderHost.RunProviderWithCommandAsync<TProvider, TConfig>()`

### Architecture Benefits

- **Unix Pipeline Philosophy**: Input providers generate streams, output providers consume them, JSON over stdin/stdout
- **Process Model**: One binary per provider — independent, scalable, fault-isolated
- **Startup Safety**: Handshake protocol ensures the CLI knows if a provider failed before data flows
- **Language Agnostic**: The protocol is just JSON lines over stdin/stdout — any language can implement a provider without this SDK
- **Developer Experience**: Write business logic, SDK handles plumbing, handshake, command routing, and lifecycle

## Getting Started

See the example providers:
- [Counter Input Provider](https://github.com/katasec/dstream-counter-input-provider)
- [Console Output Provider](https://github.com/katasec/dstream-console-output-provider)
