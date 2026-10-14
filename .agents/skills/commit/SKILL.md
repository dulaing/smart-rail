---
name: commit
description: Draft a conventional commit message for staged changes when the user requests a message or invokes /commit or $commit. This skill does not create a Git commit.
---

Follow the project's shared commit-message instructions in
[.claude/commands/commit.md](../../../.claude/commands/commit.md).
Read that file before drafting. Use any OpenSpec change name supplied by the
user as context. This skill inspects the staging area and returns a message;
staging files and creating a Git commit are separate actions.
