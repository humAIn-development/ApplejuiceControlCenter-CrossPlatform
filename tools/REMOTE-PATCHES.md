# AJCC-X Remote Patch Bridge

This directory contains the local-only commit bridge used for AI-assisted AJCC-X development.

## Why

Source commits must be created from Martin's local clone so Git author and committer identity remain controlled by the local Git configuration. The GitHub connector is used only to publish a machine-readable patch payload in issue #3; it never commits source changes.

## Transport

Issue #3 (`AJCC-X Remote Patch Channel`) contains JSON using schema `AJCC_REMOTE_PATCH_V2`.

An active payload contains:

- exact target branch and base SHA
- patch id and description
- expected changed file list
- commit message
- build/test policy
- Base64-encoded Git unified diff
- SHA-256 of the decoded diff

No remote PowerShell code is executed.

## Local command

From the repository root:

    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\Apply-RemotePatch.ps1"

The bridge verifies the repository, channel owner, branch, clean worktree, remote head, local Git email, patch hash and file list. It then applies the diff, runs validation, creates a local commit, verifies author/committer email and pushes.

If validation fails before commit, the bridge restores the exact base state automatically. If push fails after a verified local commit was created, it keeps that commit for manual recovery.
