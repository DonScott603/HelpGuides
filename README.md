# PSR Clone — Problem Steps Recorder replacement

**Version 1.4.0**

A self-contained Windows application that replicates **Problem Steps Recorder / Steps
Recorder (`psr.exe`)**, which Microsoft has deprecated, and extends it into a
**help-guide authoring tool**. Record the exact steps you take — each user action
is captured with an annotated screenshot and a plain-language description — then
review the recording in the built-in editor (retitle, rewrite, insert, delete, crop,
redact) and export a print-ready guide as a single self-contained MHTML (`.mht`)
file or as a folder of loose files.

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

## Features

Recording works the way `psr.exe` did; the guide editor and print layout are
additions.

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
  place the **total step count** is shown. Settings are **not saved between
  launches**; every start uses the defaults above.
- **Guide editor** — pressing **Stop** opens a review window before anything is
  saved. There you can:
  - give the guide a **title** (rendered as `Guide: {your title}`) and replace the
    stock "This file contains all of the recorded problem steps…" **description**;
  - **rewrite any step's text** (multi-line), with a one-click reset to the
    recorded wording;
  - **insert text-only steps** between recorded steps for explanations;
  - **delete** steps that were not needed, and move steps **up/down**;
  - **crop** a screenshot to the relevant area;
  - hide sensitive content with a solid **Redact** block (irreversible) or a
    **Pixelate** mosaic (softer, not for passwords), with undo and **Reset image**.
  Every image edit is non-destructive until you save, so you can change your mind.
  Steps are renumbered 1..n on save. **Cancel** discards the recording.
- **Output** — from the editor you choose either a single self-contained
  **`.mht`** (MHTML) file, **or** a **folder dump** of loose files (a browsable
  `.htm` plus one JPEG per step). Both contain the steps with their screenshots and
  the recording session's start/end time, plus whichever optional sections you
  enabled in Settings. The `.htm` opens in any browser; the `.mht` opens in
  Edge, Chrome, Internet Explorer or Word (Firefox does not read MHTML natively).
  The file name is derived from the guide title (`Guide_Reset_a_password.mht`);
  with no title it falls back to `RecordedSteps_<timestamp>`.
- **Print-ready** — the report carries print CSS so that each step's text and its
  screenshot stay together on one page (`break-inside: avoid`, plus a maximum image
  height so a full-monitor capture can never be taller than the page). Both the
  modern and legacy page-break spellings are emitted, so Edge/Chrome and Word
  honour it. Verified in Edge; Word's rendering of `.mht` has not been checked.
