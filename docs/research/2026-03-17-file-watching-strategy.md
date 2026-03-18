# File Watching Strategy Research

**Date:** 2026-03-17
**Context:** Issue #59 — file watcher reacts to bin/obj changes during builds
**Status:** Immediate fix applied (bin/obj filtering). This doc captures architectural options for future consideration.

## Background

The MCP server uses `FileSystemWatcher` with `IncludeSubdirectories = true` to detect source file changes. During `dotnet clean` + `dotnet build`, generated `.cs` files in `obj/` directories (e.g., `AssemblyInfo.cs`, `GlobalUsings.g.cs`) triggered the watcher, causing SnapshotVersion to climb past 235.

### Root Cause (corrected from issue description)

The issue description mentions "DLLs, `.deps.json`, PDBs" — but the `FileSystemWatcher` filter is `"*.cs"`, so those never triggered events. The actual culprits were **generated `.cs` files in `obj/`** that MSBuildWorkspace includes as Documents.

### Fix Applied

Two-layer filtering:
1. **Layer 1 (session):** `documentPaths` passed to the watcher now excludes paths containing `/bin/` or `/obj/` segments
2. **Layer 2 (watcher):** `OnFileChanged` has an early-out `IsBuildOutputPath` check before the `_watchedFiles` HashSet lookup

## inotify Overhead

On Linux, `FileSystemWatcher` uses inotify. Each watched directory consumes one inotify watch descriptor (~1,500 bytes kernel memory).

With `IncludeSubdirectories = true` on 15 project directories: **~513 inotify watches**, of which ~473 (92%) are on bin/obj directories that serve no purpose.

### Shared resource contention

inotify watches are per-user, shared across all processes:

| Program | Typical watch consumption |
|---------|--------------------------|
| VS Code (medium workspace) | ~10,000 |
| VS Code + node_modules | 50,000–100,000+ |
| Rider / IntelliJ | thousands |
| `dotnet watch` | hundreds |
| Parlance MCP (current, with fix) | ~40 (source dirs only) |

Default `max_user_watches` on modern kernels: 1,048,576. When exhausted: `ENOSPC` — silent loss of file watching with no recovery.

### Why we didn't switch to non-recursive watchers

Non-recursive watchers (one per source directory) would reduce inotify consumption from ~513 to ~28, but **miss newly added `.cs` files in new subdirectories** unless the watcher set is rebuilt on project structure changes. The current recursive approach with filtering is simpler and handles new files automatically.

## How LSP and MCP Differ on File Watching

### LSP: client owns watching

The LSP protocol has `workspace/didChangeWatchedFiles` — the server sends glob patterns to the client via `client/registerCapability`, and the client (e.g., VS Code) manages OS-level file watching. Roslyn's language server creates **zero** inotify watches of its own.

### MCP: server must watch itself

MCP has no equivalent mechanism. MCP's `resources/subscribe` goes the other direction — the client subscribes to the server, and the **server is responsible for detecting changes**. There is no way to ask the MCP client (e.g., Claude Code) to watch files on the server's behalf.

This means every MCP server that needs file-change awareness must implement its own file watching, independently consuming inotify resources.

## Architectural Options Considered

### Option A: Server-side watching with filtering (implemented)

What we did. Keep `IncludeSubdirectories = true`, filter out bin/obj paths. The ~473 wasted inotify watches on bin/obj remain but are not practically harmful at this scale.

### Option B: On-demand / lazy refresh

Don't watch files at all. When a tool is invoked, compare in-memory state to disk (the existing `RefreshAsync` logic). Zero inotify cost. Trade-off: slight latency on first tool call after changes, no proactive SnapshotVersion updates.

### Option C: Watch-only with deferred refresh (recommended future direction)

The current watcher does two things on every file change:
1. Reads the changed file from disk
2. Updates the Roslyn solution and calls `TryApplyChanges`

This is expensive work triggered by filesystem events. Even with perfect bin/obj filtering, a developer running a code formatter that touches 50 source files triggers 50 disk reads + solution mutations during the debounce callback.

**Option C separates notification from refresh:**
- File watcher bumps SnapshotVersion (cheap dirty flag) but does NOT read files or update the Roslyn workspace
- When a tool is invoked and sees a stale SnapshotVersion, it triggers the actual refresh
- The Roslyn workspace update happens once per tool call, not once per debounce window

This is how Roslyn itself works internally — track that something changed, defer the actual work until someone needs the result.

**Benefits:**
- Build storms (formatter, refactoring, clean+build) produce exactly one Roslyn refresh — when the next tool call happens
- SnapshotVersion still updates promptly for cache invalidation
- No wasted work if no tool calls happen between changes

**Trade-offs:**
- First tool call after changes has higher latency (refresh + tool work)
- More complex state management (need to track "dirty but not yet refreshed")
- SnapshotVersion semantics change from "workspace is at this version" to "changes detected since last refresh"

**Implementation sketch:**
- Add a `_dirtyFlag` (or `_pendingRefreshVersion`) alongside `_snapshotVersion`
- File watcher callback: increment `_snapshotVersion`, set dirty flag, log, done
- Tool invocation path: check dirty flag → if dirty, call `RefreshAsync` → clear flag → proceed
- `RefreshAsync` already exists and does exactly the right thing

This would be a separate issue and a modest refactor of the session's change-detection flow.
