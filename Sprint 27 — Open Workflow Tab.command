#!/bin/bash
# Opens the new Workflow tab for the sprint-close visual check, on a product
# standing at Approval — the step prototype v4.2 draws three buttons on.
# Assumes 'Run PIM.command' is already running (API :5100, frontend :5174).
open "http://localhost:5174/products/AB-CHROMA-FLOOR-WALL-T-12-5-X-12-5?tab=workflow"
