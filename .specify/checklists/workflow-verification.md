# Speckit Workflow Verification Checklist

**For**: GitHub Copilot using DeepWiki project  
**Purpose**: Ensure proper workflow phases are followed before implementation  
**Status**: Required for all features

---

## Phase Checklist: Before Starting ANY Implementation

### Phase 0: Setup ✅
- [ ] `constitution.md` exists at `.specify/memory/constitution.md`
- [ ] Feature directory created: `specs/{FEATURE}/`
- [ ] `.specify/scripts/bash/check-prerequisites.sh` runs successfully
- [ ] Output shows FEATURE_DIR and AVAILABLE_DOCS

### Phase 1: Specification ✅
- [ ] `spec.md` exists in `specs/{FEATURE}/`
- [ ] File contains 3+ user stories with priorities (P1, P2, P3)
- [ ] Each story has acceptance scenarios
- [ ] Success criteria defined (SC-001, SC-002, etc.)
- [ ] Edge cases documented
- [ ] No "NEEDS CLARIFICATION" items remain

### Phase 2: Clarification ✅
- [ ] All ambiguous items from spec.md resolved via Q&A
- [ ] Clarifications documented in spec.md session section
- [ ] Constitutional gates passed (test-first, security, etc.)
- [ ] Technical decisions made (retry policy, indexing strategy, etc.)
- [ ] User confirms: "Ready to plan"

### Phase 3: Planning ✅
- [ ] `plan.md` exists with full implementation phases
- [ ] `research.md` exists with technical decisions
- [ ] `data-model.md` or equivalent domain model defined
- [ ] `contracts/` directory with interface specifications
- [ ] `quickstart.md` with developer guide
- [ ] Constitutional gates passed (post-design review)
- [ ] All phases 1.1-1.5+ detailed with acceptance criteria
- [ ] User confirms: "Plan approved, ready to implement"

### Phase 4: Task Generation ⚠️ **CRITICAL - DO NOT SKIP**
- [ ] **TASK GENERATION COMPLETED** ← Check this first!
- [ ] `tasks.md` exists in `specs/{FEATURE}/`
- [ ] File contains 50+ numbered tasks (T001, T002, etc.)
- [ ] Tasks organized by:
  - [ ] Phase (1.1, 1.2, 1.3, etc.)
  - [ ] User Story ([US1], [US2], etc.)
  - [ ] Parallelization ([P] markers where applicable)
- [ ] Each task has:
  - [ ] Clear title (3-7 words, action-oriented)
  - [ ] File path for deliverables
  - [ ] Acceptance criteria
- [ ] Todo list created with generated tasks
- [ ] User confirms: "Tasks reviewed and approved"

**FAILURE MODE**: Skipping this phase → jumping to Phase 5 without discrete tasks  
**CONSEQUENCE**: Lost traceability, unclear acceptance, hard to parallelize  
**RECOVERY**: Go back and generate `tasks.md` before writing ANY implementation code

---

## Implementation Checklist: Phase 5 - TDD Workflow

### Before Writing ANY Code

- [ ] `tasks.md` exists and has been reviewed ← **REQUIRED**
- [ ] Todo list updated with task list
- [ ] First task identified (usually T001 or first task in current phase)
- [ ] You understand the task's acceptance criteria completely

### For Each Task (TDD Workflow)

**Step 1: Prepare**
- [ ] Mark task as `in-progress` in todo list
- [ ] Create test file if it doesn't exist
- [ ] Read acceptance criteria for this task

**Step 2: Write Tests (FIRST)**
- [ ] Write unit/integration tests that specify desired behavior
- [ ] Tests should **FAIL** initially (red phase)
- [ ] Do NOT write implementation code yet
- [ ] Run tests: `dotnet test` → should show failures

**Step 3: Implement (Make Tests Pass)**
- [ ] Write minimum implementation to make tests pass (green phase)
- [ ] Focus on acceptance criteria, not perfection
- [ ] Run tests: `dotnet test` → should show all passing
- [ ] Verify code coverage for this task ≥90%

**Step 4: Verify & Mark Complete**
- [ ] Re-read acceptance criteria
- [ ] Verify each criterion is met in code
- [ ] Check tests all pass
- [ ] Code review or self-review complete
- [ ] Mark task as `completed` in todo list

**Step 5: Move to Next Task**
- [ ] Commit changes: `git add . && git commit -m "[TaskID] Task description"`
- [ ] Start next task (repeat steps 1-5)

---

## Integration Points: When Tasks Cross Boundaries

### Within Same Phase
- Tasks execute sequentially or in parallel (per [P] markers)
- No special integration needed
- Commit frequently (per task)

### Between Phases
- All tasks from current phase must be complete
- Run full test suite for phase: `dotnet test`
- Verify acceptance criteria for phase in plan.md
- Create phase completion report (optional but recommended)
- Only then start next phase

### When Merging Parallel Work
- All parallelized tasks must be complete
- Merge branches following git workflow
- Run full test suite again (integration test)
- Verify no conflicts in shared files
- Update todo list to reflect all completions

---

## Red Flags: Signs You've Skipped Phases

🚨 **RED FLAG #1**: Implementing without `tasks.md`
- **What it looks like**: "I'll just code up the features from the plan"
- **Why it's wrong**: No discrete tracking, unclear requirements per task
- **Fix**: Stop, generate `tasks.md`, then proceed