- **Help** — opens the project page at
  [github.com/DonScott603/HelpGuides](https://github.com/DonScott603/HelpGuides).

## Build

No SDK required — uses the in-box .NET Framework compiler.

```powershell
cd PSR-Clone
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

Source is kept to **C# 5** syntax (the in-box compiler is the C# 5 compiler) and
to **ASCII**: `csc.exe` reads files without a BOM in the system ANSI code page, so
non-ASCII characters in string literals are written as `\uXXXX` escapes. The
built `bin\PsrClone.exe` is checked in so the app can be run without building.

## Use

1. Run `bin\PsrClone.exe`.
2. Click **Start Record** and perform the steps you want to document. **Pause**
   suspends capture (for example before typing a password); **Resume** continues.
3. Optionally click **Add Comment** to annotate a specific spot.
4. Click **Stop**. The guide editor opens: set a title and description, tidy up
   the step text, insert text steps, delete or reorder steps, and crop / redact /
   pixelate screenshots. Select a step, pick a tool, then drag on the image.
5. Click **Save as .mht…** or **Save to folder…** (or **Cancel** to discard the
   recording).
6. Share the result; recipients open the `.mht` (Edge, Chrome or Word) or the
   `.htm` in the folder (any browser), and can print it with each step kept on one
   page. After saving, the app offers to open the containing folder. The **Help**
   button opens the project's GitHub page.

Editing happens once, between Stop and Save. A saved guide cannot be reopened in
the editor; to change it, record again or edit the `.htm` by hand.

## Verification & diagnostics

`PsrClone.exe` is a windowed application, so these modes print nothing to the
console; read the **exit code** (and the log file where one is written).

- `--check [log.txt] [--quiet]` (alias `--version`) — verifies every dependency is
  present, reports the effective DPI awareness, and runs a real capture test on
  each monitor. Shows a dialog and writes a log; add `--quiet` for headless
  (exit `0` = healthy). **Run this first on any machine where capture or layout
  looks wrong.**
- `--selftest [out.mht]` — builds a synthetic recording and validates the produced
  MHTML *and* folder dump: embedded and loose JPEG counts, every report marker with
  the Settings toggles on and off, the guide-editor path (custom title and
  description, an inserted text step, a deleted step, rewritten step text, a crop
  with both redaction kinds checked at the pixel level, the print CSS, loose-file
  image links), and that an untouched guide renders byte-for-byte the same as the
  classic report. The extension is normalized to `.mht`; the default output is
  `%TEMP%\psrclone_selftest.mht`. Exit code `0` = pass.
- `--recordtest [out.mht] [log.txt]` — installs the real hooks, synthesizes a click
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
- **Redaction:** the solid block is applied to the image pixels before encoding
  and cannot be undone by the recipient. Pixelation obscures but does not destroy
  the underlying detail; do not rely on it for passwords or account numbers.
- **No re-editing:** the editor runs once, between Stop and Save. There is no
  project file to reopen later.
- **Settings are per-launch** and revert to defaults each time the app starts.

## Versioning

The version lives in the top-level **`VERSION`** file (`MAJOR.MINOR.PATCH`) and is
the single source of truth. `build.ps1` stamps it into the compiled `.exe` and the
in-app `BuildInfo.Version`. Use `-Bump` (optionally `-Part minor|major`) to advance
it, and record the change under *Changelog*.

## Changelog

- **1.4.0** — PSR Clone now authors **help guides**, not just problem reports.
  Stop opens a new **guide editor** in place of the old Yes/No/Cancel save prompt:
  set a **title** (`Guide: …`) and **description**, **rewrite** any step's text,
  **insert text-only steps**, **delete** and **reorder** steps, and **crop**,
  **redact** (solid) or **pixelate** screenshots — all non-destructive until save.
  The report gains **print CSS** so each step's text and screenshot stay on one
  page. Output file names derive from the title. Internally: `RecordedStep` gained
  `CustomDescription`, `Crop` and `Redactions`; `StepKind.Text` is new;
  `GuideDocument` carries title/intro/steps and `ReportWriter` has overloads for
  it (the old `IList<RecordedStep>` overloads still work and produce identical
  output); `ReportWriter.RenderPreview` is shared by the editor and the writer so
  the preview is exactly what gets saved. `--selftest` now also exercises the
  editor path (title, text step, delete, crop, both redaction kinds, print CSS) and
  asserts the default document is byte-identical to the legacy path. Also fixed:
  starting a second recording no longer leaks the previous run's screenshots, and
  the **folder dump's images now display** — its `.htm` referenced them with
  `cid:` URLs, which only resolve inside an `.mht`, so every screenshot showed as
  a broken image when the loose `.htm` was opened in a browser. The **Help**
  button now opens this repository (`DonScott603/HelpGuides`).
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
PSR-Clone/
  README.md
  VERSION                   # single source of truth for the version
  build.ps1                 # compiles via in-box csc.exe; stamps version
  src/
    Program.cs              # entry point + DPI awareness + test/diagnostic dispatch
    MainForm.cs             # recorder toolbar UI
    SettingsForm.cs         # settings dialog
    CommentOverlayForm.cs   # "Add Comment" full-screen overlay
    GuideEditorForm.cs      # post-recording editor (title, text, insert/delete, crop, redact)
    GuideDocument.cs        # title + intro + edited step list handed to ReportWriter
    Recorder.cs             # hooks, screenshot capture, UIA, gesture detection
    NativeMethods.cs        # Win32 interop (hooks, key translation, SendInput, DPI)
    Step.cs                 # recorded-step model, edit fields, description formatting
    RecorderSettings.cs     # options
    ReportWriter.cs         # HTML/MHTML generation, image rendering (crop/redact), print CSS
    DependencyCheck.cs      # --check / --version environment & dependency diagnostic
    SelfTest.cs             # --selftest headless report + editor-path validation
    RecordTest.cs           # --recordtest live hook/capture validation
    Version.g.cs            # auto-generated from VERSION by build.ps1 (checked in)
    app.manifest            # DPI awareness + asInvoker
  bin/
    PsrClone.exe            # build output (checked in); build.ps1 -SelfTest also drops selftest.mht here
```
