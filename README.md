# BO6 Personal Progress

[Русская инструкция](README.ru.md) · [Download portable ZIP](https://github.com/queerkorol-ux/bo6-personal-progress/releases)

The prebuilt release ZIP includes the EXE and full runtime folder. Build steps
below are for source checkouts only; release users can start at installation step 1.

Build-specific offline campaign progression/checkpoint bridge for an existing
compatible BO6 installation. Runtime **0.2.4**, persistent EXE launcher **0.3.5**.
The user reported this exact configuration working; a complete campaign
playthrough and compatibility with other builds have not been established.

## What it does

- Keeps campaign progress, money and local checkpoints in a separate data folder.
- Restores ordinary progression conditions instead of the compatible installation's
  forced unlock/cheat conditions; existing personal progress is retained.
- Backs up existing fixed profile/checkpoint files before launching the game.
- Keeps the checkpoint notification animation, then hides that one notification
  after approximately two seconds of UI timer time. **This is visibility only,
  not proof that a save succeeded.** Pause/frame timing can affect the interval.
- Provides an optional real Windows x64 EXE that remains alive through elevation,
  PowerShell and the game session, then exits without the final ENTER prompt.
  Time tracking still depends on the third-party application.

Game EXE/DLL files are not rewritten. The bridge does modify the **running
process**, including guarded save/progression code and a guarded Lua-load handler.
Keep the bridge open while playing and close BO6 normally before stopping it.

## Compatibility and requirements

Windows x64, Windows PowerShell 5.1, .NET Framework 4.x, administrator access,
and an already working compatible offline installation with
`sp24/sp24-cod.exe` and `!start_sp24.bat` in the game root are required.
The launcher must use the **same Windows account as the game**, including UAC.

The examined EXE SHA256 is:

```text
18360ffef4f9dccd2f16106a8afe5111d3bf2fa5ace9430f9e4b28e9af6342d6
```

The existing `discord_game_sdk.dll` must also contain the matching original
Lua hook at RVA `0x4037B0`, length `0x720`, SHA256
`9862a33e05b09ee4be3ca7a4e950178851ecd235f1e2f3cf5091609e53b55b88`.
Further runtime code/layout checks apply. A matching EXE alone is insufficient.
Unknown builds must fail the relevant guards; do not edit guards to force them.
Start with BO6 closed because the early Lua phase must precede its first load.
This is not a general retail/online patch. This repository supplies no game,
authentication, offline-emulator, DLL or driver distribution.

## Build and install

From the repository root in Windows PowerShell, build the optional EXE:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Launcher.ps1
```

This uses the installed Windows x64 .NET Framework compiler and embeds the
`asInvoker` manifest. It needs no external game binaries. The procedure is
repeatable; compiler versions/timestamps can change the resulting file hash.
The source repository intentionally does not track a compiled EXE. A published
GitHub Release triggers Windows CI to build and attach the complete portable ZIP
after the offline checks pass. `scripts/New-Release.ps1` builds the same explicit-
file package locally; it never recursively bundles your personal data.

1. Close BO6 and any previous bridge window.
2. Back up your existing **`BO6_Personal_Progress_Data`** folder while closed.
3. Copy only `BO6_Personal_Progress` into the game root, beside `sp24` and
   `!start_sp24.bat`. When updating, replace code; **keep the separate Data folder**.
4. Run `Run_BO6_Personal_Progress.cmd`, or the built
   `Run_BO6_Personal_Progress.exe`. Confirm the game account's administrator prompt.
5. Wait for **ПАТЧ АКТИВЕН**, then continue your existing personal slot.
   A fresh Data folder initializes a separate personal campaign profile.

For a time-tracking application, select
`<game root>\BO6_Personal_Progress\Run_BO6_Personal_Progress.exe`.
The complete folder is required; the EXE is not a single-file game bundle.
The CMD keeps its original final prompt; the EXE skips only the exact verified
terminal prompt and propagates failures. Do not terminate the wrapper during play.

## Data, backups and errors

Data lives at `<game root>/BO6_Personal_Progress_Data/`:
`campaign.bo6profile`, `slot1.bo6local` through `slot3.bo6local`, their `.bak`
files, `backups/` and `logs/`. Do not publish this directory or diagnostic ZIPs.
Do not delete a profile to address a checkpoint error or reset it accidentally.

The bridge logs local writes separately from the notification. A red save error
means the newest progress is **not confirmed**, regardless of any game caption.
Retain backups and inspect the local log ZIP. A changed caption script skips
only that visual transform; it must not stop the working save bridge.
Launcher logs are in `%LOCALAPPDATA%/BO6_Personal_Progress_Launcher` for the
account running each launcher process; an elevated child may use another profile.

## If Windows blocks settings writes

Only investigate this if settings reset and Defender Operational event **1123**
identifies this exact game EXE and the settings folder. Do not disable Defender,
CFA or antivirus scanning. With the game closed, an administrator can add only
the verified game application to Controlled Folder Access's allow list:

```powershell
$exe='C:\Games\Call of Duty. Black Ops 6\sp24\sp24-cod.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {throw 'Check the game path'}
Add-MpPreference -ControlledFolderAccessAllowedApplications $exe
```

This appends an application permission; it is not an antivirus scan exclusion.
Do not whitelist PowerShell. Restart the game after allowing its exact EXE.
The project itself never changes Defender preferences. Microsoft documentation:
[CFA events](https://learn.microsoft.com/en-us/defender-endpoint/controlled-folder-access-monitor),
[application permissions](https://learn.microsoft.com/en-us/powershell/module/defender/add-mppreference).

## Uninstall / return to the original launch path

Close BO6 normally, then close the bridge. Its process-memory changes disappear
with the game process. Launch through your existing original `!start_sp24.bat`
instead of Personal Progress. You may remove the code folder after closing it;
keep `BO6_Personal_Progress_Data` backed up unless intentionally discarding your
personal progress. This project does not restore or distribute original game
binaries, clean experiment folders, install MFG or reset Windows security rules.

## Checks and contribution scope

`tests/Test-Offline.ps1` parses scripts, verifies the unchanged runtime hashes,
compiles the seven C# sources and runs data-format/checksum/caption checks.
It does not open a game, execute the native leaf, read personal saves or patch
memory. Windows CI also compiles the launcher. CI success is not a playthrough
or a Windows game-integration result. The normal runtime separately performs
its existing bounded own-process native preflight before starting the game.

The runtime files remain byte-identical to the working v024 package; the
launcher adapter pins eleven of them. Contributions changing those files must
review compatibility and integrity pins together, without weakening guards.

MIT applies to original authored code only. See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for embedded game-derived
compatibility/fixture bytes that are **not** granted an MIT license.
