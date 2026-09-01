# Agent instructions

## Multi-agent workflow

For non-trivial engineering tasks, act primarily as the orchestrator.

Delegate independent work to subagents whenever doing so can reduce context
pollution or parallelize the task.

Prefer subagents for:
- codebase exploration
- locating relevant files and execution paths
- independent implementation tasks
- writing or running tests
- debugging
- reviewing changes
- documentation/API research

Use multiple subagents concurrently when their work is independent.

Do not override the configured default subagent model unless explicitly asked
to do so. The main agent should retain responsibility for:
- understanding the user's intent
- decomposing the task
- assigning bounded tasks
- coordinating agents
- resolving conflicting findings
- reviewing important changes
- integrating the final result
- giving the final answer

Wait for relevant subagents before concluding the task.

Avoid having multiple agents modify the same files concurrently unless there
is a clear reason to do so.

## Manual Warcraft III verification ownership

- The user performs all manual verification inside Warcraft III. Do not use
  Windows UI automation or other interactive control to verify the lobby, map
  load, camera, selection flow, combat, or gameplay behavior.
- The agent may perform static checks, source validation, transaction/build
  checks, archive reinspection, and other noninteractive MCP verification, but
  must stop at the runtime artifact handoff and clearly label runtime behavior
  as unverified until the user reports it.
- Do not infer successful gameplay from process start, editor launch, map load
  intent, static inspection, or build success. Record runtime evidence only
  from explicit user observations.

## Finish every task with Git

- Treat the repository as a shared working tree. Preserve unrelated user changes and inspect `git status` before editing.
- When the requested work is complete, verify the relevant files and tests, then stage only the intended changes.
- Create a concise Git commit describing the completed work.
- Push the commit to the configured upstream branch before reporting completion.
- If no remote, credentials, or upstream branch is available, do not claim the push happened. Report the exact blocker and leave the local commit ready to push.
- After committing and pushing, verify the final status and the pushed commit/upstream when possible.

## Warcraft III project scope

- Keep editable project work under this project root; do not modify installed-game files unless explicitly requested.
- Preserve the approved MVP scope: four users, two teams of two, and two mirrored arenas.
- Keep design rules consistent across the design documents, especially the current round-robin routing and shared team-life rules.
- Treat binary map files as valuable user work: make a recoverable copy before risky edits and report whether runtime testing was actually performed.

## MCP rebuild and runtime debugging notes

When rebuilding MCP-owned maps or diagnosing Warcraft III compile/load failures, read
[tools/wc3-map-mcp/docs/troubleshooting/v8-custom-hero-runtime-lessons.md](tools/wc3-map-mcp/docs/troubleshooting/v8-custom-hero-runtime-lessons.md).
Use jassdoc for native/API facts. That note records process lessons only; the v8 load crash remains unresolved.
