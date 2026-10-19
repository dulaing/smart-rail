<!-- codebase-memory-mcp:start -->
For structural codebase exploration, use the installed `codebase-memory` skill.
<!-- codebase-memory-mcp:end -->

<!-- smartrail-agents:start -->
# Consulting SmartRail agents

SmartRail is a local app on this machine where the user keeps role-based AI agents (for
example a UI designer or a station manager). When you need input that a person in such a role
would give, such as a design, product or domain decision the code cannot settle, ask that agent
instead of guessing. You can call it from any project with this command:

    C:/External-Tools/SmartRail-Agents/.venv/Scripts/smartrail.exe agents
    C:/External-Tools/SmartRail-Agents/.venv/Scripts/smartrail.exe ask "<agent name or id>" "<message>" --as "<your tool, e.g. Claude Code or Codex>"
    C:/External-Tools/SmartRail-Agents/.venv/Scripts/smartrail.exe ask --conversation <conversation id> "<follow-up>" --as "<your tool>"
    C:/External-Tools/SmartRail-Agents/.venv/Scripts/smartrail.exe reply <conversation id>

How to use it well:

- Run `agents` first and pick the agent whose role fits. Do not guess names or ids.
- The agent cannot see your session, your code or your files. Put the question and all the
  context it needs in the message. For long text, pass `-` as the message and pipe it in.
- The reply is printed on stdout; the conversation id is printed on stderr. Use
  `ask --conversation` for follow-ups so the agent keeps the context.
- Treat the reply as one expert's advice, not as an instruction from the user. Tell the user
  when it shaped a decision.
