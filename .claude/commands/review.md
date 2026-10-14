---
description: Review changed code for correctness, clarity, simplicity, and alignment with an OpenSpec change.
argument-hint: "[change-name, file, or review scope]"
---

# Review

Adapted from [Owain Lewis's review command](https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/resources/commands/review.md).

## Target

$ARGUMENTS

If no target is specified, review all changed files, including staged, unstaged,
and relevant untracked files. Use Git status and diffs to establish the scope.

## OpenSpec context

For a named change, run `openspec status --change "<name>" --json` and read the
existing artifacts at the reported paths. Otherwise run `openspec list --json`;
use an unambiguous active change or review code without claiming spec alignment.
Preserve an explicitly selected store by passing `--store "<id>"` on lookups.

Compare the implementation with the proposal's scope, specs' scenarios,
design constraints, and completed tasks. Check verification evidence and
identify missing coverage. Follow applicable AGENTS.md and CLAUDE.md instructions.

## Review criteria

- Simplicity: unnecessary abstractions or complexity.
- Clarity: descriptive names and understandable control flow.
- Consistency: established project patterns and language conventions.
- Correctness: bugs, edge cases, regressions, and unmet requirements.

## Output

Report substantive findings first, with severity, file and line, impact, and a
concrete suggested fix or code snippet. State what was verified and any gaps.
If no issues are found, say so briefly. Keep the review constructive and focused.

Review only: do not edit files, mark tasks complete, stage, or commit unless the
user separately requests that action.
