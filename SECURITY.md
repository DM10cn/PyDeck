# 🔐 Security

**English** · [简体中文](docs/SECURITY.zh-CN.md) · [🏠 Home](README.md)

## 📨 Reporting a vulnerability

Do not put credentials, personal data, private package sources, or exploit details in a public issue. If GitHub shows **Security → Report a vulnerability** for this repository, use that private reporting channel. If it is unavailable, open an issue requesting a private contact method without disclosing the vulnerability itself.

Include the affected version, impact, and minimal reproduction steps in the private report. PyDeck is a development preview; no security support period or response-time guarantee is currently offered. Fixes target the current development branch.

## 🧱 Trust boundaries

- 🐍 **Python Install Manager is trusted executable code** — discovery and capability checks establish compatibility, not authenticity
- 📦 **Offline packages must come from a trusted source** — SHA-256 proves file integrity against the chosen index, not the identity of its publisher
- 👤 **Operations run as the current user** — PyDeck does not request elevation for Python operations and cannot defend against an attacker who already controls that account
- 🔒 **Operation locking coordinates PyDeck instances** — independent PIM commands and unrelated configuration editors do not participate in that lock

## 🛡️ Implementation safeguards

| Boundary | Measures |
| --- | --- |
| Process invocation | No shell for PIM commands; separate argument entries; bounded ID grammar; absolute local manager paths; closed stdin |
| Runtime changes | Recheck identity, ownership, and current state; serialize mutations and default edits; refresh and verify results |
| Offline input | Reject remote, absolute, traversal, alternate-stream, and reparse-point paths; require SHA-256; validate ZIP entries and size limits |
| Offline execution | Copy and hash into a private staging directory; install from the verified snapshot with local primary and fallback sources |
| Output | Drain stdout/stderr with bounded retention; reject truncated list responses; keep observer failures from blocking pipe draining |
| Configuration | Bounded reads, unique temporary files, flushed atomic replacement, backup before default changes, preservation of unrelated JSON fields |
| Terminal | Constant PowerShell code; selected executable passed through an environment variable rather than inserted into shell source |

Cancellation targets only the current subprocess tree, waits for exit and stream draining, then releases the operation lock. If termination is refused, the lock stays held until exit. Cancellable install commands disable their own BITS backend so a download is not handed to a background service that outlives the process. Other BITS jobs and persistent environment settings are untouched.

Stopping installation may leave partial files. PyDeck does not claim automatic rollback or delete broad Python directories. Uninstallation has no mid-operation cancel action.

Configuration checks reduce accidental overwrite and path redirection but are not a transaction shared with unrelated tools. Offline source selection does not override administrator policy imposed on PIM.

## 🗂️ Data handling

PyDeck adds no analytics. Preferences and bounded crash diagnostics are local. Session activity remains in memory, limited to 2,000 lines / 1 MiB. Individual process streams retain at most 8 Mi characters. Copying activity is an explicit user action.

Logs and screenshots may reveal installation paths and manager output. Review them before sharing. Never commit settings, logs, crash dumps, package caches, signing keys, or credentials; ignored files are not a substitute for reviewing a commit.

## 🧪 Validation limits

Regression checks exercise malformed identities, unmanaged and stale runtime rejection, concurrency, configuration preservation, path handling, excessive process output, observer failure, and offline archive checks. These checks are not an independent security audit. Full real-world lifecycle tests and interactions with arbitrary third-party PIM configuration remain acceptance work.
