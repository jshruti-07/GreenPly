#!/bin/bash
# =============================================================================
# Setup Mac.command  —  one-time bootstrap of a macOS machine for PIM platform
#
# Installs ONLY what is missing:
#   .NET SDK 10.x  -> $HOME/.dotnet          (official dotnet-install.sh, no sudo)
#   Node 22.x      -> Homebrew if available, else $HOME/.local/node22 (no sudo)
#
# Verifies but NEVER installs:
#   Docker Desktop -> must be installed and RUNNING (GUI app + licence
#                     acceptance cannot be automated safely from a script)
#
# Then prepares the repo:
#   frontend            npm ci
#   services/catalog    dotnet restore / build / test
#
# Idempotent: safe to re-run. Full transcript -> setup-log.txt
#
# Usage:  double-click, or:  ./Setup\ Mac.command [--yes] [--check-only]
#           --yes         non-interactive; assume "yes" to install prompts
#           --check-only  report what is missing, change nothing
# =============================================================================

set -uo pipefail

cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
REPO_ROOT="$PWD"
LOG="$REPO_ROOT/setup-log.txt"

ASSUME_YES=0
CHECK_ONLY=0
for arg in "$@"; do
  case "$arg" in
    --yes|-y)     ASSUME_YES=1 ;;
    --check-only) CHECK_ONLY=1 ;;
    *) echo "Unknown argument: $arg"; exit 2 ;;
  esac
done

# ---------------------------------------------------------------- logging ---
exec > >(tee "$LOG") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'

step() { printf '\n%s== %s ==%s\n' "$BOLD" "$1" "$RESET"; }
ok()   { printf '  %sPASS%s  %s\n' "$GREEN" "$RESET" "$1"; }
warn() { printf '  %sWARN%s  %s\n' "$YELLOW" "$RESET" "$1"; }
bad()  { printf '  %sFAIL%s  %s\n' "$RED" "$RESET" "$1"; }
fatal() { printf '\n%sFATAL:%s %s\n' "$RED" "$RESET" "$1"; exit 1; }

confirm() { # prompt -> 0 yes / 1 no
  [ "$ASSUME_YES" -eq 1 ] && return 0
  local reply
  printf '  %s [y/N] ' "$1"
  read -r reply < /dev/tty || return 1
  case "$reply" in [yY]|[yY][eE][sS]) return 0 ;; *) return 1 ;; esac
}

require_network() {
  curl -fsS --max-time 10 -o /dev/null https://dot.net 2>/dev/null \
    || fatal "No network access to the internet. Installing the SDK, Node
       packages, NuGet packages and Docker images all require it."
}

# ------------------------------------------------------------ environment ---
step "Machine"
MACOS_VER=$(sw_vers -productVersion 2>/dev/null || echo "unknown")
ARCH=$(uname -m)
echo "  macOS      : $MACOS_VER"
echo "  Arch       : $ARCH"
echo "  Repo       : $REPO_ROOT"
echo "  Started    : $(date)"

case "$ARCH" in
  arm64)  NODE_ARCH="darwin-arm64" ;;
  x86_64) NODE_ARCH="darwin-x64"   ;;
  *) fatal "Unsupported architecture '$ARCH'. Expected arm64 or x86_64." ;;
esac

[ -f "$REPO_ROOT/services/catalog/Pim.Catalog.sln" ] \
  || fatal "This does not look like the pim-platform repo root
       (services/catalog/Pim.Catalog.sln not found). Put this script next to
       'Run PIM.command' and try again."

# ------------------------------------------------------------- .NET SDK ----
# global.json pins sdk 10.0.100 with rollForward=latestFeature, so any
# 10.0.1xx (or later feature band within 10.0) satisfies the build.
DOTNET_MIN_BAND=100
NEED_DOTNET=1
DOTNET_BIN=""

find_dotnet() {
  local d
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN=$(command -v dotnet); return 0
  fi
  for d in "$HOME/.dotnet" /usr/local/share/dotnet /opt/homebrew/opt/dotnet/libexec; do
    if [ -x "$d/dotnet" ]; then DOTNET_BIN="$d/dotnet"; return 0; fi
  done
  return 1
}

