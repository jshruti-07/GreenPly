#!/bin/bash
# =============================================================================
# Setup and Run PIM.command
#
# Non-interactive wrapper: runs 'Setup Mac.command --yes' (installs any missing
# prerequisites without prompting) and, on success, hands off to
# 'Run PIM.command'. Exists because the remote session can click this file but
# cannot type answers to the setup script's y/N prompts.
#
# Everything is logged to setup-log.txt by the script it calls.
# =============================================================================
set -uo pipefail
cd "$(dirname "$0")" || { echo "FATAL: cannot cd to script directory"; exit 1; }

if [ ! -x "./Setup Mac.command" ]; then
  echo "FATAL: 'Setup Mac.command' is missing or not executable in $PWD"
  exit 1
fi

exec "./Setup Mac.command" --yes