🚨 **RED FLAG #2**: Writing implementation code before tests
- **What it looks like**: Creating entity classes, repositories before test files
- **Why it's wrong**: Violates TDD; tests define behavior, code satisfies tests
- **Fix**: Delete implementation code, write tests first, then code

🚨 **RED FLAG #3**: No acceptance criteria per task
- **What it looks like**: Tasks described as "Build feature X" with no clear definition of done
- **Why it's wrong**: Impossible to know when task is complete
- **Fix**: Add specific, measurable acceptance criteria to each task

🚨 **RED FLAG #4**: Skipping clarification phase
- **What it looks like**: "I know what users want, let's build it"
- **Why it's wrong**: Ambiguities cause rework, wrong assumptions lead to scope creep
- **Fix**: Run `/speckit.clarify` command before moving to planning

🚨 **RED FLAG #5**: No phases 1.1-1.5 defined
- **What it looks like**: Plan says "Implement everything" without phase breakdown
- **Why it's wrong**: No incremental delivery, too big to fail, hard to hand off
- **Fix**: Break implementation into phases with clear boundaries

---

## The Proper Workflow Flowchart

```
START: Feature Requested
    ↓
Phase 0: Setup
    ├─ Constitution exists?
    ├─ Feature dir created?
    └─ Prerequisite script runs?
    ↓ YES → continue
Phase 1: Specification
    ├─ User stories written?
    ├─ Requirements clear?
    └─ Ambiguities documented?
    ↓ YES → continue
Phase 2: Clarification
    ├─ All Q&A resolved?
    ├─ Constitution gates pass?
    └─ User confirms ready?
    ↓ YES → continue
Phase 3: Planning
    ├─ Implementation phases defined?
    ├─ Contracts specified?
    ├─ Research complete?
    └─ Constitution gates pass (post-design)?
    ↓ YES → continue
Phase 4: Task Generation ⚠️ CRITICAL
    ├─ tasks.md GENERATED?
    ├─ 50+ tasks in checklist format?
    ├─ User story mapping ([US#])?
    └─ Todo list created?
    ↓ YES → continue
    ↓ NO → STOP, GENERATE TASKS!
Phase 5: Implementation (TDD)
    ├─ Per Task:
    │  ├─ Write tests first
    │  ├─ Make tests pass
    │  ├─ Verify acceptance criteria
    │  └─ Mark task complete
    └─ Continue until all tasks done
    ↓
DONE: Feature Complete
```

---

## Example: Correct vs Incorrect Workflow

### ❌ INCORRECT (What happened with Phase 1.1)
```
User: "Proceed with Phase 1.1 implementation"
↓
Copilot: Creates projects
Copilot: Creates entities
Copilot: Creates interfaces
Copilot: Writes tests
Copilot: All working! Phase 1.1 complete
↓
Result: ✗ Completed, but NO TASKS GENERATED
Impact: Can't track individual task status, hard to hand off work
```

### ✅ CORRECT (How to do Phase 1.2)
```
Copilot: Check - tasks.md exists? ✓
Copilot: Check - todo list created? ✓
Copilot: Mark T020 as in-progress
↓
Step 1: Write SqlServerVectorDbContext tests (FAIL initially)
Step 2: Implement SqlServerVectorDbContext (tests PASS)
Step 3: Verify acceptance criteria met
Step 4: Mark T020 complete, move to T021
↓
Step 1: Write SqlServerVectorStore tests (FAIL)
Step 2: Implement SqlServerVectorStore (PASS)
...continue for all tasks...
↓
Result: ✓ Completed tasks tracked, clear acceptance, easy handoff
```

---

## Tool Commands Reference

**Generate Tasks from Plan**:
```bash
.specify/scripts/bash/check-prerequisites.sh --json
# Then follow the prompt instructions in:
# .github/prompts/speckit.tasks.prompt.md
```

**Update Todo List**:
```
Use manage_todo_list tool in Copilot
Set operation: "write"
Include all tasks in proper format
```

**Verify Workflow**:
```bash
# Check phases exist
ls specs/001-multi-db-data-layer/
# Should show: spec.md, plan.md, research.md, data-model.md, 
#             contracts/, quickstart.md, tasks.md

# Verify tasks file
wc -l specs/001-multi-db-data-layer/tasks.md
# Should be 300+ lines
```

---

## Constitutional Alignment

This workflow implements constitution Principle I (Test-First):
- ✅ Tests generated before implementation
- ✅ Acceptance criteria explicit per task
- ✅ Code quality gate (90%+ coverage)
- ✅ Reproducibility through task definition

This workflow implements constitution Principle VI (Simplicity):
- ✅ Clear phases, not everything at once
- ✅ Discrete tasks, each independently verifiable
- ✅ Parallelization supported but not forced
- ✅ Incremental delivery possible

---

## Training & Accountability

**For GitHub Copilot**:
1. Read this checklist before starting any feature
2. If you skip a phase, stop and go back
3. Never write implementation code before tests
4. Always verify tasks.md exists before Phase 5

**For Users**:
1. Review specification before approving plan
2. Review tasks before approving implementation
3. Request corrections if phases are skipped
4. Use this checklist to keep Copilot accountable

---

## Document History

| Version | Date | Change |
|---------|------|--------|
| 1.0 | 2026-01-16 | Initial creation after Phase 1.1 retrospective |

---

**Status**: Active - Use for all features in DeepWiki project  
**Owner**: Constitution enforcement  
**Questions**: Reference `.specify/instructions/copilot-speckit-workflow.md`
