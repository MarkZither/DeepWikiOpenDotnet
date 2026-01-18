# GitHub Copilot - Speckit Workflow Instructions

## Overview
This document defines the strict workflow for using Speckit with GitHub Copilot in the DeepWiki project. Follow these steps sequentially—do not skip phases or jump to implementation prematurely.

---

## Workflow Phases

### Phase 0: Setup & Planning
**When:** Project initialization or new feature requested
**Process:**
1. User provides feature request or problem statement
2. Run: `.specify/scripts/bash/setup-plan.sh --json`
3. Parse output for FEATURE_SPEC, IMPL_PLAN, SPECS_DIR, BRANCH
4. Load `.specify/memory/constitution.md` for governance context
5. Output: Ready state with plan template loaded

**Copilot Action:** Do not propose code. Only load context and verify readiness.

---

### Phase 1: Specification Generation
**When:** Feature clearly defined
**Command:** `/speckit.specify <feature_description>`
**Process:**
1. Generate `specs/{FEATURE}/spec.md` with:
   - User stories (prioritized P1-P3)
   - Functional requirements (FR-001, FR-002, etc.)
   - Non-functional requirements
   - Edge cases and error scenarios
   - Success criteria (measurable)
2. Include acceptance checklist:
   - ✅ All requirements testable and measurable
   - ✅ No ambiguous acceptance criteria
   - ✅ Edge cases identified
   - ✅ Risk assessment completed

**Copilot Action:** Generate specification document. Do not write code or tests.

---

### Phase 2: Specification Clarification
**When:** Specification generated and requires validation
**Command:** `/speckit.clarify`
**Process:**
1. Review specification for unknowns (marked "NEEDS CLARIFICATION")
2. Generate 3-5 clarification questions
3. Document user responses
4. Update `specs/{FEATURE}/spec.md` with resolved clarifications
5. Verify all gates pass:
   - ✅ Constitution compliance
   - ✅ No technical debt
   - ✅ Observability requirements met
   - ✅ Security requirements met
   - ✅ Testing strategy defined

**Copilot Action:** Ask questions, document answers. Do not write code.

---

### Phase 3: Implementation Planning
**When:** Specification complete and clarified
**Command:** `/speckit.plan`
**Process:**
1. Generate `specs/{FEATURE}/plan.md` with:
   - Technical context (fully resolved, no NEEDS CLARIFICATION)
   - Constitution check (pre and post-design)
   - Gate evaluation (ERROR if violations)
   - **Phase 0 (Research):** Generate `research.md` with 7 technical decisions
   - **Phase 1 (Design):** Generate:
     - `data-model.md` (entity schemas)
     - `contracts/{Interface1}.md`, `contracts/{Interface2}.md` (API contracts)
     - `quickstart.md` (developer guide)
   - **Phase 1 (Agent Context):** Run `.specify/scripts/bash/update-agent-context.sh copilot`
   - **Phase 1.1-1.5 (Implementation Detail):** Detailed task breakdown

**Copilot Action:** Generate planning documents and research. Do not write implementation code.

**Output:** `plan.md` with implementation phases defined but NOT started.

---

### Phase 4: Task Generation (REQUIRED STEP - DO NOT SKIP)
**When:** Plan complete and approved
**Command:** `.specify/scripts/bash/generate-tasks.sh --plan specs/{FEATURE}/plan.md --json`
**Process:**
1. Script reads `plan.md` and generates discrete tasks
2. Each task has:
   - Unique ID (numbered sequentially)
   - Clear title (3-7 words, action-oriented)
   - Detailed description
   - Acceptance criteria
   - Dependencies (if any)
3. Output: `specs/{FEATURE}/tasks.json`
4. Update todo list with generated tasks

**Copilot Action:** 
- DO NOT implement code yet
- Read generated tasks.json
- Update todo list using `manage_todo_list` tool
- Confirm task list with user before proceeding

---

### Phase 5: Implementation (TDD Workflow)
**When:** Tasks generated and todo list approved
**Process:**
For each task in priority order:
1. Mark task as `in-progress` in todo list
2. Read task acceptance criteria
3. Write tests FIRST (TDD - tests before implementation)
4. Run tests (should fail initially)
5. Implement code to make tests pass
6. Verify tests pass and coverage ≥90% target
7. Mark task as `completed`
8. Move to next task

**Key Rule:** Write tests before implementation code. Never skip this.

**Copilot Action:** 
- Execute implementation following acceptance criteria
- Verify each task before marking complete
- Update todo list after each task completion

---

## Workflow Diagram

```
Phase 0: Setup
    ↓
Phase 1: Specification (spec.md)
    ↓
Phase 2: Clarification (resolve ambiguities)
    ↓
Phase 3: Planning (plan.md + research.md + contracts + design)
    ↓
Phase 4: Task Generation ⚠️ REQUIRED (tasks.json → todo list)
    ↓
Phase 5: Implementation (TDD workflow)
    ├─ Test 1 (write)
    ├─ Test 1 (run/fail)
    ├─ Code 1 (implement)
    ├─ Test 1 (pass)
    ├─ Mark Task 1 Complete
    ├─ Test 2 (write)
    ├─ ...repeat for each task
    └─ All tasks complete → Feature ready
```

