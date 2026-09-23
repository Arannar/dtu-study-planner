## Backend Source Reference

This reference covers the C# source files under `backend/`, including the four generated SOAP references. Build-generated files in `obj`, `bin`, and `artifacts` are excluded. Supporting project and configuration files are listed separately below.

Usage labels describe the current repository's application call paths, dependency-injection registrations, frontend requests, and test references. **Used** means reachable from startup or an exposed API, including conditional fallback paths; it does not mean every branch runs on every request. **Unused** means no application or test caller was found. Generated transport contracts can be required indirectly by serialization even when application code does not name them. These are source-inspection findings, not runtime coverage measurements.

The main request paths are:

```text
Program -> controllers -> application services
Courses -> CourseCatalogService -> DtuGateway.SearchDtuShb_Full -> CourseXmlParser
Programme definition -> catalogue + classification + mandatory courses + option descriptors
Selected study-flow import -> one HTML view -> StudyFlowParser -> cached course enrichment
DtuGateway -> generated SOAP clients (one per call); shared metadata and HTML cache
PlannerController -> StudyPlanValidator (pure, no remote calls)
```

### Startup

#### [`backend/Program.cs`](Program.cs) — used

Top-level statements form the application's entry point. They build the ASP.NET Core host, register controllers, configure CORS for `http://localhost:5173`, and register the application services, scoped DTU gateway, and singleton data cache. SOAP clients are created per remote call by the gateway. The validator is a singleton; catalogue/programme/classification/visualization/preset services and the volume resolver are scoped. DTU failures become HTTP 503 and invalid service inputs become HTTP 400 Problem Details.

Outside Development, when URL environment overrides are absent, listening uses `PORT`, container port `8080`, or `LocalApp:Url` in that order. Middleware serves default/static files and controller routes, exposes `GET /healthz`, and adds the SPA fallback only when `wwwroot/index.html` exists. `app.Run()` starts the server.

The local `OpenBrowser(url, logger)` function launches the configured local URL through the OS shell and logs launch failures. It is **conditionally used** by the application-started callback when `LocalApp:OpenBrowserOnStart` is enabled; both checked-in appsettings files disable it by default.

### Controllers

#### [`backend/Controllers/CoursesController.cs`](Controllers/CoursesController.cs) — used

`CoursesController` handles `GET /api/courses`. Its constructor receives `ICourseCatalogService`, `IVolumeResolver`, and a logger. `Get(volume, codes)` resolves an omitted volume, splits and trims the comma-separated course codes, logs the request, delegates course loading to the catalogue service, and returns `CoursesResponse` with HTTP 200. The frontend calls this endpoint to import/enrich courses. An empty code list produces an empty batch rather than a full catalogue search.

`GET /api/courses/search?query=learning&volume=2026` binds [`CourseSearchRequest`](Models/CourseSearchRequest.cs), resolves an omitted volume, and delegates to `ICourseCatalogService.SearchCoursesAsync`. The service trims the query and rejects blank input with HTTP 400. Exactly five ASCII digits use the SOAP `courseCode` parameter; all other queries use `searchWords` to search course text. `DtuGateway.SearchCoursesAsync` calls `SearchDtuShb_Full` with `FullXML` and the selected academic year. Results use the same `CourseSummary` parser as imports, are restricted to that year, deduplicated and sorted by code, and returned as `CoursesResponse` with an empty `missingCourseCodes` list. Search does not use historical fallback. Upstream failures return HTTP 503; zero matches return HTTP 200 with an empty course list.

The frontend search basket supports selecting several results for additive import. Results retain schedule alternatives and course metadata; merely searching or changing basket selections does not change the study plan.

#### [`backend/Controllers/ProgrammesController.cs`](Controllers/ProgrammesController.cs) — used

