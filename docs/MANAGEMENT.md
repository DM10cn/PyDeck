# 🛠️ Python management

**English** · [简体中文](MANAGEMENT.zh-CN.md) · [🏠 Home](../README.md)

This guide covers **0.6.2**, including management features introduced in 0.6.0 and 0.6.1. Python management settings expand inline in Settings; catalog browsing preferences live on Install Python.

## 🆕 0.6.2: historical versions and interface

### 🕰️ Browse and install a historical micro

**Install Python → All versions** groups available packages by minor series, such as 3.14 and 3.13. Expand a group to select a specific micro. Search, architecture, preview and distribution filters still apply. The selected PIM source and its history pages define availability: the official source lists Windows installation packages, not all Python source releases. A micro or variant absent from that source cannot be installed from the catalog.

Architecture, package type and **Show preview releases** are controlled directly on **Install Python**. Changes filter the list immediately, and PyDeck remembers them between sessions, including **All architectures** and individual **Embeddable**, **Free-threaded** or **With tests** selections. Settings no longer duplicates these browsing preferences; **Confirm before uninstall** remains in Settings.

Catalog entries distinguish the runtime ID **and** full version. Before installing or downloading an offline bundle, PyDeck asks PIM to resolve the exact selected micro and rejects a different result. **Reinstall to repair** requests the installed micro; it does not silently turn into an update. The original installation source is retained for online repair, and missing versions fail explicitly.

PIM can reuse one runtime ID across several micros of the same series, architecture and distribution. Choosing another micro therefore **replaces the existing installation**, rather than creating a second entry with that ID. PyDeck shows the current and requested versions before replacement and rechecks the current version under its operation lock. If it changed after confirmation, refresh and review again. Packages inside the interpreter and virtual environments using it may need attention after replacement.

Historical **Download offline package** uses the selected micro. Installing from that bundle uses a verified local snapshot and applies the same replacement check, without requiring an online catalog lookup. Keep the complete generated bundle folder. The existing checksum, archive-path, source-trust and cancellation rules still apply.

### 🧭 Page layout and activity

- **My Python** shows the effective default on its runtime row; the duplicate hero is removed
- **Install Python** uses an installation-source selector, search / filters, a recommendation and minor-series groups
- **Virtual environments** puts Create / Import in the empty state; a populated list adds search and status filters, with Terminal on each row and other actions in its menu. Refresh asks before rechecking previously verified environments, retaining Ready only after a successful probe; cancelling leaves the list unchanged, and unverified imports are only inspected
- **Settings** uses section headings, responsive setting rows and compact style previews; PIM management remains inline, and proxy fields appear when a custom proxy is selected
- **Activity** shows time, severity and message separately. Filter Information / Warnings / Errors, clear the session, or copy only matching entries. Commands and raw output use monospace; multiline errors expand for details. Logs remain session-only

Shared control metrics define typography, icons, padding and minimum height. Text scaling can increase the height, and narrow layouts rearrange content instead of using fixed-size text boxes. These layout changes do not constitute all-DPI or external accessibility acceptance. Current acceptance status belongs in [feature status](FEATURES.md).

## ✅ Operations and verification

Install, update, repair, uninstall, and cancellation refresh the actual PIM inventory. Successful installation is checked against the requested version and by starting the interpreter in isolated mode, without site initialization, with a timeout. Checks import core modules including SSL and SQLite; they do not certify every third-party package.

An exit code of zero is not sufficient for a green success message. Missing files, an unexpected version, or an unavailable refreshed list leave the result unconfirmed. Updating an already current version does not reinstall it or downgrade it.

## ⚙️ PIM configuration

Open **Settings → Python management → PIM configuration** to change the default interpreter, default platform, automatic installation, or inclusion of unmanaged Python. **PIM default** removes the corresponding user override.

The editor targets the standard user configuration, `%AppData%\Python\pymanager.json`. It preserves other fields, shows a change review, creates a backup, rejects concurrent edits, and replaces the file atomically. Backups created here can be restored from the same section. Environment overrides or administrator policies block editing until reviewed; custom configuration locations are not silently rewritten.

These preferences also affect PIM outside PyDeck. PyDeck continues to disable implicit automatic installation in its own child processes. Installation-directory migration remains outside this editor. Custom sources and Shebang rules have separate expandable sections.

## 🔎 PATH and aliases

**Settings → Python management → PATH and aliases** compares the PIM default with `py`, `python`, `python3`, and `pymanager`. It shows candidate paths, shadowed commands, and differences between the running process's PATH and saved environment variables.

Known registered interpreters and verified application execution aliases belonging to the connected PIM can be probed for their actual interpreter path. Unknown executables are not run and remain **Not verified**. Shell functions, virtual environments, and shell-specific resolution can differ from this executable-path check.

The diagnostic opens Windows app execution alias settings without changing PATH. **Refresh aliases** separately asks PIM to refresh registrations and aliases for **all** its managed runtimes; it does not toggle Windows-controlled application execution aliases.

## 🌐 Proxy and download details

**Settings → Python management → Network** offers system settings, direct connection, and a custom HTTP proxy. HTTP proxies can tunnel HTTPS traffic. System mode retains inherited proxy environment variables and the Windows proxy configuration. Changes only affect operations started by PyDeck.

