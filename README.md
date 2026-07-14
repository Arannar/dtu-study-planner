# Study Planner

Study Planner is a local full-stack application for building and validating DTU study plans. It combines an ASP.NET Core backend, generated DTU SOAP clients, and a SvelteKit frontend for importing courses, arranging semesters, checking teaching-block conflicts, and tracking programme requirement buckets.

The app runs locally and queries DTU's public course database services when live course or programme data is loaded.

## Features

- Load DTU courses by course code and course-volume year.
- Normalize course titles, ECTS, level, teaching blocks, placement options, grading mode, and examiner mode.
- List DTU programmes and import mandatory courses or available recommended study-flow packages.
- Drag courses between semesters and inspect weekly teaching-block conflicts.
- Add synthetic activities such as projects, internships, theses, and special courses.
- Validate semester parity, overlapping teaching blocks, programme ECTS buckets, and selected BSc/MSc elective restrictions.
- Export and import plans as JSON.

## Repository Layout

```text
.
|-- backend/                  ASP.NET Core API and generated SOAP clients
|-- frontend/                 SvelteKit planner UI
|-- docs/                     service notes, SOAP references, and roadmap
|-- scripts/                  setup and standalone publish scripts
|-- BScEE_generic_plan.json   curated ELEKTEK23 baseline plan
|-- AGENTS.md                 coding-agent contribution rules
`-- AI_README.md              detailed architecture notes for agent sessions
```

Generated dependencies, build output, SOAP captures, and publish artifacts are intentionally excluded from source control.

## Prerequisites

- .NET SDK with `net10.0` support.
- Node.js and npm compatible with `frontend/package.json`.
- Network access to DTU's public services when loading live data.

## Setup

From the repository root:

```powershell
.\scripts\bootstrap-local.ps1
```

On Unix-like systems:

```sh
./scripts/bootstrap-local.sh
```

## Running Locally

Start the backend:

```powershell
cd backend
dotnet run
```

The default backend URL is `http://localhost:5140`.

Start the frontend in another terminal:

```powershell
cd frontend
npm run dev
```

The frontend usually runs at `http://localhost:5173`. In development it calls `http://localhost:5140` unless `VITE_API_BASE_URL` is set. Production builds use same-origin API calls.

## Quality Gates

Backend:

```powershell
cd backend
dotnet build backend.csproj
dotnet run --project ..\backend.Tests\backend.Tests.csproj
```

Frontend:

```powershell
cd frontend
npm run check
npm run lint
npm run test
npm run build
```

## Standalone Packaging

The app can be packaged as a local self-contained ASP.NET Core executable that serves both the API and the built Svelte frontend. The published app still needs internet access for DTU data queries.

Windows:

```powershell
.\scripts\publish-standalone.ps1 -Runtime win-x64
```

macOS/Linux:

```sh
./scripts/publish-standalone.sh osx-arm64
./scripts/publish-standalone.sh linux-x64
```

Packages are written to `artifacts/publish/<runtime>`.

## API Surface

- `GET /api/courses?volume=2026&codes=01001,02002`
- `GET /api/programmes?volume=2026`
- `GET /api/programmes/{code}/definition?volume=2026&language=da-DK`
- `POST /api/planner/validate-placement`
- `POST /api/planner/validate-semester`
- `POST /api/planner/validate-plan`

## Notes And Limitations

- The backend is the source of truth for DTU data normalization and validation.
- API contracts live in `backend/Models` and are mirrored by TypeScript types in `frontend/src/lib`.
- BSc programme behavior is the most mature path. BEng and MSc bucket structures are present, but programme-specific DTU rules should be verified before relying on them for official advising.
- Generated SOAP client references under `backend/Ref*` are committed because they are required source inputs.
- Future feature ideas live in `docs/ROADMAP.md`.

## License

MIT.