`ProgrammesController` delegates to `IProgrammeService`. `GetProgrammes(volume)` serves `GET /api/programmes` with programme choices. `GetProgrammeDefinition(code, volume, language)` serves `GET /api/programmes/{code}/definition`, defaults the language to `da-DK`, and returns 404 when the service cannot find the programme. Both actions are called by the frontend. `GetStudyFlow` serves `GET /api/programmes/{code}/study-flows/{optionId}` and returns a selected saved plan or a 404 Problem Details response when no importable placements are available. Unlike the courses controller, these actions do not use `IVolumeResolver` to supply the current year.

#### [`backend/Controllers/PlannerController.cs`](Controllers/PlannerController.cs) — used

`PlannerController` receives `IStudyPlanValidator` and passes request DTO fields to its methods. `ValidatePlacement` checks a candidate against a plan, including programme context; `ValidateSemester` checks one semester; `ValidatePlan` checks the whole plan. Each returns the validator result with HTTP 200, so a rejected placement is represented by `Allowed = false`, not an HTTP error.

The frontend calls the placement and whole-plan endpoints. The standalone `validate-semester` action is **exposed but currently unused by the frontend**; its service method is also used internally by whole-plan validation and directly by backend tests, so it is not dead code.

### Services

#### [`Services/AcademicYear.cs`](Services/AcademicYear.cs) — used

`AcademicYear` validates the numeric starting year and exposes `StartYear` for SDB operations and `Catalogue` (for example, `2026/2027`) for the course service. This is the single conversion boundary; plain-year strings are no longer passed to full-XML course search.

#### [`Services/IDtuGateway.cs`](Services/IDtuGateway.cs) — used

`IDtuGateway` is the testable boundary for the seven remote operations currently needed: batch course XML, educations, SDB volumes, course catalogue versions, StudyBoxes, visualization discovery, and visualization HTML. `DtuUnavailableException` distinguishes upstream failure from an absent course. The HTTP boundary maps it to a retryable 503 Problem Details response.

#### [`Services/DtuGateway.cs`](Services/DtuGateway.cs) — used

`DtuGateway` is the only handwritten service that constructs generated SOAP clients. `GetCoursesAsync` uses `SearchDtuShb_FullAsync` with a comma-separated code list, an explicit academic-year string, and `FullXML`. Its other methods use `GetEducationsInVolumeAsync`, `GetVolumesAsync`, `CourseCatalogVersionsAsync`, `GetStudyBoxInfoByVolumesAsync`, `GetVisualizationsInEducationAsync`, and `GetVisualizationAsync`.

Metadata and HTML methods use `DtuCache`; course caching belongs to the catalogue service. Every actual remote call creates its own client, configures timeouts, and closes or aborts it. `CallAsync` logs operation duration and retries a transient timeout/communication failure once with a fresh client. SOAP faults are not retried. Generated clients are never cached or shared between concurrent operations.

#### [`Services/DtuOptions.cs`](Services/DtuOptions.cs) — used

`DtuOptions` binds the `Dtu` section in appsettings. Defaults are 25 courses per batch, 2,048 cache entries, 60-minute successful-data retention, five-minute missing-course retention, and a 30-second operation timeout. Startup validates the settings. Cache contents are per process and are lost on restart.

#### [`Services/DtuCache.cs`](Services/DtuCache.cs) — used

`DtuCache` owns a size-limited memory cache and 64 semaphore stripes. `TryGet` and `Set` store typed data. `GetAsync` coordinates a cache miss so concurrent requests reuse the first completed fetch; failed factories are not cached. `WithLockAsync` lets course batches coordinate overlapping code sets for one academic year. Factories must not recursively acquire locks from this cache. `Dispose` releases cache and synchronization resources.

Course keys include year and code; catalogue/StudyBox keys include SDB volume; discovery keys include education ID; HTML keys include view, programme, volume, and language. Parsed-flow keys additionally include a content hash, avoiding stale parse results after refreshed HTML. Courses are normalized in English, so course-cache keys do not currently need a language dimension.

#### [`Services/CourseCatalogService.cs`](Services/CourseCatalogService.cs) — used

