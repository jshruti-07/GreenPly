#!/bin/bash
# Double-clickable local run: catalog API + frontend dev server.
# Close this Terminal window (or Ctrl+C) to stop both.
cd "$(dirname "$0")" || exit 1

# .command shells may lack the login PATH — probe known dotnet homes.
if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
command -v dotnet >/dev/null || { echo "dotnet not found — run 'Verify PIM.command' first"; exit 1; }

# A previous run's API keeps serving on this port and the new one fails to
# bind silently — the UI then talks to a stale build, which has already caused
# one "blank screen" incident (2026-08-14). Reclaim the port, but only from
# our own API process.
reclaim_port() { # port, owner-pattern, label
  local port="$1" pattern="$2" label="$3" pid owner
  for pid in $(lsof -nP -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null | sort -u); do
    owner=$(ps -p "$pid" -o command= 2>/dev/null)
    case "$owner" in
      *$pattern*)
        echo "Stopping stale $label (pid $pid) so this run serves the current build"
        kill "$pid" 2>/dev/null
        ;;
      *)
        echo "FATAL: port $port is held by an unrelated process (pid $pid):"
        echo "  $owner"
        exit 1
        ;;
    esac
  done
  for _ in $(seq 1 15); do
    lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1 || break
    sleep 1
  done
  if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "FATAL: port $port still occupied."
    exit 1
  fi
}

# Both ports: a leftover API serves a stale build, and a leftover vite makes
# the new dev server exit silently (both observed 2026-08-14).
reclaim_port 5100 "Pim.Catalog.Api" "catalog API"
reclaim_port 5174 "vite" "frontend dev server"

echo "== Infra (postgres :15432, rabbitmq, redis :6380) =="
(cd infra && docker compose up -d) || exit 1

# API on 5100: macOS AirPlay squats on 5000; other local stacks use nearby
# ports. Development env is required (no launchSettings.json in repo).
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5100

echo "== Starting catalog API on :5100 (log: api.log) =="
(cd services/catalog && dotnet run --project src/Pim.Catalog.Api > ../../api.log 2>&1) &
API_PID=$!
trap 'kill $API_PID 2>/dev/null' EXIT

# Fail loudly if the API could not start, rather than proceeding to a UI that
# will talk to nothing (or worse, to a stale instance).
printf "Waiting for API health"
for _ in $(seq 1 45); do
  if curl -sf http://localhost:5100/healthz >/dev/null 2>&1; then
    echo " — UP (Swagger: http://localhost:5100/swagger)"
    break
  fi
  printf "."
  sleep 2
done
curl -sf http://localhost:5100/healthz >/dev/null 2>&1 || {
  echo; echo "API failed to start — last log lines:"; tail -20 api.log; exit 1;
}

echo "== Starting frontend on :5174 (5173 is used by another project) =="
cd frontend || { echo "FATAL: no frontend directory"; exit 1; }

# A fresh clone has no node_modules, and this script used to call 'npm run dev'
# directly — vite is not on disk, so the API came up and the UI died one line
# later (observed on the 2026-08-21 clone). Install from the lockfile when the
# tree is missing. 'npm ci' rather than 'install': it reproduces exactly the
# tree the Sprint gate verified and fails loudly if package.json and the
# lockfile have drifted, instead of silently resolving something new.
if [ ! -x node_modules/.bin/vite ]; then
  [ -f package-lock.json ] || {
    echo "FATAL: node_modules is missing and there is no package-lock.json to"
    echo "       install from. Restore the lockfile from git."
    exit 1
  }
  echo "-- node_modules missing: npm ci (first run takes a minute) --"
  npm ci || { echo "FATAL: npm ci failed — cannot start the frontend"; exit 1; }
fi

# Opened only after any install, so the browser does not race a cold npm ci
# and land on a connection-refused page.
( sleep 4 && open http://localhost:5174 ) &
npm run dev -- --port 5174 --strictPort