---

## Critical Rules

### Rule 1: Never Skip Task Generation
- **Violation:** Jumping from Phase 3 → Phase 5 (what happened with Phase 1.1)
- **Reason:** Tasks provide granular, trackable work units with clear acceptance criteria
- **Fix:** Always run `generate-tasks.sh` before implementing
- **Verification:** Check for `tasks.json` in specs directory before starting code

### Rule 2: TDD is Non-Negotiable
- **Violation:** Writing implementation code before tests
- **Reason:** Tests define expected behavior; implementation satisfies tests
- **Fix:** Always write tests first, then implementation
- **Verification:** Test file created and tests written before any implementation code

### Rule 3: Track Todo Progress
- **Violation:** Starting tasks without marking as `in-progress`
- **Reason:** Provides visibility and prevents duplicate work
- **Fix:** Use `manage_todo_list` at start and end of each task
- **Verification:** Todo list shows exactly 1 `in-progress` task at a time

### Rule 4: Verify Acceptance Criteria
- **Violation:** Considering task complete without verifying all criteria met
- **Reason:** Prevents incomplete or incorrect implementations
- **Fix:** Check each acceptance criterion before marking task complete
- **Verification:** Re-read acceptance criteria, verify code satisfies each one

### Rule 5: Constitution Compliance
- **Violation:** Implementing features that violate constitution principles
- **Reason:** Constitution defines project governance and non-negotiable standards
- **Fix:** Reference constitution during planning and implementation
- **Verification:** Plan.md shows "Constitution check: PASS" before Phase 5

---

## Example: Correct Workflow

### What Should Have Happened with Phase 1.1

**Step 1: Generate tasks from plan.md**
```bash
.specify/scripts/bash/generate-tasks.sh --plan specs/001-multi-db-data-layer/plan.md --json
```

**Step 2: Approve generated tasks.json**
- Tasks for Phase 1.1 extracted and reviewed
- Todo list created with ~8-10 tasks

**Step 3: Implement each task with TDD**
```
Task 1: Create DeepWiki.Data project
  ✓ Write project setup test → FAIL
  ✓ Create project structure → PASS
  ✓ Mark Task 1 COMPLETE

Task 2: Create DocumentEntity with validation
  ✓ Write entity validation tests → FAIL
  ✓ Implement DocumentEntity → PASS
  ✓ Mark Task 2 COMPLETE

... continue for each task

Task N: Measure code coverage
  ✓ Run coverage report → PASS (≥90%)
  ✓ Mark Task N COMPLETE

All tasks complete: Phase 1.1 READY ✓
```

---

## What Went Wrong

**Actual workflow (incorrect):**
1. ✅ Phase 0: Setup complete
2. ✅ Phase 1: Specification generated
3. ✅ Phase 2: Clarification completed
4. ✅ Phase 3: Planning completed
5. ❌ **SKIPPED Phase 4: Task generation** ← ERROR
6. ✅ Phase 5: Jumped directly to implementation

**Impact:** 
- No discrete, trackable tasks created
- No clear acceptance criteria per task
- Work started without task list alignment
- Made it harder to track which specific unit of work was being completed

---

## How to Proceed from Here

Since Phase 1.1 implementation was already completed (though not following the workflow correctly), here's the corrective action:

1. **Retroactively generate tasks** for what was implemented:
   ```bash
   .specify/scripts/bash/generate-tasks.sh --plan specs/001-multi-db-data-layer/plan.md --json
   ```

2. **Map completed work to generated tasks** (verification)

3. **Use task list for Phase 1.2 onwards** (SQL Server implementation)

4. **Follow complete workflow for any new features**

---

## Copilot Checklist: Before Starting Any Phase

- [ ] Have I read the constitution? (`.specify/memory/constitution.md`)
- [ ] Is this Phase 0, 1, 2, 3, 4, or 5?
- [ ] Have I run the required Speckit command/script?
- [ ] Do I have all required input documents?
- [ ] Will I produce the expected output documents?
- [ ] Am I about to skip a phase? (STOP if yes)
- [ ] For Phase 5: Have I generated tasks.json? (STOP if no)
- [ ] For Phase 5: Have I updated the todo list? (STOP if no)

---

## Reference

- Constitution: `.specify/memory/constitution.md`
- Setup script: `.specify/scripts/bash/setup-plan.sh`
- Task generation: `.specify/scripts/bash/generate-tasks.sh`
- Agent context update: `.specify/scripts/bash/update-agent-context.sh`
- Plan template: Used after `setup-plan.sh` execution

---

**Version:** 1.0  
**Created:** 2026-01-16  
**Status:** Approved
