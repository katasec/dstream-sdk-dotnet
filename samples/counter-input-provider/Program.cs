using Katasec.DStream.SDK.Core;
using CounterInputProvider;

// Counter Input Provider - generates sequential numbers with timestamps
// Reads JSON config from stdin, outputs JSON envelopes to stdout
// Perfect for testing pipelines and demonstrating DStream input providers

// Top-level program entry point
await StdioProviderHost.RunProviderWithCommandAsync<CounterInputProvider.CounterInputProvider, CounterInputProvider.CounterConfig>();


