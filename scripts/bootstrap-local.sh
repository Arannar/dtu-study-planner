#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

rm -rf "$repo_root/frontend/node_modules"
rm -rf "$repo_root/frontend/.svelte-kit"
rm -rf "$repo_root/frontend/.svelte-kit-local"
rm -rf "$repo_root/backend/bin" "$repo_root/backend/obj" "$repo_root/backend/artifacts"
rm -rf "$repo_root/backend/SoapExplorationData" "$repo_root/artifacts"

(
  cd "$repo_root/frontend"
  npm install
)

(
  cd "$repo_root/backend"
  dotnet restore
)

echo "Local dependencies restored for $(uname -s) $(uname -m)."
