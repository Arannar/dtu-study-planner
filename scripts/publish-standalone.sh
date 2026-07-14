#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-}"
configuration="${CONFIGURATION:-Release}"

if [[ -z "$runtime" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) runtime="linux-x64" ;;
    Linux-aarch64|Linux-arm64) runtime="linux-arm64" ;;
    Darwin-x86_64) runtime="osx-x64" ;;
    Darwin-arm64) runtime="osx-arm64" ;;
    MINGW*|MSYS*|CYGWIN*) runtime="win-x64" ;;
    *) runtime="linux-x64" ;;
  esac
fi

frontend_root="$repo_root/frontend"
backend_root="$repo_root/backend"
frontend_build="$frontend_root/build"
backend_wwwroot="$backend_root/wwwroot"
publish_root="$repo_root/artifacts/publish/$runtime"

(
  cd "$frontend_root"
  npm run build
)

rm -rf "$backend_wwwroot"
mkdir -p "$backend_wwwroot"
cp -R "$frontend_build"/. "$backend_wwwroot"/

(
  cd "$backend_root"
  dotnet publish backend.csproj \
    --configuration "$configuration" \
    --runtime "$runtime" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:UseAppHost=true \
    --output "$publish_root"
)

echo "Standalone package written to $publish_root"