Defines `ICourseCatalogService` and `CourseCatalogService`. `GetCoursesForStudyPlanAsync` validates/deduplicates codes, reads cached results, batches uncached codes, parses every returned course, and accepts only requested codes in the exact requested academic year. It returns `CoursesResponse` in requested-code order and records genuine missing results for a shorter cache lifetime. Its private `CachedCourse` record distinguishes a cached absence from a cache miss.

The courses controller, programme definition, and selected-package import share this path. Upstream errors propagate as 503 rather than being converted into `MissingCourseCodes`. Successful earlier chunks can remain cached if a later chunk fails. There is no unconditional holdings call, per-course title lookup, or silent historical substitution. The public input limit is 500 codes per call; batch size is configurable separately.

#### [`Services/CourseXmlParser.cs`](Services/CourseXmlParser.cs) — used

`CourseXmlParser.Parse` converts one XML course element into `CourseSummary`, preserving the actual `SourceVolume` and reporting incomplete title/ECTS/exam metadata in `DataWarnings`. It has no network or cache dependencies.

`ParseCourseXml`, `ExtractTitle`, `ExtractCourseLevel`, `ExtractEcts`, and `ExtractScheduleText` read metadata, using English/Danish localization fallback and decimal-comma support. `ExtractTeachingTimeBlocks` and `NormalizeTeachingBlock` expand whole-day modules into A/B blocks and preserve evening/month modules. `ParsePlacementOptions` recognizes alternative Scheme/Skema schedules; `ParseExplicitScheduleTextTimeBlocks` supplies a text fallback. `ExtractExamMetadata` chooses a visible examination by sort ID and maps grading/examiner keys, with unknown values retained explicitly. XML traversal/localization helpers and the private `ParsedCourseData` class support these transformations. All are used by parsing.

#### [`Services/IVolumeResolver.cs`](Services/IVolumeResolver.cs) — used

`IVolumeResolver.ResolveAsync` is the abstraction used by the courses controller to validate an explicit year or discover a default.

#### [`Services/VolumeResolver.cs`](Services/VolumeResolver.cs) — used

`VolumeResolver.ResolveAsync` preserves a valid explicitly supplied year. If omitted, it reads cached course-catalogue settings and SDB volumes, finds their shared active years, and prefers the course catalogue's preferred year, then the SDB current year, then the latest year. No shared year produces an upstream-availability error instead of guessing from the calendar. Programme endpoints require an explicit volume.

#### [`Services/IStudyPlanValidator.cs`](Services/IStudyPlanValidator.cs) — used

`IStudyPlanValidator` declares placement, semester, and whole-plan validation using centralized model contracts. The controller consumes this interface; startup registers its stateless implementation as a singleton.

#### [`Services/StudyPlanValidator.cs`](Services/StudyPlanValidator.cs) — used

`ValidatePlacement` checks BSc approval of an MSc candidate, semester parity, overlapping teaching blocks, and programme capacities after appending the candidate. `ValidateSemester` checks parity and each course pair, then totals ECTS. `ValidatePlan` validates every populated semester and programme capacities; it does not repeat the placement-only MSc approval check.

`ValidateSemesterParity` maps E/F blocks to odd/even semesters. `ValidateProgrammeRequirements`, `CalculateRequirementTotals`, and private `RequirementTotals` handle total/bucket limits and programme/project spillover into electives. `ResolveCourseBucket` recognizes explicit categories, synthetic activities, and BEng mandatory codes. `BuildPlanConflict` and `GetBucketCourses` produce conflict details. These are upper-capacity checks, not proof of graduation eligibility. No DTU calls occur here. The unused `TryReadEcts` stub and unread intermediate properties have been removed.

#### [`Services/ProgrammeClassificationService.cs`](Services/ProgrammeClassificationService.cs) — used

Defines `IProgrammeClassificationService` and `ProgrammeClassificationService`. `ResolveAsync` tries the requested StudyBox volume, then the previous one if no matching classification exists, exposing the actual `SourceVolume`. The cached volume-wide dataset is shared across programme requests.

