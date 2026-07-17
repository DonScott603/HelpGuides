# PSR Clone — Problem Steps Recorder replacement

A self-contained Windows application that replicates **Problem Steps Recorder / Steps
Recorder (`psr.exe`)**, which Microsoft has deprecated. Use it to record the exact
steps you take to reproduce a problem — each user action is captured with an
annotated screenshot and a plain-language description, then exported to a single
`.zip` containing an MHTML (`.mht`) report, exactly like `psr.exe`.

It targets the **.NET Framework 4.x runtime that is built into every Windows 10/11
install**, so there is nothing extra to install and nothing to ship alongside the
`.exe`. It compiles with the `csc.exe` that already lives in `C:\Windows\Microsoft.NET`.

## Features (matching psr.exe)

- **Start / Pause / Resume / Stop** recording from a compact always-on-top toolbar.
- **Automatic step capture** on every user action via global low-level mouse and
  keyboard hooks:
  - Left / right / middle **click**, **double-click**, **drag**, and **mouse wheel**.
  - **Keyboard input**, aggregated per target window into a single step
    (e.g. `hello{Enter}`).
- **Screenshot per step** of the monitor under the cursor, with the clicked UI
  element outlined and the cursor location marked.
- **UI Automation** resolves each target's element name, control type, and the
  containing window title, producing descriptions like:
  `User left click on "Save" (button) in "Untitled - Notepad" (window)`.
- **Add Comment** — dim the screen, drag to highlight a region, and type a note,
  captured as its own step.
- **Settings** — enable/disable screen capture, toggle keyboard recording, and cap
  the number of most-recent screenshots kept (default **500**, adjustable **1–1500**).
- **Output** — on Stop you choose either a single **`.zip`** containing a
  self-contained `.mht` (MHTML) report, **or** a **folder dump** of loose files
  (a browsable `.htm` plus one JPEG per step). Both include a "Recorded Problem
  Steps" section, per-step screenshots, an "Additional Details" text recap, and a
  recording-environment summary, and open in any browser.
- **Help** — opens the project page at
  [github.com/SomeGuru/PSR-Clone](https://github.com/SomeGuru/PSR-Clone).

## Build

No SDK required — uses the in-box .NET Framework compiler.

```powershell
cd PSRClone
.\build.ps1            # produces bin\PsrClone.exe
.\build.ps1 -Run       # build and launch
.\build.ps1 -SelfTest  # build and run the headless report self-test
```

The script auto-locates `csc.exe` and the UI Automation / WindowsBase assemblies
in the GAC.

## Use

1. Run `bin\PsrClone.exe`.
2. Click **Start Record** and reproduce your problem.
3. Optionally click **Add Comment** to annotate a specific spot.
4. Click **Stop**, then choose the output format: a single **`.zip`**, or a
   **folder** of loose files (HTML + images).
5. Share the result; recipients open the `.mht` (in the zip) or the `.htm` (in the
   folder) in any browser. The **Help** button opens the project's GitHub page.

## Verification

- `--selftest <out.zip>` — builds a synthetic recording and validates the produced
  zip/MHTML (embedded JPEGs, all expected markers). Exit code `0` = pass.
- `--recordtest <out.zip> <log.txt>` — installs the real hooks, synthesizes a click
  and keystrokes via `SendInput`, and confirms live capture (steps + screenshots +
  UI Automation). Exit code `0` = pass.

## Notes and limitations vs. psr.exe

- **Elevation:** like any input-hook tool, when run un-elevated (`asInvoker`) it
  cannot capture actions directed at windows running at a *higher* integrity level
  (e.g. an elevated/admin app, or the UAC secure desktop). Run it elevated to record
  interactions with elevated apps. `psr.exe` has the same constraint.
- **Privacy:** keyboard input is recorded and appears in the report (this can
  include text typed into password-like fields). Pause before typing secrets, or
  disable "Record keyboard input" in Settings. `psr.exe` carried the same warning.
- Screenshots capture the monitor containing the cursor at the moment of the action.

## Project layout

```
PSRClone/
  build.ps1                 # compiles via in-box csc.exe
  src/
    Program.cs              # entry point + test-mode dispatch
    MainForm.cs             # recorder toolbar UI
    SettingsForm.cs         # settings dialog
    CommentOverlayForm.cs   # "Add Comment" full-screen overlay
    Recorder.cs             # hooks, screenshot capture, UIA, gesture detection
    NativeMethods.cs        # Win32 interop (hooks, key translation, SendInput)
    Step.cs                 # recorded-step model + description formatting
    RecorderSettings.cs     # options
    ReportWriter.cs         # MHTML generation + zip packaging
    SelfTest.cs             # headless report validation
    RecordTest.cs           # live hook/capture validation
    app.manifest            # DPI awareness + asInvoker
  bin/                      # build output (PsrClone.exe)
```
