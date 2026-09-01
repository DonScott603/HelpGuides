# PSR Clone — Problem Steps Recorder replacement

**Version 1.3.0**

A self-contained Windows application that replicates **Problem Steps Recorder / Steps
Recorder (`psr.exe`)**, which Microsoft has deprecated. Use it to record the exact
steps you take to reproduce a problem — each user action is captured with an
annotated screenshot and a plain-language description, then exported either as a
single self-contained MHTML (`.mht`) report or as a folder of loose files,
just like `psr.exe`.

It targets the **.NET Framework 4.x runtime that is built into every Windows 10/11
install**, so there is nothing extra to install and nothing to ship alongside the
`.exe`. It compiles with the `csc.exe` that already lives in `C:\Windows\Microsoft.NET`.

## Requirements & dependencies

PSR Clone has **no third-party dependencies**. Everything it uses ships in-box with
Windows and is present on every standard Windows 10/11 desktop install:

| Dependency | Provided by | Notes |
|------------|-------------|-------|
| .NET Framework 4.6.1+ runtime | Windows 10 (1607+) / Windows 11 | Runs the app. |
| GDI+ (`System.Drawing`) | Windows | Screen capture + image encoding. |
| WinForms (`System.Windows.Forms`) | Windows | Toolbar / dialogs. |
| UI Automation (`UIAutomationClient/Types`, `WindowsBase`) | Windows (GAC) | Resolves element/window names. |

Nothing needs to be "installed" on a target machine. If a machine ever lacks the
in-box .NET Framework (e.g. Windows **Server Core**), install the Microsoft
".NET Framework 4.8" runtime once — there is nothing else to add.

**Verify a target machine in one command:** run `PsrClone.exe --check`. It confirms
every dependency above is present and working, reports the effective DPI awareness,
and performs a real capture test on each monitor (see *Troubleshooting*).

## Features (matching psr.exe)

- **Start / Pause / Resume / Stop** recording from a compact always-on-top toolbar.
- **Automatic step capture** on every user action via global low-level mouse and
  keyboard hooks:
  - Left / right / middle **click**, **double-click**, **drag**, and **mouse wheel**.
  - **Keyboard input**, aggregated per target window into a single step
    (e.g. `hello{Enter}`).
- **Screenshot per step** of the monitor under the cursor, with the clicked UI
  element outlined and the cursor location marked.
- **High-DPI & multi-monitor correct** — the process forces Per-Monitor-V2 DPI
  awareness (via manifest *and* a programmatic fallback) so mouse coordinates,
  monitor bounds and captures all agree on physical pixels, regardless of each
  screen's scaling. This fixes wrong/blank captures on secondary or scaled displays.
- **UI Automation** resolves each target's element name, control type, and the
  containing window title, producing descriptions like:
  `User left click on "Save" (button) in "Untitled - Notepad" (window)`.
- **Add Comment** — dim the screen, drag to highlight a region, and type a note,
  captured as its own step.
- **Settings** — enable/disable screen capture, toggle keyboard recording, cap
  the number of most-recent screenshots kept (default **500**, adjustable **1–1500**),
  and choose what the report contains: per-step **date/time stamps**, the
  **Additional Details** recap, and the **Recording Environment** table. All three
  report toggles are **off by default**, so reports stay lean unless you opt in.
  Two things to know before switching them on or off: Additional Details is the only
  place the per-step **program name** is shown, and Recording Environment is the only
  place the **total step count** is shown.
- **Output** — on Stop you choose either a single self-contained
  **`.mht`** (MHTML) report, **or** a **folder dump** of loose files
  (a browsable `.htm` plus one JPEG per step). Both contain a "Recorded Problem
  Steps" section with per-step screenshots and the recording session's start/end
  time, plus whichever optional sections you enabled in Settings, and open in any
  browser.