`ParseProgrammeClassificationFromStudyBoxes` dispatches to BSc/BEng/MSc parsers. Their strict label helpers match known Danish/English category prefixes against normalized programme aliases; BSc also extracts pre-approved MSc electives. `NormalizeStudyBoxCourseCodes` rejects malformed codes and deduplicates them. `BuildProgrammeAliases`, `EnumerateStudyBoxLabels`, `TryParseStrictProgrammeStudyBoxLabel`, and `NormalizeProgrammeName` support classification. This is structured-data classification; it does not infer requirement buckets from visualization HTML for unsupported degree levels.

#### [`Services/ProgrammeRules.cs`](Services/ProgrammeRules.cs) — used

`ProgrammeRules` centralizes configured degree defaults and view-discovery rules. `ResolveBucketLimits` returns BSc 180 ECTS (55 foundation, 55 programme-specific, 25 projects, 45 electives), MSc 120 (10, 50, 30, 30), and BEng 210 (135 mandatory, 30 electives, 30 internship, 15 projects). These are explicit application defaults, not programme-specific rules fetched from DTU.

`CoreVisualizationNames` identifies core views; `RecommendedVisualizationNames` and `IsRecommendedView` identify configured packages using exact names or a trailing wildcard. Package discovery currently recognizes `ELEKTEK23`'s `ELEKTRO_23_studieforløb` and `EL_*` views.

#### [`Services/ProgrammeVisualizationService.cs`](Services/ProgrammeVisualizationService.cs) — used

Defines `IProgrammeVisualizationService`, `ProgrammeVisualizationService`, `ProgramVisualizationMap`, and `VisualizationItem`. `GetProgramVisualizationMapAsync` obtains cached view metadata by education ID, maps/sorts localized identifiers, and applies `ProgrammeRules` to return core/package lists. It does not fetch view HTML. Unconsumed supporting/other-view collections and contextual fields have been removed.

#### [`Services/StudyFlowParser.cs`](Services/StudyFlowParser.cs) — used

`StudyFlowParser.Parse` uses AngleSharp to read supplied HTML without scripts or network access. It handles semester cards, timetable cells, and supplementary course tables using DOM selectors, tolerating attribute order, quote style, and wrapper changes. Text regexes identify course codes, semester numbers, and block tokens; markup is not parsed with regexes.

`SemesterNumber`, `CourseCode`, `Ects`, and `MapBucket` extract fields. `ElementText` preserves table-cell boundaries; `ParseBlocks` normalizes months/whole-day modules; `ToSemesterBlock` maps day/period cells, including evening modules. `ParsedStudyFlowPlan` and `ParsedStudyFlowPlacement` are used intermediate classes. Courses repeated in different semesters remain distinct; semesters 1–12 are supported. A plain classification table without semester headings yields no invented placements.

#### [`Services/GenericStudyFlowPresetLoader.cs`](Services/GenericStudyFlowPresetLoader.cs) — used conditionally

`IGenericStudyFlowPresetLoader.TryLoad` and `GenericStudyFlowPresetLoader` load the curated `ELEKTEK23` JSON plan. `ResolveGenericPlanPath` searches content-root/application-base directories and their parents. Unsupported programmes, missing files, or parse failures return false, with failures logged. Programme discovery uses this to advertise the available generic option; importing that option returns the saved plan. The duplicate embedded loader formerly in `ProgrammeService` has been removed.

#### [`Services/ProgrammeService.cs`](Services/ProgrammeService.cs) — used

Defines `IProgrammeService` and `ProgrammeService`. `GetProgrammesAsync` returns sorted programme choices from cached catalogue data. `ResolveProgrammeCatalogueAsync` retains requested/previous-year catalogue fallback; `FlattenProgrammes` uses generated `Education`/`Line` types instead of dynamic objects. `InferLevel` recognizes degree names, and the duplicate-alias helpers suppress replaced legacy BSc entries.

`GetProgrammeDefinitionAsync` assembles metadata, classification, enriched mandatory courses, source volumes, view references, and lightweight study-flow descriptors. `DescribeOptions` assigns `view-{id}` identifiers and optionally includes `generic-plan`. Discovery performs no package-HTML requests or package-course enrichment.

