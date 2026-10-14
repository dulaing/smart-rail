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

## Spec

- Read only the relevant code and decisions before writing or revising a spec.
- Say why the feature matters, what done looks like, what exists now, and the few
  decisions needed to build it. Use plain language and link existing docs.
- Split work by a result someone can run or see, not by technical layers.
  Order tasks by dependency; put independent work before blocked work.
- Give each task one clear check. Mark an unavailable prerequisite as Blocked
  with its reason; do not imply an unrun check passed.
- If the user asks only for a spec, save it and stop. If they also ask for
  implementation, continue into the requested work.

## Task

- Read the feature file and relevant code. A named task is the target; if no
  task is named, choose the first unchecked task whose prerequisites are met.
  If the user asks for the whole feature, continue through its tasks.
- Implement the smallest clear solution. Change other files only when needed
  for that result. Preserve unrelated edits.
- Run the task's check. Use meaningful tests for behavior that can fail;
  a simple build or manual smoke check can be enough for scaffolding.
- Check off a task only after it works. If blocked, record the cause and
  continue independent requested work when possible.
- Explain the code path in simple terms so the user can learn every file.

## Review

When requested, review the selected task or changed files for correctness,
clarity, simplicity, and unmet spec behavior. Report actionable findings
with file and line. Do not edit during a review unless asked to fix them.

## Commit

When requested, inspect staged changes and draft a short conventional commit
message that explains why. Do not stage or create a commit unless the user
asks for those actions.