# AGENTS.md

## Frontend rules

1. Keep all shared domain types, constants, and pure planner utilities in `frontend/src/lib/planner.ts`, and avoid redefining backend contract shapes inside route components.
2. Keep Svelte route components focused on UI orchestration; move reusable data transformation, filtering, sorting, and validation logic into typed utility modules under `frontend/src/lib`.
3. Any frontend change must remain compatible with the existing strict TypeScript + Svelte quality gates by keeping code lintable and checkable with the existing `check` and `lint` scripts.

## Backend rules

1. Keep controllers thin: controller actions should only accept request DTOs, delegate business logic to services, and return HTTP results without embedding validation rules directly in controllers.
2. Keep planning and validation business rules inside service classes behind interfaces, following the existing `IStudyPlanValidator` / `StudyPlanValidator` pattern so logic stays testable and reusable.
3. Keep API contract models centralized in `backend/Models` and ensure frontend-facing request/response shapes evolve consistently with the planner domain instead of introducing ad hoc anonymous or duplicated structures.
