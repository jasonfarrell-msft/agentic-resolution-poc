# Session Log: Documentation Update—Entra-Only SQL Auth

**Date:** 2026-05-11  
**Time:** 12:54:55Z  
**Topic:** docs-entra-setup  
**Scribe:** Scribe (Session Logger)  
**Requestor:** Jason Farrell  

## Summary

Bob completed documentation alignment to remove stale SQL password setup instructions and emphasize Entra-only authentication with managed identities across SETUP.md, DEPLOY.md, and scripts\README.md.

## Changes Processed

1. **SETUP.md**
   - Removed SQL Password Requirements section
   - Updated troubleshooting to remove password complexity references
   - Retained Entra-only auth explanation

2. **DEPLOY.md**
   - Removed entire SQL Server Password section
   - Updated Key Vault documentation to specify Entra authentication
   - Fixed sample data count (5 → 15 demo tickets)

3. **scripts\README.md**
   - Removed CI/CD password examples and `-SqlAdminPassword` parameter docs
   - Updated to emphasize non-interactive Azure CLI authentication
   - Removed password requirements troubleshooting
   - Fixed sample data count (5 → 15 demo tickets + 8 KB articles)

## Validation Performed

✅ No `SQL_ADMIN_PASSWORD` or `SqlAdminPassword` references remain  
✅ All password requirements sections removed  
✅ Sample data references aligned to "15 demo tickets" and "8 KB articles"  
✅ Setup path unified to `..\scripts\Setup-Solution.ps1` (no `-SeedSampleTickets`)  
✅ `Reset-Data.ps1 -SeedSampleTickets` documented only for manual operations  

## Decision Record

Merged Bob's decision entry from .squad/decisions/inbox/bob-docs-entra-setup.md into .squad/decisions.md as entry 15.

## Status

✅ **Complete** — Documentation aligned to current Entra-only architecture.
