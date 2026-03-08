# LSP Server Direction

## Overview

This note evaluates whether adding an LSP surface is a natural fit for Parlance and, if so, which direction is justified.

## Fit Assessment

Adding an LSP surface is a natural fit for Parlance, but not as the primary product surface yet.

Why it fits:

- Parlance already lives in the Roslyn layer that editor integrations need: analyzers, code fixes, and workspace-driven analysis/fix pipelines.
- The existing CLI and analyzer projects already separate reusable analysis logic from host-specific plumbing.
- An LSP surface would complement the CLI by enabling interactive diagnostics, fixes, and rule explanations inside editors and agent tooling.

Why it should not be first:

- For normal editor diagnostics and quick fixes, the most natural next step is still the analyzer package.
- A bespoke language server would duplicate a large amount of host, protocol, and workspace behavior that already exists upstream.

## Recommendation

Recommended order:

1. Ship the analyzer package.
2. If LSP is still needed, build a thin integration around Microsoft's `roslyn-language-server`.
3. Only build a custom LSP server if Parlance needs protocol behavior that Roslyn's server cannot reasonably expose.

This means:

- `Parlance.CSharp.Analyzers` remains the source of truth for diagnostics and fixes.
- The LSP layer should be treated as transport and host orchestration, not as the place where analysis rules live.
- A fully custom server is the last resort, not the default plan.

## Open Source Reuse Decision

Use existing open source components. Do not build an LSP server from scratch by default.

### Best reuse path

Use Microsoft's `roslyn-language-server` as the default server foundation if Parlance decides to expose LSP.

Why:

- Roslyn now ships an official `roslyn-language-server` tool.
- It is the engine behind the official VS Code C# extension and C# Dev Kit.
- It supports extension assemblies via `--extension`.
- Its composition code loads those extension assemblies into MEF.
- Its host workspace includes analyzer loading support in the language server process.

This is the strongest alignment with Parlance's current architecture because Parlance already produces Roslyn analyzers and code fixes rather than a separate semantic engine.

### What not to do

- Do not fork Roslyn's language server.
- Do not treat Roslyn's server source tree as a small embeddable library.
- Do not write a raw JSON-RPC server unless there is a clear feature gap that justifies owning the full host.

## `csharp-ls` vs Microsoft Server

There are three similarly named things that are easy to conflate:

1. `razzmatazz/csharp-language-server`
   - Independent Roslyn-based C# language server
   - Distributed as the `csharp-ls` dotnet tool
2. `OmniSharp/csharp-language-server-protocol`
   - An LSP protocol library for .NET
   - Not itself a C# language server
3. `SofusA/csharp-language-server`
   - A wrapper around Microsoft's Roslyn language server
   - Now explicitly deprecated because Microsoft released official standalone support

### When `csharp-ls` can be the default

`csharp-ls` is defensible as the default when the goal is a standalone, editor-agnostic server with minimal client-side glue.

Reasons it can win in that scenario:

- It presents itself directly as a normal standalone LSP server.
- Editors can usually launch `csharp-ls` with less custom bootstrapping.
- It explicitly targets non-VS Code clients and has a simpler mental model for Neovim, Emacs, Helix, and similar environments.
- Its README positions it as a direct server, while Microsoft's README still says the Roslyn server is generally meant to be launched by editor clients and its command-line options may change.

### Why it should not be Parlance's default

For Parlance specifically, `csharp-ls` should not be the default foundation.

Reasons:

- Parlance's value is in Roslyn analyzers and code fixes, not in owning an alternate C# host.
- Microsoft's server is now official and already designed to load extension assemblies.
- Using the Microsoft server keeps Parlance closest to the same engine used by the official C# extension stack.
- Choosing `csharp-ls` would add another abstraction layer between Parlance and the primary Roslyn host instead of reducing risk.

### Practical conclusion

If Parlance wants:

- mainstream alignment with the official C# tooling stack: default to `roslyn-language-server`
- a fallback for editor ecosystems where a standalone server is easier to wire up: consider `csharp-ls`

That makes `csharp-ls` a compatibility option, not the strategic default.

## Decision

- LSP is justified as a complement to Parlance.
- A bespoke LSP server is not justified today.
- The default direction should be: analyzer package first, then a thin integration around Microsoft's `roslyn-language-server`.
- `csharp-ls` is useful to understand and may be a fallback for some clients, but it should not be the primary foundation for Parlance.

## Follow-up: Why `csharp-ls` Might Be the Default Elsewhere

There is some naming confusion here, so the comparison needs to be precise.

If someone says `csharp-lsp`, they may mean one of these:

1. `razzmatazz/csharp-language-server`
   - The independent Roslyn-based server
   - Installed as the `csharp-ls` dotnet tool
2. `OmniSharp/csharp-language-server-protocol`
   - An LSP protocol library for .NET
   - Not a C# language server by itself
3. `SofusA/csharp-language-server`
   - A wrapper around Microsoft's Roslyn language server
   - Now deprecated because Microsoft released official standalone support

### Why `csharp-ls` can be the default in some environments

`csharp-ls` can be the better default when the main goal is to support generic LSP clients with as little editor-specific glue as possible.

Reasons:

- It behaves like a direct standalone language server.
- Its README is explicitly aimed at editor-agnostic usage.
- It is easier to point simple clients such as Neovim, Helix, or Emacs at `csharp-ls` without additional bootstrapping.
- Microsoft's `roslyn-language-server` still documents itself as something typically launched by editor clients, and its CLI surface is described as subject to change.

So for a general-purpose "pick one C# LSP server for many editors" decision, `csharp-ls` is a credible default.

### Why that still should not be Parlance's default

For Parlance, the strategic default should still be Microsoft's server.

Reasons:

- Parlance's differentiator is Roslyn analyzers and code fixes, not a custom alternative C# host.
- Roslyn's official server now supports extension assemblies and analyzer loading in-process.
- The official VS Code C# tooling stack already launches that server and extends it.
- Staying on the Microsoft path keeps Parlance closer to the primary Roslyn host instead of inserting another compatibility layer.

That means the practical split is:

- if the priority is generic editor compatibility with minimal glue, `csharp-ls` may be the easier default
- if the priority is aligning Parlance with the official Roslyn ecosystem and extension model, `roslyn-language-server` is the better default

For Parlance, the second priority is the stronger one.

## Sources

- Roslyn language server README:
  https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/README.md
- Roslyn language server project:
  https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/Microsoft.CodeAnalysis.LanguageServer.csproj
- Roslyn extension assembly loading:
  https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/Services/ExtensionAssemblyManager.cs
- Roslyn export provider composition:
  https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/LanguageServerExportProviderBuilder.cs
- Roslyn analyzer loading in the language server:
  https://github.com/dotnet/roslyn/blob/main/src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/VSCodeAnalyzerLoaderProviderFactory.cs
- VS Code C# extension launching Roslyn language server:
  https://github.com/dotnet/vscode-csharp/blob/main/src/lsptoolshost/server/roslynLanguageServer.ts
- `csharp-ls` README:
  https://github.com/razzmatazz/csharp-language-server/blob/main/README.md
- `csharp-ls` releases:
  https://github.com/razzmatazz/csharp-language-server/releases
- OmniSharp LSP protocol library:
  https://github.com/OmniSharp/csharp-language-server-protocol
- Deprecated wrapper around Roslyn server:
  https://github.com/SofusA/csharp-language-server/blob/main/README.md