dotnet_band_ok() { # reads `dotnet --list-sdks`, true if a usable 10.0.1xx+ exists
  "$DOTNET_BIN" --list-sdks 2>/dev/null | awk -v min="$DOTNET_MIN_BAND" '
    { split($1, v, "."); if (v[1]==10 && v[2]==0 && v[3]+0 >= min) found=1 }
    END { exit found ? 0 : 1 }'
}

step ".NET SDK 10"
if find_dotnet && dotnet_band_ok; then
  ok "found $("$DOTNET_BIN" --version) at $DOTNET_BIN"
  NEED_DOTNET=0
elif [ -n "$DOTNET_BIN" ]; then
  warn "dotnet present at $DOTNET_BIN but no SDK satisfies global.json (10.0.$DOTNET_MIN_BAND+)"
  echo "        installed SDKs:"
  "$DOTNET_BIN" --list-sdks 2>/dev/null | sed 's/^/          /' || echo "          (none)"
else
  bad "dotnet not found"
fi

if [ "$NEED_DOTNET" -eq 1 ] && [ "$CHECK_ONLY" -eq 0 ]; then
  if confirm "Install .NET SDK channel 10.0 into \$HOME/.dotnet (no sudo)?"; then
    require_network
    TMP_INSTALL=$(mktemp -d) || fatal "cannot create temp dir"
    curl -fsSL --retry 3 --retry-delay 2 -o "$TMP_INSTALL/dotnet-install.sh" \
      https://dot.net/v1/dotnet-install.sh \
      || fatal "Could not download dotnet-install.sh from https://dot.net/v1/"
    chmod +x "$TMP_INSTALL/dotnet-install.sh"
    "$TMP_INSTALL/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet" \
      || fatal "dotnet-install.sh failed. See output above."
    DOTNET_BIN="$HOME/.dotnet/dotnet"
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
    [ -x "$DOTNET_BIN" ] || fatal "dotnet still not executable at $DOTNET_BIN"
    dotnet_band_ok || fatal "Installed SDK does not satisfy global.json (needs 10.0.$DOTNET_MIN_BAND+).
       Installed: $("$DOTNET_BIN" --list-sdks | tr '\n' ' ')"
    ok "installed $("$DOTNET_BIN" --version)"
    NEED_DOTNET=0
  else
    warn "skipped — the backend cannot build without it"
  fi
fi

if [ -n "$DOTNET_BIN" ]; then
  DOTNET_DIR_RESOLVED=$(dirname "$DOTNET_BIN")
  export PATH="$DOTNET_DIR_RESOLVED:$PATH"
  export DOTNET_ROOT="$DOTNET_DIR_RESOLVED"
fi

# ----------------------------------------------------------------- Node ----
NODE_MIN_MAJOR=22
NEED_NODE=1

node_major() { node -v 2>/dev/null | sed 's/^v//' | cut -d. -f1; }

step "Node $NODE_MIN_MAJOR+"
if command -v node >/dev/null 2>&1; then
  NV=$(node_major)
  if [ -n "$NV" ] && [ "$NV" -ge "$NODE_MIN_MAJOR" ] 2>/dev/null; then
    ok "found $(node -v) at $(command -v node), npm $(npm -v 2>/dev/null || echo '?')"
    NEED_NODE=0
  else
    bad "node $(node -v 2>/dev/null) is older than v$NODE_MIN_MAJOR"
  fi
else
  bad "node not found"
fi

