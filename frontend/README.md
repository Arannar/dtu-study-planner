# Study Planner Frontend

SvelteKit/Svelte 5 frontend for the Study Planner workspace.

Run from this directory:

```powershell
npm install
npm run dev
npm run check
npm run lint
npm run build
```

The development frontend expects the backend at `http://localhost:5140` unless `VITE_API_BASE_URL` is set. Production builds use same-origin API calls so the ASP.NET Core backend can serve the static app.