`GetStudyFlowAsync` validates that the requested option belongs to the programme, loads only that option, retries empty non-Danish HTML in Danish, reuses a parsed result by content hash, and batches course enrichment. `ResolvePlacementSelections` matches blocks to course alternatives. `BuildPresetPlannedCourses` preserves parsed buckets and metadata; `ResolveFallbackTimeBlocks`/`IsSemesterMonthBlock` select semester-compatible blocks. `ResolvePresetCourseEcts` retains the special zero-ECTS even-semester continuation of course 10060. Empty/unparseable options return an unavailable result. `ValidateLanguage` limits the public contract to da-DK/en-GB. Every remaining helper is used.

### API And Plan Models

These files contain data-only classes with initialized properties, not business methods. Every class listed here is **used**, whether instantiated by services, nested in other DTOs, or bound/serialized by ASP.NET Core. A property can be carried for frontend/save-file use without being inspected by the validator.

| File | Classes and purpose | Usage |
| --- | --- | --- |
| [`backend/Models/CoursePlacementOption.cs`](Models/CoursePlacementOption.cs) | `CoursePlacementOption`: an alternative schedule's `Id`, display `Label`, and `TimeBlocks`. | Built by catalogue parsing, nested in course summaries, and matched during study-flow import. |
| [`backend/Models/CourseSummary.cs`](Models/CourseSummary.cs) | `CourseSummary`: normalized catalogue code/title/level/ECTS, schedule text, grading/examiner modes, effective blocks, selected option and all alternatives, raw schedule keys, language, source volume, and incomplete-data warnings. | Returned by course loading and nested in mandatory-course definitions; also enriches preset plans. |
| [`backend/Models/CoursesResponse.cs`](Models/CoursesResponse.cs) | `CoursesResponse`: successful `Courses` plus `MissingCourseCodes`. | Catalogue service result consumed by the courses controller and programme service. |
| [`backend/Models/PlannedCourse.cs`](Models/PlannedCourse.cs) | `PlannedCourse`: a course/activity assigned to a semester, with chosen placement, ECTS, blocks, grading/examiner metadata, and optional `Kind`, `ActivityType`, `DisplayCode`, `ScheduleMode`, and requirement `Bucket`. | Common plan element used in requests, validation, and imported saved plans; activity/bucket fields support synthetic work and requirement accounting. |
| [`backend/Models/StudyPlan.cs`](Models/StudyPlan.cs) | `StudyPlan`: the list of `PlannedCourse` entries. | Shared container in every validation request and saved-plan payload. |
| [`backend/Models/PlacementResult.cs`](Models/PlacementResult.cs) | `PlacementResult`: `Allowed`, explanatory `Message`, conflicting course codes, and shared blocks. | Placement response and reusable conflict item in semester/whole-plan results. |
| [`backend/Models/PlannerRequests.cs`](Models/PlannerRequests.cs) | `PlacementRequest`: required plan/candidate plus programme level, approved MSc codes, mandatory codes, and optional limits. `ValidateSemesterRequest`: required plan and semester. `ValidatePlanRequest`: required plan plus programme level, mandatory codes, and optional limits. | Request-body contracts for the three planner actions. All three are bound by exposed routes; the frontend currently sends only placement and plan requests. |
| [`backend/Models/PlanValidationResult.cs`](Models/PlanValidationResult.cs) | `SemesterValidationResult`: semester number, allowed flag, ECTS total, and conflicts. `PlanValidationResult`: aggregate allowed flag, semester results, and programme-wide conflicts. | Produced by the validator and returned through planner endpoints. |
| [`backend/Models/ProgrammeClassification.cs`](Models/ProgrammeClassification.cs) | `ParsedProgrammeCourse` stores classified code/bucket/order; `ProgrammeStudyBoxClassification` stores classified courses, approved MSc codes, source volume and `HasData`. | Internal service results used by classification and response assembly. |
| [`backend/Models/ProgrammeModels.cs`](Models/ProgrammeModels.cs) | Programme catalogue, definition, bucket, visualization, and saved-plan contracts described below. | Shared by programme services, preset loading, validation context, and API serialization. |

