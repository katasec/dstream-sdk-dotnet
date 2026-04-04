# AGENTS.md — dstream-dotnet-sdk

> .NET SDK for building DStream providers. Part of the [DStream ecosystem](https://github.com/katasec/dstream-mission-control).

## Role

`dstream-dotnet-sdk` is the **SDK for .NET provider authors**. It provides abstractions (`IInputProvider`, `IOutputProvider`) and a host runtime that handles stdin/stdout plumbing, lifecycle management, and the command envelope protocol. Provider authors depend on this SDK to build DStream-compatible binaries without writing boilerplate.

## Design Docs

- Architecture & protocol: [dstream/docs/design/design.md](https://github.com/katasec/dstream/blob/main/docs/design/design.md)
- Ecosystem inventory: [dstream-mission-control/docs/repository-inventory.md](https://github.com/katasec/dstream-mission-control/blob/main/docs/repository-inventory.md)

## Code Style (C#)

**Governing principle: Progressive Disclosure.** Code reveals intent in layers — what at the top, how one level deeper.

- **Outline-first**: The top 15–20 lines of any file or class must disclose intent and flow. If a reader must scroll to understand what a class does, refactor.
- **Small, composable methods**: Each method is one named step (~20–40 lines max). Callers read step names; drilling in reveals implementation.
- **Top-down method ordering**: Public entry points first, private helpers below. File reads like an outline.
- **Error handling**: Handle exceptions explicitly. No empty `catch { }`. Log at the boundary or propagate with context.
- **No deeply nested branching**: Max 2 levels. Use early returns.
- **Side effects isolated**: stdin/stdout, process lifecycle — in clearly named methods, not mixed with logic.
- **Zero warnings**: `dotnet build` must produce zero warnings.
- **Prefer explicit block syntax** over expression-bodied (`=>`) for multi-line method bodies.
- **No speculative abstractions**: Build for what the task requires. This is a public SDK — breaking changes require a major version bump.

## Behaviour Rules

- **Propose before editing**: Describe what you're about to change and why before modifying files.
- **Test every change**: Run `dotnet test` before considering work done.
- **Build before push**: `dotnet build` must succeed with zero errors and zero warnings.
- **Focus**: Only change what the task requires.
- **Breaking changes**: Any change to public interfaces (`IInputProvider`, `IOutputProvider`, envelope schema) requires explicit approval and a major version bump. Flag before implementing.

## SDK Contract

- `IInputProvider`: emits events to stdout as JSON envelopes
- `IOutputProvider`: consumes events from stdin as JSON envelopes
- Host runtime handles the command envelope handshake on startup
- CancellationToken must be passed through for graceful shutdown
- Do not change public interface signatures without a version bump

## Task Context

Tasks arrive via GitHub Actions `workflow_dispatch` from `katasec/dstream-mission-control`. The issue body is your primary context. Read `## Task`, `## Context`, and `## Acceptance criteria` carefully before writing any code.
