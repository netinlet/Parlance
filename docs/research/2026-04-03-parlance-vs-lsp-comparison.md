<!-- generated from 20-Projects/Parlance/Research/2026-04-03-parlance-vs-lsp-comparison.md — edit in vault, run tools/docs/publish.sh -->

# Parlance MCP Tools vs LSP Specification

**Date:** 2026-04-03
**Purpose:** Compare Parlance's MCP tool surface against the Language Server Protocol (3.17) to understand overlap, gaps, and design differences.

---

## Tool Inventory

### Parlance MCP Tools (12 total)

| Tool | Parameters | Returns |
|---|---|---|
| **workspace-status** | (none) | Workspace health, loaded projects, target frameworks, language versions, project dependencies |
| **describe-type** | `typeName` | Members, base types, interfaces, accessibility, file location |
| **find-implementations** | `typeName` | Types that implement/inherit from the given interface or class |
| **find-references** | `symbolName` | All references grouped by file, with line numbers and snippets |
| **get-type-at** | `filePath`, `line`, `column` | Resolved type at position (especially useful for `var` declarations) |
| **outline-file** | `filePath` | Structural outline: types, members, signatures, without method bodies |
| **get-symbol-docs** | `symbolName` | Structured XML docs: summary, params, returns, remarks, exceptions. Resolves `inheritdoc`. |
| **call-hierarchy** | `methodName` | Callers (incoming) and callees (outgoing), one level deep |
| **get-type-dependencies** | `typeName` | What a type depends on and what depends on it (fields, properties, parameters, inheritance, usage) |
| **safe-to-delete** | `symbolName` | Whether a symbol has zero references (safe=true/false), sample locations |
| **decompile-type** | `typeName` | Decompiled source of an external/metadata type |
| **analyze** | `files[]`, `curationSet?`, `maxDiagnostics?` | Diagnostics with severity, fix classification, rationale, and quality score |

### LSP Code Intelligence Methods (18 total)

| Method | Parameters | Returns |
|---|---|---|
| **textDocument/definition** | file + position | Location(s) where the symbol is defined |
| **textDocument/declaration** | file + position | Location(s) where the symbol is declared |
| **textDocument/typeDefinition** | file + position | Location of the type definition (e.g., variable → its class) |
| **textDocument/implementation** | file + position | Location(s) of implementations |
| **textDocument/references** | file + position + includeDeclaration | Location(s) of all references |
| **textDocument/hover** | file + position | Rendered type info + documentation (MarkupContent) |
| **textDocument/signatureHelp** | file + position | Active signature, parameter info for a method call |
| **textDocument/documentSymbol** | file | Hierarchical symbol tree (DocumentSymbol[]) or flat list (SymbolInformation[]) |
| **workspace/symbol** | query string | Symbols matching the query across the workspace |
| **textDocument/prepareCallHierarchy** | file + position | CallHierarchyItem(s) at position |
| **callHierarchy/incomingCalls** | CallHierarchyItem | Callers of the method |
| **callHierarchy/outgoingCalls** | CallHierarchyItem | Callees from the method |
| **textDocument/prepareTypeHierarchy** | file + position | TypeHierarchyItem(s) at position |
| **typeHierarchy/supertypes** | TypeHierarchyItem | Parent/base types |
| **typeHierarchy/subtypes** | TypeHierarchyItem | Child/derived types |
| **textDocument/publishDiagnostics** | (push notification) | Diagnostics for a file |
| **textDocument/diagnostic** | file | Diagnostics for a file (pull model) |
| **workspace/diagnostic** | previous result IDs | Diagnostics for entire workspace (pull model) |
| **textDocument/codeAction** | file + range + diagnostics | Available fixes and refactorings |
| **textDocument/codeLens** | file | Inline actionable annotations |

---

## Direct Overlaps

### find-references ↔ textDocument/references

Both find all references to a symbol across the workspace. LSP takes `(file, position)` and returns `Location[]`. Parlance takes `symbolName` and returns references grouped by file with line numbers and source snippets.

### find-implementations ↔ textDocument/implementation

Both find types implementing an interface or inheriting from a class. LSP takes `(file, position)` and returns `Location[]`. Parlance takes `typeName` and returns structured `ImplementationEntry[]` with name, kind, file, and line.

### outline-file ↔ textDocument/documentSymbol

Very close. Both return the structural outline of a file. LSP returns `DocumentSymbol[]` with kind, range, and children. Parlance returns `OutlineType[]` with members, signatures, accessibility, and line numbers. Parlance omits method bodies; LSP includes the full range span.

