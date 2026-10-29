# phd-sync mirror (Plant_Lab)

Laura's PhD project keeps its sessions in sync through a hub and logs in a claude.ai Project.
Claude Code cannot read that Project, so this folder is the bridge:

- `plant_lab_inbox.md`: written by the claude.ai sessions at every sync. Current paradigm decisions
  and all open items addressed to Plant_Lab. Read it at the start of a session. Do not edit it.
- `plant_lab_syncout.md`: written by Claude Code. Add each new dated entry (2 to 4 lines: decisions,
  findings, pitfalls + fixes, changed files, open questions, next steps) at the top of the section
  "New entries, not yet transferred", and keep the "Current state" block (max ~10 lines) current.
  At the next sync a claude.ai session copies the not-yet-transferred entries into the Plant_Lab log,
  moves them into "Already transferred" and updates the header date. Never add entries to
  "Already transferred".

Both files are copies: they are only as current as the last sync.
