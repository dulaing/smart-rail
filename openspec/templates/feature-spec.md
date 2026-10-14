<!-- Source: https://github.com/owainlewis/youtube-tutorials/blob/main/tutorials/spec-driven-development/LESSON.md#a-useful-spec-template -->
<!-- Planning checklist: this does not replace OpenSpec's built-in artifact templates. -->

# Feature Name

## Why

[1-2 sentences explaining the problem and why it matters now.]

## What

[Concrete deliverable.
Specific enough to verify when done.]

## Constraints

### Must

- [Required patterns, libraries, conventions]

### Must Not

- [No new dependencies unless specified]
- [Do not modify unrelated code]

### Out of Scope

- [Adjacent features we are explicitly not building]

## Current State

[What exists now.
This saves the agent from exploring blindly.]

- Relevant files: `path/to/file.ts`
- Existing patterns to follow

## Tasks

### T1: [Title]

What: [What to build]
Files: `path/to/file`, `path/to/test`
Verify: `command to run` or manual check

### T2: [Title]

What: ...
Files: ...
Verify: ...

## Validation

- `command to verify the full feature works`
- `npm test` or equivalent
- Manual check: [what to verify in UI/API]