`ProgrammeModels.cs` contains nine used classes:

- `ProgrammeListResponse` reports requested `Volume`, actual `ResolvedVolume`, and programme choices.
- `ProgrammeListItem` identifies an education/programme with its education ID/GUID, programme code/level, Danish/English names and popular titles, and language availability flags.
- `ProgrammeDefinitionResponse` combines the selected programme, volumes/language, bucket limits, mandatory courses, approved MSc electives, visualization references, recommended-package references, lightweight study-flow descriptors, classification source volume, missing codes, and notes.
- `ProgrammeBucketLimits` carries total and category ECTS capacities; mandatory/internship limits are nullable for degree structures that do not use them. It is both response metadata and validator input.
- `ProgrammeMandatoryCourse` attaches a bucket, bucket label, and source order to an enriched `CourseSummary`.
- `ProgrammeVisualizationReference` exposes a view's name, numeric ID, and GUID.
- `ProgrammeStudyFlowDescriptor` describes an import choice (ID, label, description, kind, optional visualization identity) without loading a saved plan.
- `ProgrammeStudyFlowOption` extends that descriptor with its required saved plan and missing-course codes, returned only on import.
- `SavedStudyPlanDto` represents the versioned import/export shape: timestamp, volume string, selected placement IDs by code, imported codes, and required `StudyPlan`. Both JSON preset loading and generated study flows use it; it is not a database entity.

### Generated SOAP Source

All four generated files remain **used build inputs**. `DtuGateway` alone constructs their clients, calls the operations below, and closes/aborts each client. Other operations, DTOs needed only by those operations, alternative endpoints/constructors, and generated channel interfaces are retained proxy scaffolding with no direct application callers. Do not hand-edit generated sources to remove unused operations.

| File | Used classes and operations | Operations/types not consumed by application paths |
| --- | --- | --- |
| [`RefCourseSoap/Reference.cs`](RefCourseSoap/Reference.cs) | `CourseSoap` / `CourseSoapClient`, SOAP 1.2; `SearchDtuShb_FullAsync` returns batch FullXML; `CourseCatalogVersionsAsync` discovers available academic-year settings. XML payloads are parsed by handwritten services. | All other operations, including holdings, historical lookup, titles, and `GetDataForStudyplan`; `CourseTitle`, `CourseTitleAndVolume`, `CourseExamPlanner`, teaching-hours wrappers, and `CourseSoapChannel` are no longer on active request paths. |
| [`RefVolumes/Reference.cs`](RefVolumes/Reference.cs) | `IVolumeService` / `VolumeServiceClient`; `GetEducationsInVolumeAsync` and `GetVolumesAsync`. `Education`, `Line`, `LanguageItem`, and `Volume` carry metadata/default-year discovery. | Draft education and general-view operations; `GeneralView` and channel scaffolding. |
| [`RefVisualizations/Reference.cs`](RefVisualizations/Reference.cs) | `IVisualizationService` / `VisualizationServiceClient`; discovery through `GetVisualizationsInEducationAsync`, HTML through `GetVisualizationAsync`; `Visualization` / `LanguageItem` are used payloads. | GUID-based, draft, and general visualization operations and channel scaffolding. GUID lookup would still return HTML. |
| [`RefCourseblocks/Reference.cs`](RefCourseblocks/Reference.cs) | `ICourseblockService` / `CourseblockServiceClient`; `GetStudyBoxInfoByVolumesAsync` returns `StudyBoxInfo` for programme classification. | Other block/association/filtered/first-term operations; `Courseblock`, `StudyleaderCourseAssociation`, `FirstTermCollection`, `CollectionMetadata`, `Course`, and channel scaffolding. |

### Backend Supporting Files

These are not C# source, but control how the backend builds, starts, or is maintained.

