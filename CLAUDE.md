@AGENTS.md

## Notes for Claude Code

- Windows machine: the Bash tool is Git Bash and there is a PowerShell tool (Windows PowerShell 5.1, no
  `pwsh`). Run the repo's `.ps1` scripts from PowerShell. There is no `python`; `node` is available for
  small text-processing scripts.
- Parallel agents work in git worktrees under `.claude/worktrees/` (ignored by git). Brief them with the
  rules above, the files to read, and what must not change; merge their branches by cherry-picking onto the
  working branch, then build and run every test suite before committing the merge result.
- The owner tests in the real game and reports back. Keep replies to them short and concrete, end with a
  checklist of what to try in the game, and never report something as working that only a test with fakes
  has shown.
- Don't push, open pull requests, merge, tag or release unless the owner asked for it in this conversation.