- **Help** — opens the project page at
  [github.com/SomeGuru/PSR-Clone](https://github.com/SomeGuru/PSR-Clone).

## Build

No SDK required — uses the in-box .NET Framework compiler.

```powershell
cd PSRClone
.\build.ps1                 # produces bin\PsrClone.exe (stamped from the VERSION file)
.\build.ps1 -Run            # build and launch
.\build.ps1 -SelfTest       # build and run the headless report self-test
.\build.ps1 -Bump           # increment the patch version, then build
.\build.ps1 -Bump -Part minor   # bump minor (or -Part major)
```

The script auto-locates `csc.exe` and the UI Automation / WindowsBase assemblies
in the GAC, and stamps the version from the **`VERSION`** file into the assembly
(`FileVersion`/`ProductVersion`) and the in-app `BuildInfo.Version` constant shown
in the window title and Help dialog. **Bump the version on every change.**

## Use

1. Run `bin\PsrClone.exe`.
2. Click **Start Record** and reproduce your problem.
3. Optionally click **Add Comment** to annotate a specific spot.
4. Click **Stop**, then choose the output format: a single **`.mht`**, or a
   **folder** of loose files (HTML + images).
5. Share the result; recipients open the `.mht`, or the `.htm` (in the folder),
   in any browser. The **Help** button opens the project's GitHub page.

## Verification & diagnostics

- `--check` — verifies every dependency is present, reports the effective DPI
  awareness, and runs a real capture test on each monitor. Shows a dialog and writes
  a log; add `--quiet` for headless (exit `0` = healthy). **Run this first on any
  machine where capture or layout looks wrong.**
- `--selftest <out.mht>` — builds a synthetic recording and validates the produced
  MHTML *and* folder dump (embedded JPEGs, loose images, all expected markers).
  The extension is normalized to `.mht`. Exit code `0` = pass.
- `--recordtest <out.mht> <log.txt>` — installs the real hooks, synthesizes a click
  and keystrokes via `SendInput`, and confirms live capture (steps + screenshots +
  UI Automation). Exit code `0` = pass.

## Troubleshooting

**"Screenshots aren't captured / look wrong on another machine."** This is almost
always a DPI-awareness problem, not a missing dependency (a missing runtime would
stop the app from launching at all). Run:

```powershell
PsrClone.exe --check
```

- If **DPI awareness** shows anything other than *Per-Monitor-aware*, a group policy
  or launcher is stripping the manifest. PSR Clone now also sets awareness
  programmatically at startup, which resolves this in nearly all cases.
- If a monitor shows **CAPTURE BLANK/FAILED**, that display is protected (DRM/secure
  content) or the app lacks rights for it — run **elevated** and retry.
- If a dependency shows **[FAIL]**, install the in-box **.NET Framework 4.8** runtime
  on that machine (only needed on stripped-down SKUs such as Server Core).

## Notes and limitations vs. psr.exe

- **Elevation:** like any input-hook tool, when run un-elevated (`asInvoker`) it
  cannot capture actions directed at windows running at a *higher* integrity level
  (e.g. an elevated/admin app, or the UAC secure desktop). Run it elevated to record
  interactions with elevated apps. `psr.exe` has the same constraint.
- **Privacy:** keyboard input is recorded and appears in the report (this can
  include text typed into password-like fields). Pause before typing secrets, or
  disable "Record keyboard input" in Settings. `psr.exe` carried the same warning.
- Screenshots capture the monitor containing the cursor at the moment of the action.

## Versioning

The version lives in the top-level **`VERSION`** file (`MAJOR.MINOR.PATCH`) and is
the single source of truth. `build.ps1` stamps it into the compiled `.exe` and the
in-app `BuildInfo.Version`. Use `-Bump` (optionally `-Part minor|major`) to advance
it, and record the change under *Changelog*.

## Changelog

- **1.3.0** — three new Settings toggles control what the report contains:
  per-step **date/time stamps**, the **Additional Details** recap, and the
  **Recording Environment** table (machine name, username, OS, screen layout).
  **All three default to off**, so reports no longer carry those sections unless you
  enable them; the "Recording session" start/end header is always kept. `--selftest`
  now exercises both the on and off paths, and asserts report *content* against the
  raw folder-dump HTML rather than the quoted-printable-encoded `.mht` — in the
  `.mht`, soft line breaks can split a marker, which made presence checks flaky and
  would have made the new absence checks pass spuriously.
- **1.2.1** — output format is now a single **`.mht`** file rather than a `.zip`,
  finishing the packaging change started in 1.2.0: the save dialog, status line and
  "open containing folder" prompt all use the real written path (previously they
  named a `.zip` that was never created), `--selftest` validates the `.mht` on disk
  (it had been failing outright), and the unused `System.IO.Compression` references
  and `--check` probes were dropped.
- **1.2.0** — Per-Monitor-V2 DPI awareness (manifest + programmatic) fixing
  wrong/blank captures on scaled/secondary monitors; robust capture with fallback
  so a failed grab never drops a step; `Add Comment` now targets the monitor under
  the cursor; `--check` environment/dependency diagnostic; automatic version
  stamping from the `VERSION` file (`-Bump`); single-row auto-sized toolbar; version
  shown in title/Help; expanded documentation.
- **1.1.0** — Single-row toolbar with a **Help** button (opens the GitHub repo);
  Settings default of **500** screenshots (range **1–1500**); choose **`.zip`** or a
  **folder dump** on save.
- **1.0.0** — Initial release: global mouse/keyboard capture, per-step screenshots
  with element highlighting, UI Automation descriptions, MHTML `.zip` output.

## Project layout

```
PSRClone/
  VERSION                   # single source of truth for the version
  build.ps1                 # compiles via in-box csc.exe; stamps version
  src/
    Program.cs              # entry point + DPI awareness + test/diagnostic dispatch
    MainForm.cs             # recorder toolbar UI
    SettingsForm.cs         # settings dialog
    CommentOverlayForm.cs   # "Add Comment" full-screen overlay
    Recorder.cs             # hooks, screenshot capture, UIA, gesture detection
    NativeMethods.cs        # Win32 interop (hooks, key translation, SendInput, DPI)
    Step.cs                 # recorded-step model + description formatting
    RecorderSettings.cs     # options
    ReportWriter.cs         # MHTML generation + folder packaging
    DependencyCheck.cs      # --check environment & dependency diagnostic
    SelfTest.cs             # headless report validation
    RecordTest.cs           # live hook/capture validation
    Version.g.cs            # auto-generated from VERSION by build.ps1
    app.manifest            # DPI awareness + asInvoker
  bin/                      # build output (PsrClone.exe)
```