| File | Purpose and usage |
| --- | --- |
| [`backend/backend.csproj`](backend.csproj) | **Used by build/publish.** ASP.NET Core `net10.0` project with nullable types and implicit usings, WCF and AngleSharp dependencies, assembly/product/icon settings, and default `UseAppHost=false`. Excludes nested backend and artifact sources/content to avoid compiling generated build output. |
| [`backend/Directory.Build.props`](Directory.Build.props) | **Used automatically by MSBuild.** Redirects intermediate/output paths to `backend/artifacts/msbuild/<project>/obj` and `bin`. |
| [`backend/appsettings.json`](appsettings.json) | **Used at runtime.** Base logging, allowed-host, local URL, browser-launch configuration, and validated DTU batching/cache/timeout settings. |
| [`backend/appsettings.Development.json`](appsettings.Development.json) | **Used in Development.** Overrides local-app/logging settings; currently repeats the corresponding base values. |
| [`backend/Properties/launchSettings.json`](Properties/launchSettings.json) | **Used by local launch tooling.** HTTP and HTTPS profiles set Development, ports, and disabled automatic browser launch. It does not configure a deployed server. |
| [`backend/dotnet-tools.json`](dotnet-tools.json) | **Maintenance only.** Pins `dotnet-svcutil` 8.0.0 for proxy generation; not application runtime code. |
| [`backend/RefCourseSoap/dotnet-svcutil.params.json`](RefCourseSoap/dotnet-svcutil.params.json) | **Regeneration only.** Records the course ASMX WSDL, XML serializer choice, target framework, and `Reference.cs` output. |
| [`backend/RefVolumes/dotnet-svcutil.params.json`](RefVolumes/dotnet-svcutil.params.json) | **Regeneration only.** Records the volume-service WSDL and `Planner.Backend.Soap.Volumes` namespace mapping. |
| [`backend/RefVisualizations/dotnet-svcutil.params.json`](RefVisualizations/dotnet-svcutil.params.json) | **Regeneration only.** Records the visualization-service WSDL and `Planner.Backend.Soap.Visualizations` namespace mapping. |
| [`backend/RefCourseblocks/dotnet-svcutil.params.json`](RefCourseblocks/dotnet-svcutil.params.json) | **Regeneration only.** Records the courseblock-service WSDL and `Planner.Backend.Soap.Courseblocks` namespace mapping. |
| [`backend/backend.http`](backend.http) | **Manual API examples; not runtime code.** Covers course batches, programme discovery, and on-demand study-flow import. |
| [`backend/wwwroot/.gitkeep`](wwwroot/.gitkeep) | **Repository placeholder.** Keeps the static-content directory tracked; has no application logic. Packaging/deployment puts the built frontend here for startup's static-file middleware. |

No handwritten C# file is intentionally unused. The obsolete embedded preset loader, validator stub, unread intermediate fields, and unused view categories were removed. Generated unused operations remain as described above.

### Verification And Behavioral Boundaries

`backend.Tests` is a console regression suite covering course batching/cache fills, exact-year filtering, failure-vs-missing behavior, normalization, programme classification, default-year resolution, lazy imports, and DOM parsing. Reduced live course responses and two actual ELEKTEK23 HTML views live under `backend.Tests/Fixtures`; their README records provenance and limitations. Frontend tests cover the new import route and upstream-error handling.

A live smoke test returned 21 curated-plan courses in one SOAP batch. Programme selection fetched no package HTML; one selected package imported 26 placements. A repeated three-code HTTP batch made no new SOAP request. These are observed checks, not latency guarantees.

The course search currently exposes the published catalogue; the captured 2025/2026 request returned no courses. When `/api/courses` receives `allowHistoricalFallback=true` (enabled by the frontend HoS extra info toggle), empty search batches fall back to individual `GetCourse` requests for the exact academic year. Both paths reject mismatched years, and their cache entries are separate. Without the flag, unavailable courses remain missing. View names do not guarantee importable semester data; unavailable options produce a clear response. Requirement capacities remain application defaults, and package discovery remains strongest for ELEKTEK23.
