# 🧪 Guest validation, one step at a time

**English** · [简体中文](README.zh-CN.md)

Run each supplied script **inside the test VM**, then return its report before moving to the next step. Build PyDeck on the host. The guest only needs the runtime dependencies for later application checks.

## 1️⃣ Inspect the environment

Copy `01-InspectEnvironment.ps1` to a writable folder in the VM. Open **64-bit Windows PowerShell** in that folder as the same user who will run PyDeck; administrator rights are not required.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\01-InspectEnvironment.ps1
```

The execution-policy option applies only to this process. It does not change the machine or user policy. Managed policy can still block execution; report that error instead of changing security settings. Optionally pass `-ReferenceRelease <version>` to label the report with the package being inspected; omitting it leaves the label empty.

If an older copy fails at line 8 with `Join-Path ... Path ... empty string`, replace the script with the corrected copy or explicitly supply the output directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\01-InspectEnvironment.ps1 -OutputDirectory .\PyDeck-Diagnostics
```

📦 The script prints a **SEND BACK** path under `PyDeck-Diagnostics` next to the script. Return that ZIP. It contains exactly:

- `summary.txt` — readable outcome and next step
- `checks.log` — individual PASS / WARN / FAIL / ERROR results
- `result.json` — structured versions and findings

If ZIP creation fails, return these three files from the printed folder. Each run uses a new directory. `FAIL` means a prerequisite is unmet; `ERROR` means a check could not complete. Other checks still run. Successful script completion is **not** a compatibility pass.

🔎 This step checks Windows, the effective x64 .NET runtime location, current-user Windows App Runtime registration, VC++ runtime files, PIM discovery, and existing PyDeck installer registrations. It does not launch Python or PyDeck, download or install anything, change VM settings, or read application preferences. User profile paths are masked. Installation paths and package versions remain diagnostic data; review the report before sharing it. Generated reports are excluded from source control.

⚠️ **PyDeck requires Windows 11 x64**. Windows 10 LTSC reporting an OS failure is expected. An otherwise complete environment does not bypass that requirement or establish Windows 10 support.

## 🔄 Following steps

Only step 01 is supplied now. After reviewing its guest log, prepare the applicable script and package for each next stage:

| Stage | Work and evidence |
| --- | --- |
| 02 · Dependencies | Verify the selected installers and required versions; user performs installations; recheck and report |
| 03 · Installation | Test the chosen MSI/MSIX; preserve installer output, exit codes and package identity |
| 04 · Startup | Use an isolated smoke-test profile; collect app results and errors |
| 05 · Upgrade and removal | Use explicit test installation ownership; verify files, shortcuts and retained user data |
| 06 · Interactive acceptance | Use Computer Use for layouts, controls, material fallback and real user flows |

MSI and MSIX are separate test tracks. Do not reuse the existing repository upgrade script blindly inside a guest: it is a developer harness and can uninstall its test installation. Later scripts must identify exactly what they will modify. Never reset a checkpoint, uninstall an unrelated installation or force a shutdown as implicit cleanup.
