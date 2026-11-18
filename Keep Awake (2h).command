#!/bin/bash
# Keeps the display awake for 2 hours so long verification runs are not
# interrupted by the lock screen.
#
# This changes NO system settings. It runs Apple's built-in `caffeinate`
# utility, which holds a power-management assertion only while this window is
# open. Close the window (or Ctrl+C) and normal sleep/lock behaviour resumes
# immediately.
echo "Display will stay awake for 2 hours."
echo "Close this window at any time to restore normal lock behaviour."
echo
caffeinate -d -t 7200
echo "Keep-awake finished; normal sleep and lock behaviour restored."
