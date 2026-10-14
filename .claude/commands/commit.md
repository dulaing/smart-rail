---
description: Draft a conventional commit message explaining the reason for staged changes.
---

# Commit

Adapted from [Owain Lewis's commit command](https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/resources/commands/commit.md).

Generate a commit message for staged changes. This command drafts the message;
it does not stage files or execute `git commit`.

## Instructions

1. Run `git diff --staged --stat` and `git diff --staged` to inspect the exact
   staged changes. If nothing is staged, report that and stop.
2. If the user names an OpenSpec change, read its existing artifacts using
   `openspec status --change "<name>" --json` for context. Preserve an explicitly
   selected store with `--store "<id>"`. Base the message on the staged diff;
   do not imply that unstaged work or the entire change is complete.
3. Explain why the change is needed. Reference issues only when supplied or
   supported by repository context. Flag unrelated staged work that prevents
   a coherent message without changing the staging area.

## Format

```text
<type>(<scope>): <subject>

<body explaining why>
```

Types: `feat` (feature), `fix` (bug), `refactor`, `test`, `docs`, `chore`.
Keep the subject at most 50 characters, in imperative mood. Omit the scope when
there is no useful one. Add an issue reference when applicable.

Return the proposed message in a code block. Do not claim tests passed or review
was completed unless that evidence is available.
