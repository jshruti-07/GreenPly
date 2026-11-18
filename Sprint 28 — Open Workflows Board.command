#!/bin/bash
# Opens the new Workflows & Tasks screen for the sprint-close visual check —
# the list view filtered to one step, which is the same data behind a filter.
# Assumes 'Run PIM.command' is already running (API :5100, frontend :5174).
open "http://localhost:5174/workflows?view=list&step=approval"
