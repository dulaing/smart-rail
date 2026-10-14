# SmartRail's OpenSpec workflow

OpenSpec saves the decisions an agent needs before implementation. A **change**
is a proposed unit of work; it lives in `openspec/changes/<change-name>/`.
The `spec-driven` schema splits the tutorial's single feature document into:

| Tutorial section | OpenSpec artifact | Purpose |
| --- | --- | --- |
| Why, What, Out of Scope | `proposal.md` | Motivation, scope, capabilities, and impact |
| Observable behavior and acceptance checks | `specs/<capability>/spec.md` | Delta requirements and scenarios |
| Constraints, Current State | `design.md` | Technical decisions and existing patterns |
| Tasks, Validation | `tasks.md` | Numbered checkbox tasks with verification |

`openspec/specs/` holds the current system contracts. Each change's `specs/`
holds proposed additions, modifications, or removals. Archiving reconciles those
deltas with the main specs and moves the change into `openspec/changes/archive/`.
Checking off tasks alone does not update the main specs.

## Typical loop

These are prompts for your coding assistant, not terminal commands:

| Step | Claude Code | Codex |
| --- | --- | --- |
| Explore an uncertain idea (optional) | `/opsx:explore <idea>` | `$openspec-explore <idea>` |
| Create the planning artifacts | `/opsx:propose <feature description>` | `$openspec-propose <feature description>` |
| Revise the plan after reading it | `/opsx:update <change-name> <revision>` | `$openspec-update-change <change-name> <revision>` |
| Implement | `/opsx:apply <change-name>` | `$openspec-apply-change <change-name>` |
| Review the implementation | `/review <change-name>` | `$review <change-name>` |
| Draft a message for staged work | `/commit` | `$commit` |
| Archive completed work | `/opsx:archive <change-name>` | `$openspec-archive-change <change-name>` |

Review the generated artifacts before requesting implementation. For the
tutorial's small-task loop, explicitly ask apply to implement only task `1.1`
(or another task id), verify it, and stop. Start a fresh session for the next task
when useful; the files carry the context forward.

Run `/review`, fix any findings, and repeat the relevant verification. Stage the
intended files with `git add <paths>`, then use `/commit` to draft the message.
Run `git commit` yourself or explicitly ask the assistant to create the commit.
The tutorial's commit command only generates a message; ours preserves that behavior.

`/opsx:sync` or `$openspec-sync-specs` can reconcile delta specs with the main specs
without archiving when you need that separately.

Useful terminal commands:

```powershell
openspec list
openspec status --change <change-name>
openspec validate <change-name> --strict
```

Validation checks artifact structure and consistency; run the task's verification
commands to check the implementation itself.

## What was customized

- `config.yaml` retains `schema: spec-driven` and adds planning rules plus
  advisory apply/archive guidance. It does not register new commands or enforce hooks.
- `templates/feature-spec.md` saves the lesson's template as a planning checklist.
  Its T1/T2 headings are examples; real OpenSpec tracked tasks use `- [ ] 1.1 ...`.
- `.claude/commands/review.md` and `.claude/commands/commit.md` provide Claude Code
  commands. `.agents/skills/review/` and `.agents/skills/commit/` expose the same
  instructions to Codex. Open a new assistant session if newly added skills are
  not visible in the current one.
- The installed global profile is `core`: propose, explore, apply, update, sync,
  and archive. Profile selection belongs to `openspec config profile`, rather
  than the project YAML. The companions live outside the managed `opsx` namespace.

Sources: [tutorial lesson](https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/LESSON.md),
[review prompt](https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/resources/commands/review.md),
[commit prompt](https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/resources/commands/commit.md),
[OpenSpec customization](https://github.com/Fission-AI/OpenSpec/blob/main/docs/customization.md),
and [OpenSpec tool support](https://github.com/Fission-AI/OpenSpec/blob/main/docs/supported-tools.md).
