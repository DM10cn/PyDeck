# 🛠️ Python management

**English** · [简体中文](MANAGEMENT.zh-CN.md) · [🏠 Home](../README.md)

These features are available in **0.6.0**. The published 0.5.1 installers do not contain them.

## ✅ Operations and verification

Install, update, repair, uninstall, and cancellation refresh the actual PIM inventory. Successful installation is checked against the requested version and by starting the interpreter in isolated mode, without site initialization, with a timeout. Checks import core modules including SSL and SQLite; they do not certify every third-party package.

An exit code of zero is not sufficient for a green success message. Missing files, an unexpected version, or an unavailable refreshed list leave the result unconfirmed. Updating an already current version does not reinstall it or downgrade it.

## ⚙️ PIM configuration

Open **Settings → Python management → PIM configuration** to change the default interpreter, default platform, automatic installation, or inclusion of unmanaged Python. **PIM default** removes the corresponding user override.

The editor targets the standard user configuration, `%AppData%\Python\pymanager.json`. It preserves other fields, shows a change review, creates a backup, rejects concurrent edits, and replaces the file atomically. Backups created here can be restored from the same dialog. Environment overrides or administrator policies block editing until reviewed; custom configuration locations are not silently rewritten.

These preferences also affect PIM outside PyDeck. PyDeck continues to disable implicit automatic installation in its own child processes. Installation-directory migration, custom sources, and shebang settings are outside this editor's scope.

## 🔎 PATH and aliases

**Settings → Python management → PATH and aliases** compares the PIM default with `py`, `python`, `python3`, and `pymanager`. It shows candidate paths, shadowed commands, and differences between the running process's PATH and saved environment variables.

Known registered interpreters and verified application execution aliases belonging to the connected PIM can be probed for their actual interpreter path. Unknown executables are not run and remain **Not verified**. Shell functions, virtual environments, and shell-specific resolution can differ from this executable-path check.

The diagnostic opens Windows app execution alias settings without changing PATH. **Refresh aliases** separately asks PIM to refresh registrations and aliases for **all** its managed runtimes; it does not toggle Windows-controlled application execution aliases.

## 🌐 Proxy and download details

**Settings → Python management → Network** offers system settings, direct connection, and a custom HTTP proxy. HTTP proxies can tunnel HTTPS traffic. System mode retains inherited proxy environment variables and the Windows proxy configuration. Changes only affect operations started by PyDeck.

Proxy passwords are stored as a current-user generic credential in **Windows Credential Manager**, tied to the proxy address and username. Ordinary preferences contain only the mode, address, and username. Passwords and credential-bearing URLs are redacted from captured PIM output and error messages. Leaving the password blank retains a matching saved credential; **Clear saved password** removes it.

For official Python packages, PyDeck measures bytes while downloading metadata-selected archives, validates SHA-256 and archive paths, and passes a verified temporary bundle cache to PIM. **PIM performs the installation and repair**, and online installs retain their original online source. Offline installation continues to use only the verified local source.

Transfers show KB below 1 MB and MB from 1 MB upward, using 1 KB = 1,024 bytes. Speed is smoothed; remaining time appears only with a known total and enough observations. Broken transfers may resume with checked HTTP ranges and are always validated against the full checksum. Missing data stays unknown. Extraction remains a separate, approximate PIM stage.

## 🔧 Check and repair

Use a managed version's **⋯ → Check installation** for a basic interpreter check. **Reinstall to repair** invokes PIM's forced reinstall after confirmation. It requires the same version to remain available; otherwise, use Update. Reinstallation may remove packages or changes inside the interpreter directory and is not a rollback operation.

## 🧪 Compatibility and acceptance

Writing Python installations requires **PIM 26.3 or later**. Older or unrecognized versions remain available for compatible read-only queries; reconnect after upgrading to refresh capabilities. Testing found registration failures with `install --by-id` in both PIM 25.2 and 26.3. PyDeck uses selectors instead, verifies that a selector resolves to exactly the expected ID before mutation, and validates the installed result. The tested mutation baseline is 26.3; this is not a claim that every later PIM release has been tested.

Core checks cover config conflicts, secret handling, command resolution, transfer validation and resumption, proxy failure, and capability changes. GUI smoke checks exercise the four languages and settings dialogs. The destructive lifecycle harness is **only for a disposable Windows Sandbox**:

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Smoke-Test.ps1
.\scripts\Test-PimLifecycle.ps1
```

The lifecycle script verifies official PIM installers, creates its own headless sandbox, tests PIM 25.2 → 26.3 with an older official Python fixture, and writes per-case results under `artifacts/`. It stops only the sandbox it created. A fixture must be older than the online candidate for the actual update case to pass; `-SeedBundleDirectory` accepts a prepared flat offline bundle. Failures and unexecuted cases must not be counted as passes.

📋 Current results and remaining validation boundaries are recorded in [feature status](FEATURES.md). venv, custom-source editing, and shebang management remain deferred.
