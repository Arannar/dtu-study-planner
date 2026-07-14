# AI_README

This file is a starting context brief for agentic coding sessions on the Study Planner repository. Prefer it together with `AGENTS.md`, which contains the active contribution rules.

## Project Intent

Study Planner is a local DTU study-plan builder. It combines:

- A .NET 10 ASP.NET Core backend that calls DTU's public SOAP services and exposes normalized JSON endpoints.
- A SvelteKit/Svelte 5 frontend that lets users import courses, select programmes, drag courses into semesters, inspect teaching-block conflicts, track requirement buckets, and save/load study plans.

The backend is the source of truth for DTU data normalization and plan validation. The frontend mirrors the API contracts with TypeScript types and provides client-side planning ergonomics.

## High-Level Architecture

```text
SvelteKit UI
  -> JSON over localhost
ASP.NET Core controllers
  -> service interfaces
Planner/domain services
  -> generated WCF/SOAP clients
DTU public course database services
```

The frontend hardcodes `API_BASE_URL = 'http://localhost:5140'` in `frontend/src/lib/planner.ts`. The backend launch profile uses `http://localhost:5140`, and CORS allows `http://localhost:5173`.

## Backend

Backend project: `backend/backend.csproj`

Target/framework and key packages:

- `net10.0`
- `System.ServiceModel.*` packages for SOAP client access

`backend/Directory.Build.props` redirects local build output to a per-user local build root when possible. The project file also removes generated artifact folders from compile/content items.

### Backend Entry Point

`backend/Program.cs` configures:

- Controllers.
- CORS policy named `frontend`.
- Generated SOAP clients:
  - `CourseSoapClient`
  - `VolumeServiceClient`
  - `VisualizationServiceClient`
  - `CourseblockServiceClient`
- Service registrations:
  - `ICourseCatalogService` -> `CourseCatalogService`
  - `IStudyPlanValidator` -> `StudyPlanValidator`
  - `IVolumeResolver` -> `VolumeResolver`
  - `IProgrammeVisualizationService` -> `ProgrammeVisualizationService`
  - `IProgrammeService` -> `ProgrammeService`

### Controllers

`backend/Controllers/CoursesController.cs`

- Route: `api/courses`
- Resolves an effective volume through `IVolumeResolver`.
- Parses comma-separated `codes`.
- Delegates to `ICourseCatalogService.GetCoursesForStudyPlanAsync`.
- Returns `CoursesResponse`.

`backend/Controllers/ProgrammesController.cs`

- Route: `api/programmes`
- `GET /api/programmes?volume=...`
- `GET /api/programmes/{code}/definition?volume=...&language=...`
- Delegates to `IProgrammeService`.

`backend/Controllers/PlannerController.cs`

- Route: `api/planner`
- `POST validate-placement`
- `POST validate-semester`
- `POST validate-plan`
- Request DTOs currently live in this controller file. If they evolve substantially, consider centralizing them under `backend/Models` per `AGENTS.md`.
- Delegates to `IStudyPlanValidator`.

### Models and Contracts

Frontend-facing backend contracts are in `backend/Models`:

- `CourseSummary`
- `CoursePlacementOption`
- `CoursesResponse`
- `PlannedCourse`
- `StudyPlan`
- `PlacementResult`
- `SemesterValidationResult`
- `PlanValidationResult`
- programme list/definition models in `ProgrammeModels.cs`

These are mirrored by TypeScript types in `frontend/src/lib/planner.ts`. Keep backend and frontend shapes synchronized when changing API contracts.

### Course Data Normalization

Main file: `backend/Services/CourseCatalogService.cs`

Responsibilities:

- Calls DTU course SOAP methods for each requested course code:
  - `GetCourseNewestBeforeVolumeAsync`
  - `GetCourseHoldingsAsync`
  - fallback `GetCoursesTitlesAsync`
- Parses XML for title, programme level, ECTS, schedule text, raw schedule keys, and normalized time blocks.
- Maps DTU programme levels such as `DTU_BSC`, `DTU_MSC`, `DTU_BENG` to `bsc`, `msc`, `beng`.
- Normalizes weekly teaching blocks:
  - Whole modules like `E1` expand to `E1A` and `E1B`.
  - Evening modules like `E7` stay atomic.
  - Atomic modules like `E3A` stay atomic.
  - Month blocks normalize to `JANUARY`, `JUNE`, `JULY`, `AUGUST`.
