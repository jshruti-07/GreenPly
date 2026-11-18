#!/bin/bash
# =============================================================================
# Network URL.command  —  READ-ONLY
#
# Works out the address another machine on the same LAN would use to reach the
# PIM frontend, and reports what currently stands in the way.
#
# Nothing is started, stopped or modified. Output: network-log.txt
# =============================================================================

set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }
exec > >(tee "network-log.txt") 2>&1

BOLD=$'\033[1m'; RESET=$'\033[0m'
RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'
sec()  { printf '\n%s== %s ==%s\n' "$BOLD" "$1" "$RESET"; }
ok()   { printf '  %sPASS%s  %s\n' "$GREEN" "$RESET" "$1"; }
bad()  { printf '  %sFAIL%s  %s\n' "$RED" "$RESET" "$1"; }
warn() { printf '  %sWARN%s  %s\n' "$YELLOW" "$RESET" "$1"; }

FRONTEND_PORT=5174
API_PORT=5100

echo "Network reachability — $(date)"
echo "Host: $(scutil --get LocalHostName 2>/dev/null || hostname)"

sec "This machine's LAN addresses"
FOUND_IP=""
# Walk real interfaces in service order; skip loopback, Docker bridges and
# utun/VPN tunnels, which are not what a peer on the LAN will reach.
for iface in $(networksetup -listallhardwareports 2>/dev/null \
               | awk '/Device:/ {print $2}'); do
  ip=$(ipconfig getifaddr "$iface" 2>/dev/null)
  [ -z "$ip" ] && continue
  hwport=$(networksetup -listallhardwareports 2>/dev/null \
           | awk -v d="$iface" '/Hardware Port:/{p=$0} $2==d {sub("Hardware Port: ","",p); print p; exit}')
  printf '  %-6s %-18s %s\n' "$iface" "$ip" "${hwport:-}"
  [ -z "$FOUND_IP" ] && FOUND_IP="$ip"
done
[ -n "$FOUND_IP" ] || bad "no LAN address found — is this machine on Wi-Fi or Ethernet?"

sec "What port $FRONTEND_PORT is bound to"
# The distinction that matters: 127.0.0.1 accepts only local connections,
# *.5174 (or 0.0.0.0) accepts connections from the network.
BIND=$(lsof -nP -iTCP:"$FRONTEND_PORT" -sTCP:LISTEN 2>/dev/null | awk 'NR>1 {print $1, $9}' | sort -u)
if [ -z "$BIND" ]; then
  bad "nothing is listening on $FRONTEND_PORT — the dev server is not running"
  EXPOSED=0
else
  printf '%s\n' "$BIND" | sed 's/^/  /'
  if printf '%s' "$BIND" | grep -E '(\*:|0\.0\.0\.0:)'"$FRONTEND_PORT" >/dev/null 2>&1; then
    ok "bound to all interfaces — reachable from the LAN"
    EXPOSED=1
  else
    warn "bound to loopback only — other machines CANNOT reach it"
    echo "        Vite needs --host to listen on the network."
    EXPOSED=0
  fi
fi

sec "API port $API_PORT"
APIBIND=$(lsof -nP -iTCP:"$API_PORT" -sTCP:LISTEN 2>/dev/null | awk 'NR>1 {print $1, $9}' | sort -u)
printf '%s\n' "${APIBIND:-  (nothing listening)}" | sed 's/^/  /'
if printf '%s' "$APIBIND" | grep -E '(\*:|0\.0\.0\.0:)'"$API_PORT" >/dev/null 2>&1; then
  warn "the API itself is exposed to the LAN. It has no authentication."
else
  ok "API is loopback-only — peers reach it only through the dev server's /api proxy"
fi

sec "macOS application firewall"
FW=$(/usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate 2>/dev/null)
echo "  ${FW:-unknown}"
if printf '%s' "$FW" | grep -i "enabled" >/dev/null 2>&1; then
  warn "firewall is on — the first LAN bind may raise an 'Accept incoming connections?' dialog.
        Answer Allow, or the port stays unreachable from other machines."
fi

sec "Verdict"
if [ -z "$FOUND_IP" ]; then
  bad "no LAN address — cannot form a network URL"
elif [ "$EXPOSED" = "1" ]; then
  printf '  %sOpen this on the other laptop:%s  http://%s:%s/\n' \
         "$BOLD" "$RESET" "$FOUND_IP" "$FRONTEND_PORT"
else
  printf '  Address to use once exposed:  %shttp://%s:%s/%s\n' \
         "$BOLD" "$FOUND_IP" "$FRONTEND_PORT" "$RESET"
  echo "  Not reachable yet — restart the frontend with --host."
  echo "  'Run PIM (LAN).command' does exactly that."
fi

echo
printf '%sNothing was changed. Output: network-log.txt%s\n' "$BOLD" "$RESET"