install_node_tarball() {
  require_network
  local base="https://nodejs.org/dist/latest-v${NODE_MIN_MAJOR}.x"
  local sums file sum tmp dest
  sums=$(curl -fsSL --retry 3 "$base/SHASUMS256.txt") \
    || fatal "Could not fetch $base/SHASUMS256.txt"
  file=$(printf '%s\n' "$sums" | awk -v a="$NODE_ARCH" \
          '$2 ~ ("^node-v[0-9.]+-" a "\\.tar\\.gz$") {print $2; exit}')
  sum=$(printf '%s\n' "$sums" | awk -v f="$file" '$2==f {print $1; exit}')
  [ -n "$file" ] && [ -n "$sum" ] \
    || fatal "No $NODE_ARCH tarball listed at $base — cannot install Node unattended."

  tmp=$(mktemp -d) || fatal "cannot create temp dir"
  echo "  downloading $file"
  curl -fsSL --retry 3 -o "$tmp/$file" "$base/$file" \
    || fatal "Download failed: $base/$file"
  printf '%s  %s\n' "$sum" "$tmp/$file" | shasum -a 256 -c - \
    || fatal "SHA-256 mismatch on $file — refusing to install."

  mkdir -p "$HOME/.local" || fatal "cannot create $HOME/.local"
  dest="$HOME/.local/node${NODE_MIN_MAJOR}"
  # Extract to a staging dir first so a failed extract never half-replaces
  # a working install.
  tar -xzf "$tmp/$file" -C "$tmp" || fatal "Extract failed for $file"
  local unpacked="$tmp/${file%.tar.gz}"
  [ -x "$unpacked/bin/node" ] || fatal "Unexpected tarball layout: no bin/node in $unpacked"
  if [ -d "$dest" ]; then
    mv "$dest" "${dest}.old.$(date +%s)" || fatal "cannot move aside existing $dest"
  fi
  mv "$unpacked" "$dest" || fatal "cannot move Node into $dest"
  export PATH="$dest/bin:$PATH"
  ok "installed $(node -v) at $dest"
}

if [ "$NEED_NODE" -eq 1 ] && [ "$CHECK_ONLY" -eq 0 ]; then
  if command -v brew >/dev/null 2>&1; then
    if confirm "Install Node $NODE_MIN_MAJOR via Homebrew (brew install node@$NODE_MIN_MAJOR)?"; then
      require_network
      brew install "node@$NODE_MIN_MAJOR" || fatal "brew install node@$NODE_MIN_MAJOR failed"
      # Versioned formulae are keg-only: they are not linked onto PATH.
      BREW_NODE="$(brew --prefix)/opt/node@$NODE_MIN_MAJOR/bin"
      [ -x "$BREW_NODE/node" ] || fatal "Homebrew reported success but $BREW_NODE/node is missing"
      export PATH="$BREW_NODE:$PATH"
      ok "installed $(node -v) at $BREW_NODE"
      NEED_NODE=0
    else
      warn "skipped — the frontend cannot start without it"
    fi
  else
    echo "  Homebrew not present. Node can be installed into \$HOME/.local"
    echo "  from the official nodejs.org tarball (checksum-verified, no sudo)."
    if confirm "Install Node $NODE_MIN_MAJOR that way?"; then
      install_node_tarball
      NEED_NODE=0
    else
      warn "skipped — the frontend cannot start without it"
    fi
  fi
fi

# --------------------------------------------------------------- Docker ----
# Deliberately NOT auto-installed: Docker Desktop is a GUI application whose
# first launch requires accepting a licence and granting privileged helper
# rights. Scripting that would be unreliable and is a poor security posture.
step "Docker"
DOCKER_OK=0
if ! command -v docker >/dev/null 2>&1; then
  bad "docker CLI not found — Docker Desktop is not installed"
  echo "        Install it from https://www.docker.com/products/docker-desktop/"
  echo "        (or via Homebrew — run 'brew search docker' to get the current"
  echo "         cask name, which has changed between Homebrew releases),"
  echo "        launch it once, accept the terms, then re-run this script."
elif ! docker info >/dev/null 2>&1; then
  bad "docker CLI found but the daemon is not responding"
  echo "        Start Docker Desktop and wait for 'Engine running', then re-run."
else
  ok "docker engine responding ($(docker --version))"
  DOCKER_OK=1
fi

# ----------------------------------------------------------- check-only -----
if [ "$CHECK_ONLY" -eq 1 ]; then
  step "Check-only mode — nothing was installed"
  exit 0
fi

# --------------------------------------------------- frontend dependencies --
step "Frontend dependencies"
if [ "$NEED_NODE" -eq 1 ]; then
  warn "skipped: Node is not available"
elif [ ! -f "$REPO_ROOT/frontend/package-lock.json" ]; then
  fatal "frontend/package-lock.json missing — refusing to run 'npm install'
       against an unpinned tree. Restore the lockfile from git."
