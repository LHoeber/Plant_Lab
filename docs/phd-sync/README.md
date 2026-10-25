# phd-sync mirror (Plant_Lab)

Laura's PhD project keeps its sessions in sync through a hub and logs in a claude.ai Project.
Claude Code cannot read that Project, so this folder is the bridge:

- `plant_lab_inbox.md`: written by the claude.ai sessions at every sync. Current paradigm decisions
  and all open items addressed to Plant_Lab. Read it at the start of a session. Do not edit it.
- `plant_lab_syncout.md`: written by Claude Code. When Laura says "sync out", add a dated entry at
  the top (2 to 4 lines: decisions, findings, pitfalls + fixes, changed files, open questions, next steps)
  and rewrite the "Current state" block (max ~10 lines). A claude.ai session copies it into the
  Plant_Lab log at the next sync and marks it as transferred.

Both files are copies: they are only as current as the last sync.