- Parses multi-scheme schedule text such as "Scheme A" / "Scheme B" into `CoursePlacementOption` entries.
- Returns missing course codes separately from found courses.

### Programme Data

Main file: `backend/Services/ProgrammeService.cs`

Responsibilities:

- Gets programme metadata through the volume service.
- Falls back from a requested volume to the previous volume if the requested volume has no programme entries.
- Flattens DTU education lines into `ProgrammeListItem`.
- Infers programme level from English/Danish education names.
- Filters duplicate legacy BSc programme entries when newer 23/24-style replacements are present.
- Builds `ProgrammeDefinitionResponse` with:
  - programme metadata
  - bucket limits
  - mandatory courses
  - approved MSc elective course codes
  - visualization references
  - recommended study package views
  - study-flow import options
  - missing course codes and explanatory notes
- Uses Courseblock StudyBox data for strict programme bucket classification when possible.
- Falls back to parsing "Studieplan" visualization HTML for mandatory courses when needed.
- Builds study-flow options from "Studieforloeb" and recommended package visualizations.
- Loads the curated `BScEE_generic_plan.json` option for programme code `ELEKTEK23`.

Important bucket limit defaults:

- BSc: total 180, polytechnical foundation 55, programme-specific 55, projects 25, electives 45.
- MSc: total 120, polytechnical foundation 10, programme-specific 50, projects 30, electives 30.
- BEng: total 210, mandatory 135, internship 30, projects 15, electives 30.

### Validation Rules

Main files:

- `backend/Services/IStudyPlanValidator.cs`
- `backend/Services/StudyPlanValidator.cs`

Rules currently implemented:

- Placement validation rejects BSc placement of MSc-level courses unless the course code is in the programme's approved MSc elective list.
- Courses with fall blocks (`E...`) can only be placed in odd-numbered semesters.
- Courses with spring blocks (`F...`) can only be placed in even-numbered semesters.
- Semester validation checks pairwise overlap by intersecting normalized time blocks.
- Plan validation checks programme requirement totals when programme level and bucket limits are supplied.
- BSc/MSc programme-specific and project overflow counts against elective capacity.
- BEng validates mandatory, elective, internship, and project limits separately.
- Synthetic activity types such as `bscProject`, `bengProject`, `bengInternship`, `mscThesis`, and `specialCourse` affect bucket resolution.

Known nuance: month/intensive blocks are modeled in the frontend parity utilities, while backend parity checks currently look for `E` and `F` prefixes.

### Generated SOAP Clients

Generated SOAP clients live in:

- `backend/RefCourseSoap`
- `backend/RefCourseblocks`
- `backend/RefVisualizations`
- `backend/RefVolumes`

Avoid hand-editing generated `Reference.cs` files unless intentionally regenerating or patching generated service references.

## Frontend

Frontend project: `frontend`

Key stack:

- SvelteKit 2
- Svelte 5 runes mode
- TypeScript
- Vite
- ESLint, Prettier, `svelte-check`

### Important Files

`frontend/src/lib/planner.ts`

- Shared frontend contract types corresponding to backend models.
- Constants such as `API_BASE_URL`, `DEFAULT_CODES`, teaching block/month block values, and timetable layout.
- Pure utility functions for:
  - parsing course codes
  - sorting time blocks
  - filtering courses
  - semester parity checks
  - splitting multi-semester course blocks
  - selected placement option resolution
  - DTU course database URLs
  - synthetic activity checks

Per `AGENTS.md`, shared planner types, constants, and pure utilities should stay here or in typed modules under `frontend/src/lib`, not duplicated inside route components.

`frontend/src/lib/planner-ui.ts`

- Frontend-only typed helpers for route orchestration and display calculations.
- Owns synthetic activity templates, programme requirement bars, exam-rule bars, course display labels, bucket display resolution, validation-message formatting, and tooltip text.

`frontend/src/routes/+page.svelte`

