# DTU regression fixtures

Captured from public SOAP services during the September 2026 backend refactor. Tests run entirely offline.

- `courses-2026-2027.xml`: `SearchDtuShb_Full`, `FullXML`, the 21 codes in the repository's curated plan plus invalid code `99999`. All 21 real courses were returned. Only course attributes, titles, ECTS, level, schedules, and exam grading/examiner fields were retained; descriptions and staff details were removed. Covers alternative schemes, ordinary blocks, intensive months, and multi-semester courses.
- `courses-2025-2026.xml`: the same search for `01001,10060,99999` in `2025/2026` returned an empty root. This records publication availability at capture time, not a claim that those courses never existed. The service must not fill this gap with a different year's data.
- `study-flow-4826.html`: visualization 4826 for `ELEKTEK23`, volume 2026, `da-DK`. Despite the discovery name `Studieforløb`, the captured HTML is a course classification listing, not a semester layout. Expected parse result: no placements.
- `study-flow-5302.html`: visualization 5302 (`EL_Atuo_23`) for the same programme, volume, and language. Semester cards provide 26 course placements, including a two-semester continuation of course 10060.

The tests also vary markup attributes/wrappers and use a small synthetic timetable covering months, evening modules, and semesters seven/eight. Network failures and concurrent requests are simulated through `IDtuGateway`.

To refresh course fixtures, use the same exact-year full-XML query and remove non-planner fields before committing. Review semantic expectations instead of replacing fixtures merely to make tests pass. Visualization fixtures should retain their complete DOM structure because it is the parser's input contract.
