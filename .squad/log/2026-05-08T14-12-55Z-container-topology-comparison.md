# Container Topology Investigation Summary

**Date:** 2026-05-08T14:12:55Z  
**Investigator:** Coordinator + Hicks + Apone  
**Type:** Deployment Architecture Analysis

---

## Question

Why does dev have a container app deployed for each agent, but test only has 2 container apps?

---

## Answer

**Setup-Solution.ps1 deploys exactly 2 Container Apps to all environments (dev, test):**

| Environment | Containers | Source |
|---|---|---|
| **Dev** | 2 scripted: ca-api, ca-res + 5 manual: ca-mcp, ca-incident, ca-classifier, ca-request, ca-escalation, ca-resolution | Script (2) + Manual migration experiment (5) |
| **Test** | 2 scripted: ca-api-agent-resolution-test, ca-res-agent-resolution-test | Script (2) |

---

## Root Cause

**Scripted Topology:** 2 containers (Tickets API + Python Resolution API) — intentional, Phase 1 complete.

**Dev Extras:** Manual deployments from hosted-agents migration experiment; not part of current scripted path.

**Why Test != Dev:** Test re-provisioned by setup script; dev still has manual experimental containers.

---

## Recommendation

**Cleanup:** Re-provision dev environment via setup script to restore parity (2 containers).

**Decision:** Team votes on whether to keep 2-container topology or deploy Phase 2 MCP Server (Option 1/2/3).

---

## Status

✅ **RESOLVED** — Discrepancy explained; decision documented in .squad/decisions.md.
