#!/bin/bash
#
# Publishes this repository to Azure DevOps.
#
# THIS SCRIPT CONTAINS NO CREDENTIALS AND MUST NEVER CONTAIN ANY.
# Git will prompt you for a username and a Personal Access Token the first time
# (or open a browser, if Git Credential Manager is installed). That prompt is
# yours to answer — do not paste a token into this file.
#
# It is deliberately NON-DESTRUCTIVE: if the Azure DevOps repository already has
# commits in it, this stops and tells you, rather than force-pushing over them.
#
set -u
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

# ---------------------------------------------------------------------------
# The one line to change if you want a different repository.
# ---------------------------------------------------------------------------
REMOTE_URL="https://dev.azure.com/altzor/PIM/_git/pim-platform"
REMOTE_NAME="origin"

exec > >(tee "azure-push-log.txt") 2>&1

echo "=== Publishing to Azure DevOps: $(date) ==="
echo "    target: $REMOTE_URL"
echo

# The Linux-side bridge cannot delete files, so a git command run from there
# leaves its lock behind and every git write after it fails.
rm -f .git/index.lock
find .git/objects -name 'tmp_obj_*' -delete 2>/dev/null
rm -f sprint29-commit-log.txt sprint28-commit-log.txt 2>/dev/null

# --- 1. Pre-flight ----------------------------------------------------------
BRANCH=$(git branch --show-current)
if [ "$BRANCH" != "main" ]; then
  echo "STOPPED: on branch '$BRANCH', expected 'main'."
  exit 1
fi

DIRTY=$(git status --porcelain | grep -v '^??' | wc -l | tr -d ' ')
if [ "$DIRTY" != "0" ]; then
  echo "STOPPED: there are uncommitted changes. Commit them first:"
  git status --short
  exit 1
fi

echo "--- what will be published ---"
echo "    branch:  $BRANCH"
echo "    commits: $(git rev-list --count HEAD)"
echo "    files:   $(git ls-files | wc -l | tr -d ' ')"
echo "    size:    $(du -sh .git | cut -f1)"
echo "    head:    $(git log --oneline -1)"
echo

# --- 2. Remote --------------------------------------------------------------
if git remote get-url "$REMOTE_NAME" >/dev/null 2>&1; then
  CURRENT=$(git remote get-url "$REMOTE_NAME")
  if [ "$CURRENT" != "$REMOTE_URL" ]; then
    echo "--- repointing '$REMOTE_NAME' from $CURRENT ---"
    git remote set-url "$REMOTE_NAME" "$REMOTE_URL" || exit 1
  else
    echo "--- remote '$REMOTE_NAME' already points at the target ---"
  fi
else
  echo "--- adding remote '$REMOTE_NAME' ---"
  git remote add "$REMOTE_NAME" "$REMOTE_URL" || exit 1
fi

# --- 3. Refuse to overwrite an existing history -----------------------------
# Azure DevOps offers to add a README when a repo is created. If that was
# accepted the remote already has a commit, and pushing would either be
# rejected or -- with --force -- would destroy it. Neither is something to do
# silently.
echo
echo "--- checking whether the remote is empty (git will ask you to sign in now) ---"
EXISTING=$(git ls-remote --heads "$REMOTE_URL" 2>/dev/null | wc -l | tr -d ' ')
LSSTATUS=$?

if [ "$LSSTATUS" -ne 0 ]; then
  echo
  echo "STOPPED: could not reach the repository."
  echo "  * Check the URL is right and that the repo exists in the PIM project."
  echo "  * If you were not prompted to sign in, Git may have cached a bad"
  echo "    credential: run  git credential-osxkeychain erase  and retry."
  exit 1
fi

if [ "$EXISTING" != "0" ]; then
  echo
  echo "STOPPED: the Azure DevOps repository is NOT empty — it already has:"
  git ls-remote --heads "$REMOTE_URL" | sed 's/^/     /'
  echo
  echo "  That is almost certainly the README Azure DevOps offers to create."
  echo "  Nothing has been pushed. Two safe ways forward:"
  echo "    a) Delete that repo in Azure DevOps and recreate it WITHOUT a README,"
  echo "       then run this script again."
  echo "    b) Tell Claude, and the two histories can be reconciled deliberately."
  echo "  This script will not force-push over somebody else's commit."
  exit 1
fi

# --- 4. Push ----------------------------------------------------------------
echo
echo "--- pushing $BRANCH ---"
git push -u "$REMOTE_NAME" "$BRANCH" || { echo; echo "PUSH FAILED — see the message above."; exit 1; }

echo
echo "--- verifying what landed ---"
git ls-remote --heads "$REMOTE_NAME" | sed 's/^/     /'
echo
echo "PUBLISHED. $(git rev-list --count HEAD) commits are now on Azure DevOps."
echo "Browse: ${REMOTE_URL%/_git/*}/_git/${REMOTE_URL##*/_git/}"
echo
echo "You can close this window."
