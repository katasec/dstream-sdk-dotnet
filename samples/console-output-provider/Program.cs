using Katasec.DStream.SDK.Core;
using ConsoleOutputProvider;

// Top-level program entry point
await StdioProviderHost.RunProviderWithCommandAsync<OutputProvider, ConsoleConfig>();