Proxy passwords are stored as a current-user generic credential in **Windows Credential Manager**, tied to the proxy address and username. Ordinary preferences contain only the mode, address, and username. Passwords and credential-bearing URLs are redacted from captured PIM output and error messages. Leaving the password blank retains a matching saved credential; **Clear saved password** removes it.

For official and explicitly trusted custom HTTPS packages, PyDeck measures bytes while downloading metadata-selected archives, validates SHA-256 and archive paths, and passes a verified temporary bundle cache to PIM. **PIM performs the installation and repair**, and online installs retain their original online source. Offline installation continues to use only the verified local source.

Transfers show KB below 1 MB and MB from 1 MB upward, using 1 KB = 1,024 bytes. Speed is smoothed; remaining time appears only with a known total and enough observations. Broken transfers may resume with checked HTTP ranges and are always validated against the full checksum. Missing data stays unknown. Extraction remains a separate, approximate PIM stage.

## 🔧 Check and repair

Use a managed version's **⋯ → Check installation** for a basic interpreter check. **Reinstall to repair** invokes PIM's forced reinstall after confirmation. It requires the same version to remain available; otherwise, use Update. Reinstallation may remove packages or changes inside the interpreter directory and is not a rollback operation.

## 🧪 Compatibility and acceptance

Writing Python installations requires **PIM 26.3 or later**. Older or unrecognized versions remain available for compatible read-only queries; reconnect after upgrading to refresh capabilities. Testing found registration failures with `install --by-id` in both PIM 25.2 and 26.3. PyDeck uses selectors instead, verifies that a selector resolves to exactly the expected ID before mutation, and validates the installed result. The tested mutation baseline is 26.3; this is not a claim that every later PIM release has been tested.

Core checks cover config conflicts, secret handling, command resolution, transfer validation and resumption, proxy failure, and capability changes. GUI smoke checks exercise the four languages and inline settings. The destructive lifecycle harness is **only for a disposable Windows Sandbox**:

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Smoke-Test.ps1
.\scripts\Test-PimLifecycle.ps1
```

The lifecycle script verifies official PIM installers, creates its own headless sandbox, tests PIM 25.2 → 26.3 with an older official Python fixture, and writes per-case results under `artifacts/`. It stops only the sandbox it created. A fixture must be older than the online candidate for the actual update case to pass; `-SeedBundleDirectory` accepts a prepared flat offline bundle. Failures and unexecuted cases must not be counted as passes.

📋 Current results and remaining validation boundaries are recorded in [feature status](FEATURES.md). venv, source selection, and Shebang rules are available in 0.6.1; pip, uv/conda, environment deletion, and script-file editing remain outside this scope.

## 🧰 Virtual environments

Use **Virtual environments** to create an environment with an installed interpreter or import a folder containing `pyvenv.cfg`. Creation uses `python -I -m venv` in a new, exclusively reserved folder. Existing folders are never overwritten. Cancellation and failure retain partial files and an incomplete record. **Remove from list** removes only PyDeck's record.

Import and Refresh inspect metadata without running Python. Check and Terminal first show the interpreter path and ask before executing a bounded probe. Terminal sets `VIRTUAL_ENV` and puts Scripts first in PATH without running the environment's activation script. Probing an imported environment executes its interpreter and normal site initialization, so import only environments you trust. A missing base interpreter is reported explicitly.

## 🌐 Installation sources

Expand **Settings → Python management → Installation source**. Leave the URL empty to use the official source, or explicitly trust a custom HTTPS PIM index. This preference applies only to PyDeck, not pip/PyPI or other terminals. Source changes invalidate the catalog; stale cards cannot start an installation.

Only HTTPS URLs without credentials, query strings or fragments are accepted in this first version. Packages require SHA-256 and retain size/path checks, proxy support, cancellation, speed and ETA. A different package origin or cross-origin package redirect requires confirmation before connecting. PIM handles catalog resolution and its existing signature rules remain in force. A checksum proves integrity, not publisher identity.

PIM re-reads the online source when it installs. Its temporary bundle cache does not freeze a custom server's metadata: if that server changes the package after validation, PIM may download it again. Trust the source operator; post-installation version checks detect a mismatch but are not a transactional guarantee against a changing source.

Update and Repair read the installation's original HTTPS index from PIM metadata, even if a different browsing source is selected. Missing or local-only original sources fail explicitly instead of silently choosing another publisher. Use the existing local offline-bundle flow for offline installs.

## 📜 Shebang rules

Expand **Shebang rules** with PIM 26.3 or later. Choose default/allow/block for launching non-Python programs; map templates to the default or an installed Python with `py` / `pyw`. Add or replace a rule by entering its template. Existing advanced entries remain untouched unless explicitly removed or replaced. Review differences before saving; backups, policy checks and conflict detection apply. PyDeck does not edit or execute project scripts to test rules.

## 🔔 PyDeck updates

**Settings → About → Check PyDeck updates** checks this repository's latest stable release through GitHub, using the app's network preferences. It excludes drafts, prereleases and beta-tagged history. When newer, **Open release page** launches the fixed PyDeck GitHub destination in your default browser. The app neither downloads installers silently nor replaces itself while running.