### call-hierarchy ↔ callHierarchy/*

Same goal. LSP uses a 3-step protocol: prepare (resolve symbol at position) → incomingCalls → outgoingCalls, each as separate requests. Parlance collapses this into a single `call-hierarchy` call that takes a method name and returns both callers and callees in one response.

### analyze ↔ textDocument/diagnostic + workspace/diagnostic

Both return diagnostics. LSP returns `Diagnostic[]` with code, severity, range, message, tags, and related information. Parlance adds `FixClassification`, `Rationale`, `CurationSet`, and a computed `Score` — the "opinionated" layer on top of raw diagnostics.

### get-type-at ↔ textDocument/hover + textDocument/typeDefinition

Partial overlap. All use file + position. LSP hover returns rendered docs and type info as markdown. LSP typeDefinition navigates to where the type is defined. Parlance's `get-type-at` focuses specifically on resolving the concrete type (especially for `var` declarations) and returns structured type metadata.

### get-symbol-docs ↔ textDocument/hover

Partial overlap. LSP hover bundles type info and documentation into a single rendered response. Parlance separates documentation into a dedicated tool with structured fields (summary, params, returns, remarks, exceptions) and resolves `inheritdoc` chains.

### describe-type ↔ textDocument/hover + typeHierarchy/*

Partial overlap. LSP hover shows type info at a position. LSP type hierarchy navigates supertypes/subtypes as a multi-step protocol. Parlance combines members, base types, interfaces, accessibility, sealed/abstract/static flags, and file location into a single name-based call.

---

## Parlance Tools with No LSP Equivalent

| Tool | What it does | Why it's unique |
|---|---|---|
| **workspace-status** | Returns workspace health, project count, load state, target frameworks, dependencies | LSP has `initialize`/`initialized` for handshake but no ongoing health query. An agent needs to know if the workspace is ready before making other calls. |
| **decompile-type** | Decompiles external/metadata types into readable C# source | Not in LSP. Lets an agent inspect dependency internals without source code. |
| **safe-to-delete** | Checks if a symbol has zero references | Higher-level analysis. LSP gives raw references; Parlance answers "can I delete this?" directly with a boolean + sample locations. |
| **get-type-dependencies** | Maps what a type depends on and what depends on it, with relationship types | LSP type hierarchy is inheritance-only (supertypes/subtypes). Parlance maps usage-based relationships: fields, properties, parameters, method returns — in both directions. |

---

## LSP Methods with No Parlance Equivalent

### High Value for Agents

**textDocument/definition** — Go-to-definition. An agent reads code, sees a method call or type reference, and needs to jump to where it's defined. Currently the agent has to `Grep` for it or use `describe-type` and hope the `FilePath` + `Line` in the result gets them there. A dedicated tool would be more direct and handle overloads and partial classes correctly.

**workspace/symbol** — Fuzzy search for symbols by name across the workspace. Parlance tools require you to already know the exact name (or get an ambiguous candidates list). A search/query tool would let the agent discover symbols it doesn't know about yet — "find me anything matching `*Handler*`".

### Medium Value for Agents

**textDocument/codeAction** — Returns available fixes and refactorings for a diagnostic at a location. Parlance's `analyze` tells you what's wrong; code actions would tell you what automated fixes are available. Relevant since Parlance already tracks `FixClassification`.