- Main application UI.
- Uses Svelte 5 runes (`$state`, `$derived`).
- Manages available courses, selected programme, selected study-flow option, current study plan, validation state, drag-and-drop state, save/load UI, and synthetic activities.
- Calls backend endpoints:
  - `/api/courses`
  - `/api/programmes`
  - `/api/programmes/{code}/definition`
  - `/api/planner/validate-placement`
  - `/api/planner/validate-plan`
- Builds requirement bars and bucket legends.
- Supports importing mandatory courses and recommended packages.
- Allows adding/removing/moving courses across semesters.
- Provides weekly timetable and semester block visualizations.

`frontend/src/routes/+layout.svelte` is currently minimal and only renders children.

### Frontend Quality Gates

Run from `frontend`:

```powershell
npm run check
npm run lint
npm run build
```

Any frontend code change should remain compatible with the strict TypeScript/Svelte checks.

## Local Development Commands

Root setup:

```powershell
.\scripts\bootstrap-local.ps1
```

Backend:

```powershell
cd backend
dotnet restore
dotnet build
dotnet run
```

Frontend:

```powershell
cd frontend
npm install
npm run dev
npm run check
npm run lint
```

Backend URL: `http://localhost:5140`

Frontend dev URL: usually `http://localhost:5173`

## Standalone Packaging

The chosen packaging strategy is "ASP.NET Core hosts the static Svelte build".

Implementation details:

- `frontend/svelte.config.js` uses `@sveltejs/adapter-static` and writes the static app to `frontend/build`.
- `frontend/src/lib/planner.ts` uses `http://localhost:5140` in Vite dev and same-origin API calls in production builds.
- `backend/Program.cs` serves default/static files, maps API controllers, and falls back to `index.html`.
- `backend/appsettings.json` enables production browser auto-open through `LocalApp:OpenBrowserOnStart`.
- `backend/appsettings.Development.json` disables browser auto-open for normal development.
- `scripts/publish-standalone.ps1` and `scripts/publish-standalone.sh` build the frontend, copy `frontend/build` to `backend/wwwroot`, and run self-contained `dotnet publish`.

Windows packaging:

```powershell
.\scripts\publish-standalone.ps1 -Runtime win-x64
```

macOS/Linux packaging:

```sh
./scripts/publish-standalone.sh osx-arm64
./scripts/publish-standalone.sh linux-x64
```

Packages are written to `artifacts/publish/<runtime>`. The Windows package contains `DTU_StudyPlanner.exe`; macOS/Linux packages contain the platform-specific `DTU_StudyPlanner` backend executable. Static frontend files are published under `wwwroot`.

## Data and Generated/Local Folders

Treat these as generated, exploratory, or local dependency/build output unless a task explicitly targets them:

- `frontend/node_modules`
- `frontend/.svelte-kit`
- `frontend/.svelte-kit-local`
- `frontend/build`
- `backend/bin`
- `backend/obj`
- `backend/artifacts`
- `backend/wwwroot`
- legacy SOAP capture folders if present, such as `backend/SoapExplorationData` or root `SoapExplorationData`
- root `artifacts`

The repository also contains source/reference documentation under `docs/`, including SOAP UI projects and service description documents.

## Current Known Product Shape

The frontend is already more than a demo page: it is the planning workspace. It supports programme selection, course import, drag-and-drop placement, requirement tracking, study-flow import, synthetic activity creation, and JSON save/load.

BSc programme behavior is the most mature. BEng and MSc have structural bucket limits and activity support, but future work should verify programme-specific DTU rules before assuming full correctness.

ELEKTEK23 is a key reference path because it has the curated `BScEE_generic_plan.json` preset and stronger programme-definition wiring.

## Coding Guidance for Future Agents

- Read `AGENTS.md` before editing.
- Keep controllers thin. Add validation/business logic to services behind interfaces.
- Keep frontend route components focused on UI orchestration. Move reusable transformation/filtering/sorting/validation logic into `frontend/src/lib`.
- Keep backend API contracts in `backend/Models`; avoid anonymous or duplicated response shapes.
- Keep `frontend/src/lib/planner.ts` synchronized with backend model changes.
- Avoid casual edits to generated SOAP references.
- Keep generated/local folders out of source control; they can be large and noisy.
- Prefer focused tests/checks based on the files touched. At minimum, frontend changes should pass `npm run check` and `npm run lint`; backend changes should pass `dotnet build`.