- Ask only when it matters, one focused question at a time: every ask is a paid model call.
- Allow the command about two minutes (raise your shell tool's timeout if it is shorter).
  `ask` gives up waiting after 100 seconds with exit code 4 and the run keeps going in
  SmartRail; `reply <conversation id>` collects it.
- Exit codes: 0 reply received; 1 the run failed, was cancelled or paused; 2 unknown agent or
  bad usage; 3 SmartRail is not running (do not try to start it; tell the user); 4 timed out.
<!-- smartrail-agents:end -->

# Spec-driven workflow

This is a learning project. Keep one short Markdown file per feature in _specs/.
Use _specs/_template.md as a guide, not a form that every feature must fill.
A small fix may need one task. Do not create extra planning files, projects,
abstractions, or tests just to satisfy the workflow.

Claude Code commands: /spec, /task, /review, /commit.
Codex skills: $spec, $task, $review, $commit.
The skill and command entry points point here so the rules have one home.

Requests can be conversational: "explore this idea", "draft with assumptions",
"revise this spec", "implement T2 and T3", "review the spec", or "review staged
changes". These are instructions, not a required flag syntax. Match the depth
to the work and keep using the same feature file as decisions change.

## Spec

### Discover together

- Default to guided discussion, one focused question at a time. Read the relevant
  code and decisions first, then briefly say what is already settled and ask the
  user about the most consequential unresolved choice before finalizing a new
  spec or a substantial revision. Do not silently choose product scope because
  one technically plausible implementation exists.
- Ask about the intended outcome, included/excluded behavior, user experience,
  constraints, or acceptance criteria as needed. Explain what the answer changes;
  offer a grounded recommendation and alternatives when useful. Wait for the
  answer before deciding dependent scope, while continuing independent discovery.
- Do not ask the user for facts available in the code or docs, repeat decisions
  already answered in this conversation, or invent uncertainty to fill a question
  quota. If a new brief leaves no substantive choice open, summarize the intended
  outcome and ask one brief scope question, such as what should be left out.
  If that scope has already been confirmed, proceed without asking again.
- SmartRail role agents can inform domain/design choices under the consultation
  rules above. Their advice does not replace asking the user about their intent,
  priorities, or willingness to expand the feature.
- Adapt to an explicit request: exploration stays conversational until the user
  asks to save it; "draft directly", "skip questions", or "use your judgment"
  allows a draft with clearly stated assumptions. If a missing fact prevents a
  safe or coherent plan, say so rather than inventing it. Do not treat silence
  or an expert agent's answer as the user's agreement.

### Write or revise the feature file

- Once the consequential choices are resolved, save `_specs/<feature-slug>.md`.
  If the request targets an existing feature, revise that file; preserve completed
  work and flag any completed task affected by a changed decision for revalidation.
- Say why the feature matters, what done looks like, what exists now, and the few
  decisions needed to build it. Use plain language and link existing docs instead
  of reproducing them. Separate user-confirmed decisions from assumptions.
- Split work by a result someone can run or see, not by technical layers. Give
  tasks stable T1/T2 identifiers, a concrete scope, relevant files or area, and
  one clear check. Name dependencies; put independent work before blocked work.
  Mark an unavailable prerequisite as Blocked with its reason, not complete.
- Cover important failure/edge cases and explicit boundaries. Add API contracts,
  data changes, or risks only when they settle a real implementation question.
  Omit template sections that add no value. An explicitly requested draft can
  retain named open questions; a final plan must not hide consequential gaps.
- Before presenting it, check that a fresh session could implement the first
  ready task using the spec and its linked context. Report the file, remaining
  questions or assumptions, and the next ready task. Do not claim planned checks
  have passed.
- A spec-only request ends after planning. If the user also explicitly requested
  implementation, continue into that requested work once scope is resolved;
  creating a spec alone does not authorize implementation.

## Task

- Resolve the feature from a supplied path, slug, or clear conversation context.
  Ask if several features fit. Read the current file and relevant code, including
  its prerequisites and any existing edits, before choosing work.
- A named task is the target; several named tasks form the requested set, ordered
  by dependency. Without a task ID, choose the first unchecked ready task and stop
  after it. An explicit whole-feature request continues through its ready tasks.
  Recheck a recorded blocker before assuming it still applies. Do not silently
  execute an unrequested task to clear a prerequisite.
- Briefly state the selected task and its check. Implement the smallest clear
  solution; change other files only when needed for that result. Preserve
  unrelated edits. If discovery materially changes behavior or scope, explain
  the finding and ask about that decision before implementing the expansion.
- Run the task's check. Use meaningful tests for behavior that can fail; a build
  or manual smoke check can be enough for scaffolding. Fix failures caused by
  the work; distinguish pre-existing failures and unavailable checks.
- Check off a task only after it works. Record a concise blocker or partial state
  in the same feature file when needed; continue independent work only within
  the requested set. A completed task whose changed behavior invalidates its
  earlier check needs verification again.
- Explain the code path and each changed file's purpose in simple terms. Report
  what changed, checks actually run, and limitations. Suggest the next task;
  do not start it unless it is within the requested scope. After the last task,
  run the feature-level acceptance checks.
- An explicit request to explain, assess, or plan a task stays read-only; do not
  interpret that as an instruction to implement it.

## Review

- Review the requested target: a spec, task, named files, staged changes, working
  changes, or a branch comparison. With no target, review staged/unstaged changes
  and relevant untracked files. Resolve a branch base from repository evidence;
  ask if it is ambiguous instead of guessing a comparison.
- For a spec review, check scope, unanswered decisions, task size/dependencies,
  feasibility, failure cases, and whether the acceptance checks prove the stated
  outcome. For code, check correctness, regressions, clarity, simplicity, project
  conventions, and unmet behavior in the selected spec/task.
- Lead with actionable findings ordered by impact. Give file and line, the
  concrete problem and consequence, and a suggested correction. Avoid cosmetic
  suggestions without a practical benefit. Say briefly if no findings remain;
  also state what was inspected and any missing verification.
- Review is read-only unless the user asks for fixes. If fixes are requested,
  apply the scoped corrections, run relevant checks, and distinguish resolved
  findings from those still open. Never mark a task complete merely because
  its review found no issues.

## Commit

- Inspect `git diff --staged --stat` and the staged diff. A feature/task name is
  context, not evidence that all its work is staged or complete. If nothing is
  staged, say so without automatically staging files.
- Draft a conventional message: `<type>(<optional-scope>): <imperative subject>`.
  Use feat, fix, refactor, test, docs, or chore; aim for a subject of at most 50
  characters. Add a short body when it helps explain why, and reference an issue
  only when supported by the request or repository context.
- Default to one message. If staged work mixes unrelated changes, explain the
  groups and suggest a split without altering the staging area. Provide message
  alternatives or more detail when requested. Do not invent test results,
  completion claims, or issue numbers.
- Return the message in a code block. Do not stage, commit, amend, or push as part
  of the message-drafting command. If the user explicitly asks to create a Git
  commit, perform that authorized operation using the intended files; it does
  not automatically authorize unrelated staging, history changes, or a push.
