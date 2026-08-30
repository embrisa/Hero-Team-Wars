# Agent instructions

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
