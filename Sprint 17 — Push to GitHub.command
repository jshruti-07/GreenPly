#!/bin/bash
#
# Sprint 17 — publish to GitHub, privately.
#
# This is the one step Claude cannot do for you: it needs your GitHub account.
# The script asks for your account name, creates a PRIVATE repository, and
# pushes. It will not create a public one.
#
# Why private, deliberately:
#   - appsettings.Development.json carries the local dev database password
#   - 02_dev_seed.sql is the client's own product catalogue
#
cd "$(dirname "$0")" || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
die() { printf '\n\033[31m%s\033[0m\n\n' "$*" >&2; echo "Nothing was pushed. You can close this window."; exit 1; }

rm -f .git/index.lock

say "Checking the repository is ready"
# --untracked-files=no on purpose: this script itself is untracked, as are the
# one-shot launchers from this sprint, and none of them should block a push.
# A MODIFIED tracked file should, because it means committed work is missing.
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  git status --short --untracked-files=no
  die "Tracked files have uncommitted changes. Commit or discard the above first."
fi
echo "no uncommitted changes to tracked files"
git --no-pager log --oneline -4

if git remote get-url origin >/dev/null 2>&1; then
  say "A remote already exists"
  git remote -v
  read -r -p "Push to it? [y/N] " ANSWER
  [ "$ANSWER" = "y" ] || [ "$ANSWER" = "Y" ] || die "Stopped at your request."
  git push -u origin main || die "Push failed — see the message above."
  say "Pushed."
  echo "You can close this window."
  exit 0
fi

say "Checking for the GitHub command line tool"
if ! command -v gh >/dev/null; then
  cat <<'EOF'
'gh' is not installed. Two options:

  A) Install it, then run this script again:
       brew install gh
       gh auth login

  B) Create the repository yourself at https://github.com/new
     - name it        pim-platform
     - set it to      Private
     - do NOT tick "Add a README" or any other initial file
     Then run these two lines in this folder, replacing YOURNAME:

       git remote add origin https://github.com/YOURNAME/pim-platform.git
       git push -u origin main
EOF
  die "Stopped: no 'gh' available."
fi

if ! gh auth status >/dev/null 2>&1; then
  say "You are not signed in to GitHub"
  echo "Run this first, then re-run this script:"
  echo
  echo "    gh auth login"
  die "Stopped: not signed in."
fi

gh auth status 2>&1 | sed 's/^/  /'

say "Where should this go?"
echo "Enter your GitHub account name (or an organisation you belong to)."
read -r -p "GitHub account: " OWNER
[ -n "$OWNER" ] || die "No account name given."

read -r -p "Repository name [pim-platform]: " NAME
NAME="${NAME:-pim-platform}"

say "About to create a PRIVATE repository"
echo "  $OWNER/$NAME"
echo "  and push $(git rev-list --count HEAD) commits from branch main"
echo
read -r -p "Go ahead? [y/N] " ANSWER
[ "$ANSWER" = "y" ] || [ "$ANSWER" = "Y" ] || die "Stopped at your request."

say "Compacting the object store first"
git gc --prune=now --quiet

say "Creating and pushing"
gh repo create "$OWNER/$NAME" --private --source=. --remote=origin --push \
  || die "Failed. If the name is taken, run this again with a different one."

say "Done"
git remote -v
echo
echo "Your code is now at: https://github.com/$OWNER/$NAME"
echo
echo "Next thing worth doing, whenever you like: turn on GitHub Actions so the"
echo "test suites run automatically on every change instead of only when you"
echo "run them here. Ask Claude to set it up — the pipeline already exists as"
echo "pipelines/azure-pipelines.yml and needs porting."
echo
echo "You can close this window."