else
  # 'npm ci' (not 'install'): reproduces exactly the tree the Sprint gate
  # verified, and fails loudly if package.json and the lockfile disagree.
  ( cd "$REPO_ROOT/frontend" && npm ci ) || fatal "npm ci failed — see output above."
  ok "node_modules installed from lockfile"
fi

# ---------------------------------------------------- backend restore/build -
step "Backend restore / build / test"
if [ "$NEED_DOTNET" -eq 1 ]; then
  warn "skipped: .NET SDK 10 is not available"
else
  ( cd "$REPO_ROOT/services/catalog" && dotnet restore && dotnet build --no-restore ) \
    || fatal "Backend restore/build failed — see output above.
       Note: README records that Sprint 15 (Outbox/) has never been compiled;
       if the failure is in that area, that is the known gap, not your setup."
  ok "catalog service builds"

  # Pim.Catalog.Integration.Tests drives Testcontainers, which needs a live
  # Docker daemon. Running it without one produces ~49 connection failures
  # that look like product defects but are pure environment noise, so the
  # suite is narrowed to the unit projects when Docker is down.
  if [ "$DOCKER_OK" -eq 1 ]; then
    ( cd "$REPO_ROOT/services/catalog" && dotnet test --no-build ) \
      || fatal "Backend tests failed — see output above."
    ok "catalog tests green (unit + integration)"
  else
    warn "Docker is down: running unit test projects only, skipping Testcontainers integration tests"
    ( cd "$REPO_ROOT/services/catalog" \
        && dotnet test --no-build tests/Pim.Catalog.Domain.Tests \
        && dotnet test --no-build tests/Pim.Catalog.Application.Tests ) \
      || fatal "Backend unit tests failed — see output above."
    ok "catalog unit tests green (integration tests NOT run — Docker required)"
  fi
fi

# --------------------------------------------------------------- summary ----
step "Summary"
READY=1
if [ "$NEED_DOTNET" -eq 1 ]; then bad ".NET SDK 10";  READY=0; else ok ".NET SDK 10"; fi
if [ "$NEED_NODE"   -eq 1 ]; then bad "Node $NODE_MIN_MAJOR+"; READY=0; else ok "Node $NODE_MIN_MAJOR+"; fi
if [ "$DOCKER_OK"   -eq 0 ]; then bad "Docker engine"; READY=0; else ok "Docker engine"; fi

# Persist PATH for future double-clicked .command runs. Marked block, rewritten
# in place on re-run rather than appended again.
if [ "$READY" -eq 1 ]; then
  ZPROFILE="$HOME/.zprofile"
  MARK_BEGIN="# >>> pim-platform path >>>"
  MARK_END="# <<< pim-platform path <<<"
  NODE_DIR=$(dirname "$(command -v node)")
  DOTNET_DIR=$(dirname "$(command -v dotnet)")
  TMP_Z=$(mktemp) || fatal "cannot create temp file"
  if [ -f "$ZPROFILE" ]; then
    awk -v b="$MARK_BEGIN" -v e="$MARK_END" '
      $0==b {skip=1} !skip {print} $0==e {skip=0}' "$ZPROFILE" > "$TMP_Z"
  fi
  {
    echo "$MARK_BEGIN"
    echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    echo "export PATH=\"$DOTNET_DIR:$NODE_DIR:\$PATH\""
    echo "$MARK_END"
  } >> "$TMP_Z"
  mv "$TMP_Z" "$ZPROFILE" && ok "PATH recorded in ~/.zprofile (marked block)"
fi

echo
if [ "$READY" -eq 1 ]; then
  printf '%sSetup complete.%s Log: setup-log.txt\n' "$GREEN$BOLD" "$RESET"
  echo
  echo "Next: 'Run PIM.command' starts infra (postgres :15432, rabbitmq, redis :6380),"
  echo "the catalog API on :5100 (Swagger /swagger) and the frontend on :5174."
  echo "First run also pulls the postgres/rabbitmq/redis images — allow a few minutes."
  if confirm "Start the application now?"; then
    exec "$REPO_ROOT/Run PIM.command"
  fi
else
  printf '%sSetup incomplete%s — resolve the FAIL lines above and re-run.\n' "$RED$BOLD" "$RESET"
  echo "Nothing was started. Log: setup-log.txt"
  exit 1
fi
