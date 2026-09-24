# 🔐 Security

**English** · [简体中文](docs/SECURITY.zh-CN.md) · [🏠 Home](README.md)

## 📨 Reporting a vulnerability

Do not put credentials, personal data, private package sources, or exploit details in a public issue. If GitHub shows **Security → Report a vulnerability** for this repository, use that private reporting channel. If it is unavailable, open an issue requesting a private contact method without disclosing the vulnerability itself.

Include the affected version, impact, and minimal reproduction steps in the private report. No security support period or response-time guarantee is currently offered. Fixes target the current development branch.

## 🧱 Trust boundaries

- 🐍 **Python Install Manager is trusted executable code** — discovery and capability checks establish compatibility, not authenticity
- 📦 **Offline packages must come from a trusted source** — SHA-256 proves file integrity against the chosen index, not the identity of its publisher
- 👤 **Operations run as the current user** — PyDeck does not request elevation for Python operations and cannot defend against an attacker who already controls that account
- 🔒 **Operation locking coordinates PyDeck instances** — independent PIM commands and unrelated configuration editors do not participate in that lock
- 🧰 **Dependency recovery provides download links** — users install the packages themselves; the native readiness check is not a signature or publisher-authenticity check

## 🛡️ Implementation safeguards

| Boundary | Measures |
| --- | --- |
| Process invocation | No shell for PIM commands; separate argument entries; bounded ID grammar; absolute local manager paths; closed stdin |
| Runtime changes | Recheck identity, ownership, and current state; serialize mutations and default edits; refresh and verify results |
| Offline input | Reject remote, absolute, traversal, alternate-stream, and reparse-point paths; require SHA-256; validate ZIP entries and size limits |
| Offline execution | Copy and hash into a private staging directory; install from the verified snapshot with local primary and fallback sources |
| Output | Drain stdout/stderr with bounded retention; reject truncated list responses; keep observer failures from blocking pipe draining |
| Configuration | Bounded reads, unique temporary files, flushed atomic replacement, backup before default changes, preservation of unrelated JSON fields |
| Proxy credentials | Windows Credential Manager, scoped to proxy address / username; no password in preferences; redacted captured output and errors; overlong live lines omitted |
| Measured downloads | Official HTTPS package origin, SHA-256, checked resume ranges, bounded archives; PIM remains responsible for installation |
| PATH diagnosis | Unknown commands are never executed; trusted probes disable implicit installation and use timeouts |
| Terminal | Constant PowerShell code; selected executable passed through an environment variable rather than inserted into shell source |
| Dependency entry point | Fixed official HTTPS download destinations; no installer execution or elevation; installed launcher uses an explicit sibling GUI path, while the standalone helper never launches it |

Cancellation targets only the current subprocess tree, waits for exit and stream draining, then releases the operation lock. If termination is refused, the lock stays held until exit. Cancellable install commands disable their own BITS backend so a download is not handed to a background service that outlives the process. Other BITS jobs and persistent environment settings are untouched.

Stopping a Python installation may leave partial files. PyDeck does not claim automatic rollback or delete broad Python directories. Python uninstallation inside the app has no mid-operation cancel action. These rules concern Python operations, not the Windows Installer UI for installing PyDeck itself.

Configuration checks reduce accidental overwrite and path redirection but are not a transaction shared with unrelated tools. Offline source selection does not override administrator policy imposed on PIM.

Atomic configuration replacement retries brief Windows sharing/delete-access failures for at most 375 ms. Each retry rechecks path redirection and the original file's fingerprint; a concurrent edit aborts the save. Persistent locks surface an error while retaining the original file. There is no delete-then-write fallback.

## 🗂️ Data handling

PyDeck adds no analytics. Preferences and bounded crash diagnostics are local. Session activity remains in memory, limited to 2,000 lines / 1 MiB. Individual process streams retain at most 8 Mi characters. Copying activity is an explicit user action.

Logs and screenshots may reveal installation paths and manager output. Review them before sharing. Never commit settings, logs, crash dumps, package caches, signing keys, or credentials; ignored files are not a substitute for reviewing a commit.

## 🧪 Validation limits

Regression checks exercise malformed identities, unmanaged and stale runtime rejection, concurrency, configuration preservation, path handling, excessive process output, observer failure, and offline archive checks. These checks are not an independent security audit. A disposable Sandbox harness covers the supported Python lifecycle. Arbitrary third-party PIM configuration, all account contexts, and future CLI versions remain outside that matrix. The [feature-status record](docs/FEATURES.md) distinguishes this work from completed PyDeck installer checks.