**typeHierarchy/*** — Dedicated up/down walk of the inheritance tree. `describe-type` returns base types and interfaces, and `get-type-dependencies` covers usage-based relationships. But neither provides the recursive navigate-up-then-down workflow that LSP's 3-step type hierarchy enables. Partially covered but not equivalent.

### Low Value for Agents

**textDocument/completion** — Autocomplete suggestions. An AI agent writes whole lines and blocks, not character-by-character. Hard to imagine a scenario where an agent needs completion suggestions.

**textDocument/signatureHelp** — Parameter hints during method calls. Interactive typing aid. An agent can get this information from `describe-type` or `get-symbol-docs`.

**textDocument/declaration** — Distinct from definition in C/C++ (header vs implementation). In C# they're the same thing, so `definition` would cover it.

**textDocument/codeLens** — Visual inline annotations like "3 references" or "Run test". A UI affordance, not a query an agent would make.

---

## Fundamental Design Difference: Position-Based vs Name-Based

**LSP is position-based.** Every query starts with `(file, line, column)` and the server resolves which symbol is at that position. This assumes an IDE with a cursor.

**Parlance is name-based.** Most tools take a symbol or type name and resolve it across the workspace. Only `get-type-at`, `outline-file`, and `analyze` take file paths. This assumes an AI agent that knows names from reading code but doesn't maintain cursor state.

The name-based approach also means Parlance tools handle **ambiguity explicitly** — returning `Candidates` lists (with display name, fully qualified name, kind, project, file, line) when multiple symbols match a query. LSP doesn't need this because the position uniquely identifies the symbol.

| Aspect | LSP | Parlance |
|---|---|---|
| Primary input | File + position | Symbol name |
| Symbol resolution | Implicit (server resolves at cursor) | Explicit (name match, with disambiguation) |
| Ambiguity handling | Not needed (position is unique) | `Candidates` list on every tool |
| File-based tools | All tools take a file URI | Only `get-type-at`, `outline-file`, `analyze` |
| Designed for | IDE with cursor context | AI agent with name knowledge |

---

## Naming Conventions

### Parlance: Flat kebab-case

```
find-references
call-hierarchy
describe-type
workspace-status
```

No prefix encodes scope. You have to read the description to know whether a tool operates on a file, a symbol name, or the whole workspace.

### LSP: Hierarchical namespace/method

```
textDocument/references
callHierarchy/incomingCalls
workspace/symbol
typeHierarchy/subtypes
```

The prefix encodes scope:
- `textDocument/*` — operates on a single file
- `workspace/*` — operates across the workspace
- `callHierarchy/*` — sub-protocol for call hierarchy
- `typeHierarchy/*` — sub-protocol for type hierarchy

The suffix uses `camelCase`. The hierarchy drives capability negotiation — a server advertises `textDocumentProvider.implementationProvider = true`.

### Comparison

LSP's hierarchy works well for a formal protocol with 50+ methods and capability negotiation between client and server. Parlance's flat naming works well for MCP, where tool discovery is a flat list with descriptions — the agent reads the description, not the name hierarchy.

If the tool count grows significantly (25+), a light prefix convention could help discoverability without going full LSP:

```
workspace-status
file-outline, file-analyze, file-type-at
symbol-references, symbol-docs, symbol-describe
```

At 12 tools, flat names are fine.

---

## LSP Total Method Count vs Code Intelligence

LSP 3.17 has ~50+ methods total, but most are protocol plumbing unrelated to code intelligence:

| Category | ~Count | Purpose |
|---|---|---|
| **Code intelligence** | **~18** | Navigation, symbols, hierarchies, diagnostics, actions |
| Document sync | ~7 | didOpen, didClose, didChange, didSave, willSave |
| File lifecycle | ~6 | willCreate/didCreate/willRename/didRename/willDelete/didDelete |
| Formatting | ~3 | formatting, rangeFormatting, onTypeFormatting |
| UI/window | ~5 | showMessage, logMessage, showDocument, progress |
| Protocol lifecycle | ~4 | initialize, initialized, shutdown, exit |
| Semantic tokens | ~3 | Syntax highlighting data (full, delta, range) |
| Editor features | ~6 | foldingRange, selectionRange, documentHighlight, rename, inlayHint, linkedEditingRange |
| Notebook | ~4 | Notebook document sync |

The meaningful comparison is **18 LSP code intelligence methods vs 12 Parlance tools**. Parlance covers most of the code intelligence surface and adds 4 tools that LSP doesn't have at all.

---

## Discovery and Activation Models

### How LSP Servers Get Activated

LSP servers are activated **automatically by file type**. The editor's LSP client maintains a registry mapping file extensions to language server commands. Open a `.cs` file → the client spawns the language server → diagnostics/navigation just work. Zero configuration per project, zero thought from the developer.

For Claude Code specifically, this registry is the **plugin system** (`anthropics/claude-plugins-official`). The `csharp-lsp` plugin entry in `marketplace.json` is:

```json
{
  "name": "csharp-lsp",
  "strict": false,
  "lspServers": {
    "csharp-ls": {
      "command": "csharp-ls",
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
```

That's the entire plugin definition. `extensionToLanguage` is the trigger — Claude Code sees a `.cs` file, spawns `csharp-ls` over stdio, and LSP features activate. The plugin directory itself contains only a README and LICENSE; all configuration lives in the marketplace registry with `strict: false` (meaning the marketplace entry IS the plugin, no `plugin.json` needed).

This plugin points at `razzmatazz/csharp-ls`, an F#-based LSP server built directly on Roslyn (not OmniSharp). It implements 28 LSP capabilities including diagnostics, go-to-definition, find-references, call hierarchy, type hierarchy, code actions, rename, and decompilation via ICSharpCode.Decompiler. There is an open bug (anthropics/claude-code#16360) that Claude Code's LSP client is missing handlers for some standard LSP requests that `csharp-ls` needs.

### How MCP Servers Get Activated

MCP servers are activated **by explicit configuration** — the user adds the server to their Claude Code settings (or project `.mcp.json`). There is no file-type trigger. The tools appear in the agent's available tool list with descriptions, and the agent decides whether to call them.

In practice, the agent might `Grep` for a symbol instead of calling `find-references`, because `Grep` is a built-in habit. The MCP tools are optional — the agent always has the choice to ignore them.

**Nudge mechanisms:**

| Mechanism | How it works | Scope |
|---|---|---|
| **CLAUDE.md** | Instructions like "use Parlance MCP tools for C# navigation" | Per-repo (checked in) or global (`~/.claude/CLAUDE.md`) |
| **.github/copilot-instructions.md** | Same concept for GitHub Copilot | Per-repo |
| **AGENTS.md** | Same concept for OpenAI Codex | Per-repo |
| **GEMINI.md** | Same concept for Gemini CLI | Per-repo |
| **MCP server instructions** | The MCP spec has an `instructions` field in the server info response, read by the client on connect | Travels with the server, no file needed |

The MCP `instructions` field is the cleanest option — the guidance travels with the server, not the repo. Every project that configures Parlance as an MCP server gets the nudge automatically without touching any files.

### The Gap

LSP activation is **automatic and invisible**. MCP activation is **manual and requires nudging**. An LSP server integrated into the editor's plugin system "just works" for every C# project. An MCP server requires per-project configuration and agent instructions to be preferred over built-in alternatives like Grep.

---

## The Case for Parlance as Both MCP and LSP

### Two Interfaces, Two Audiences

Parlance currently ships as an MCP server — name-based tools designed for AI agents. But the Roslyn workspace engine underneath could also expose an LSP interface. These aren't competing — they serve different consumers with different activation models.

**MCP (agent-facing):** Name-based, opinionated, higher-level. Tools like `safe-to-delete`, `get-type-dependencies`, `analyze` with fix classification and scoring. Designed for an AI agent that reasons about code at the symbol level. Requires explicit configuration but delivers capabilities no LSP server provides.

**LSP (IDE-facing):** Position-based, standard protocol, automatic activation. Diagnostics after every edit, hover info, go-to-definition, code actions. Designed for a human developer (or an AI agent operating within an IDE plugin system). Activates automatically via file-type association.

### What LSP Would Enable

**Automatic activation in AI coding tools.** Claude Code's plugin system, and likely similar systems in Copilot and Codex, use `lspServers` entries to auto-activate by file extension. An LSP interface would let Parlance activate for every `.cs` file without any CLAUDE.md nudge or MCP configuration. The agent gets diagnostics pushed after every edit, automatically.

**Developer education in IDEs.** LSP's position-based interface maps directly to IDE features that developers interact with every day — hover tooltips, squiggly underlines, lightbulb code actions, code lenses. This is where Parlance's opinionated layer becomes an education tool:

- **Hover:** Instead of just showing type info, show Parlance's rationale for why a pattern is idiomatic or not
- **Diagnostics:** Push Parlance's curated diagnostics with fix classification directly into the editor's problems panel — developers see them as they type
- **Code actions:** Surface Parlance's recommended fixes as lightbulb quick-fixes — "Parlance suggests: use pattern matching here" with a one-click apply
- **Code lenses:** Inline annotations like "Parlance score: 85" or "3 idiomatic improvements available" above classes/methods
- **Inlay hints:** Show inferred types, but also Parlance-specific hints like "this dependency could be removed"

This turns Parlance from a tool the developer has to invoke into **ambient guidance that teaches as they code**. The developer doesn't ask "is this idiomatic?" — they see it in real time, in the same place they already look for compiler errors.

**The SonarQube-class functionality that falls out as a side-effect** (per the product vision) becomes visible to developers directly in their editors, not just in CI reports or agent conversations.

### Architecture

The MCP and LSP interfaces would share the same Roslyn workspace engine:

```
Parlance.CSharp.Workspace      (MSBuildWorkspace, compilations, semantic model)
    ↑                    ↑
Parlance.Mcp             Parlance.Lsp
(name-based,             (position-based,
 agent-oriented,          IDE-oriented,
 stdio MCP transport)     stdio LSP transport)
```

The workspace is the expensive part — loading MSBuild projects, maintaining compilations, tracking file changes. Both interfaces are thin projections over it. Most of the heavy lifting (find references, resolve types, run analyzers) already exists in the workspace layer. The LSP interface would primarily be:

- A position-to-symbol resolver (convert file+line+column to the symbol name the MCP tools already work with)
- LSP protocol serialization (JSON-RPC, LSP message types)
- Document sync handlers (didOpen/didChange/didClose to keep the workspace updated)
- Push diagnostics (run analyzers on change, notify the client)

### What Each Interface Does Best

| Capability | MCP | LSP | Notes |
|---|---|---|---|
| Diagnostics with rationale | analyze tool | textDocument/diagnostic | MCP adds fix classification, scoring, curation sets |
| Go-to-definition | (gap) | textDocument/definition | LSP fills the MCP gap |
| Find references | find-references (by name) | textDocument/references (by position) | Both useful, different entry points |
| Safe-to-delete analysis | safe-to-delete | (no equivalent) | MCP-only, higher-level |
| Type dependency graph | get-type-dependencies | (no equivalent) | MCP-only, usage-based |
| Decompile externals | decompile-type | csharp/metadata (custom) | Both possible |
| Hover education | (not applicable) | textDocument/hover | LSP-only, visual |
| Code action fixes | (gap) | textDocument/codeAction | LSP fills the MCP gap |
| Workspace symbol search | (gap) | workspace/symbol | LSP fills the MCP gap |
| Automatic activation | Needs config + nudge | File-type trigger | LSP wins on discoverability |
| Ambient developer education | Not applicable | Hover, diagnostics, lenses | LSP-only, teaches as you code |

### Strategic Observation

The MCP interface is the **"IDE for the AI agent"** — the core product vision. The LSP interface would be the **"senior developer looking over your shoulder"** — the education and guidance layer that reaches developers directly in their editors.

These aren't competing products. The LSP interface makes Parlance visible to the much larger audience of human developers using IDEs. It creates a natural pipeline: developers who see Parlance's opinionated guidance in their editor are the same developers who will want Parlance available to their AI agents. The LSP interface is the on-ramp; the MCP interface is the destination.

And because the existing `csharp-lsp` plugin in Claude Code's marketplace has known issues (missing LSP handler support), there's an opening — a Parlance LSP server that works correctly with Claude Code's plugin system would be immediately useful, before even considering the education angle.

---

## Reference: razzmatazz/csharp-language-server (csharp-ls)

The existing C# language server used by Claude Code's `csharp-lsp` plugin. Relevant as both a comparison point and a potential competitor.

**Architecture:** Written in F# (98.9%), uses Roslyn directly (not OmniSharp), custom JSON-RPC 2.0 transport over stdio. Uses ICSharpCode.Decompiler for metadata navigation.

**Implemented capabilities (28):** Completion, hover, signature help, go-to-definition, go-to-type-definition, go-to-implementation, find references, document highlight, document symbol (hierarchical), code action (Roslyn refactorings + code fixes), code lens (reference counts), formatting (document, range, on-type), rename (with file rename), folding range, semantic tokens, call hierarchy (incoming only — outgoing is a stub), type hierarchy, inlay hints, diagnostics (pull model, document + workspace), workspace symbol search, decompilation (custom `csharp/metadata` extension), `$/setTrace` and `$/logTrace` (logging bridge from Microsoft.Extensions.Logging to LSP trace notifications).

**Stubs (not implemented):** Declaration, document color, document link, inline value, moniker, linked editing range, selection range, execute command, semantic tokens delta, call hierarchy outgoing calls.

**Known issues with Claude Code:** Open bug (anthropics/claude-code#16360) — Claude Code's LSP client is missing handlers for standard LSP requests that `csharp-ls` requires (e.g., `workspace/configuration`).

---

## Summary

- **6 tools have clear LSP counterparts** (find-references, find-implementations, outline-file, call-hierarchy, analyze, get-type-at), differentiated by name-based input and richer return types.
- **2 tools partially overlap** with LSP hover (get-symbol-docs, describe-type) but decompose what LSP bundles into one response.
- **4 tools are unique to Parlance** (workspace-status, decompile-type, safe-to-delete, get-type-dependencies) — the higher-level analysis layer.
- **2 LSP methods are high-value gaps**: go-to-definition and workspace symbol search.
- **2 LSP methods are medium-value gaps**: code actions and type hierarchy.
- **4 LSP methods are low-value for agents**: completion, signature help, declaration, code lens.
