# Ducky Multi VRC Launcher

A Windows desktop launcher for multiple VRChat clients, with monitor selection and an automatic grid.

For repository owners: see [easy GitHub upload instructions](GITHUB_SETUP.md).

## Use

Open **Ducky Multi VRC Launcher.exe**.

1. Enter the number of additional clients to open (1-16). There is no default count.
2. Select a monitor. The list shows its Windows device name, resolution, position, and whether it is the main display.
3. Check the live grid preview and detected VRChat installation. Use **Browse** if Steam detection did not find the correct `launch.exe`.
4. Click **Launch clients** once. Each newly launched client is capped at 20 FPS.

**Close all clients** stops the queue and requests a normal shutdown of all clients identified as opened by this launcher. Your main client and all clients open before this session are excluded. If a client is still starting, the launcher watches for its window for up to 90 seconds and requests that it close too. Keep the launcher open while closing. Clients that do not respond are reported and are not force-killed.

**Stop launching** cancels future launches. It does not close clients that are already open. Closing the launcher also stops its queue and arrangement timer.

Existing VRChat clients, including your main client, are excluded. This launcher arranges only windows it identifies as newly opened during its own session. It uses debug profiles starting at 101; each needs its own manual account login. Use separate accounts for simultaneous online sessions. Existing sessions using the same account may conflict even if the local profile numbers differ.

## Count and arrangement behavior

- A request for two clients issues at most two launch requests, never eight.
- There is no retry loop. If the result of a launch is uncertain, the queue stops instead of creating a duplicate.
- Only one Ducky launcher may run at once. The Launch button is disabled after a session starts, preventing accidental double-click launches.
- New game windows are detected through Windows window enumeration rather than relying on protected VRChat process command lines or `MainWindowHandle`.
- Each detected client is assigned its own grid tile. If Unity recreates its window while loading, its tile follows the same process.
- The grid fills the selected monitor's usable area, excluding the taskbar. An incomplete final row stretches to fill its width.
- Windows are placed as they appear, during the gaps between launches, and for a settling period afterward. The launcher reports sizing failures once in its status area instead of printing repeated console warnings.
- If multiple unexpected new VRChat windows appear, the queue stops rather than guessing which one belongs to it. Avoid starting unrelated VRChat clients during a launch session.
- A client can briefly appear in its normal startup position before its game window becomes detectable. A slow or unidentifiable client stops further launches after 90 seconds. No automatic replacement is launched.

## Requirements

Windows 10/11, .NET Framework 4.x, and an installed Steam copy of VRChat. Steam library folders are detected, including installations on other drives. Browse can select an installation manually. An administrator account is not required by the launcher.

The app does not include VRChat or create accounts. It does not change avatar quality, visibility, account settings, network configuration, or server restrictions. A 20 FPS cap limits rendering rate, not RAM or graphics-memory usage. Running many clients requires substantial resources.

## Observed limitation

On the development setup, only **three accounts total** (the main account plus two additional accounts) could occupy the same online instance. Further accounts received error **32752** and were redirected to another instance. This remains unresolved and is not established as a universal VRChat limit. This launcher does not guarantee that all opened clients can join the same instance.

The earlier console launcher also created duplicate clients when process identification failed. That implementation and its retry loop have been removed. The desktop replacement enforces a launch budget and never automatically reissues a launch request.

This is an independent project. VRChat also offers [VRC Quick Launcher](https://vcc.docs.vrchat.com/tools/vrc-quick-launcher/).

## Build and test

The source is in `src/DuckyLauncher.cs`. Build with Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test.ps1
```

Build uses the .NET Framework compiler bundled with Windows; no downloaded packages are required. The executable uses the Windows GUI subsystem, so no terminal accompanies it.

Tests cover grid counts 1-16 across landscape, portrait, odd-sized, 4K, and negative-coordinate monitor layouts. They check complete coverage, bounds, no overlaps, maximum launch requests, cancellation, and close-all ownership filtering. They do not start VRChat. The UI has also been rendered and visually inspected. Actual VRChat startup and placement still require end-to-end validation on the user's machine; no successful game run is claimed by the automated tests.

For a UI-only preview image (does not start VRChat):

```powershell
& '.\Ducky Multi VRC Launcher.exe' --preview "$PWD\preview.png"
```

## Privacy and repository contents

New client logs are saved under `%LOCALAPPDATA%\DuckyMultiVRCLauncher\logs`. Logs may include sensitive account information; do not post raw logs publicly.

Existing development diagnostics are kept locally under `private/`, ignored by Git, and are not needed to run the app. Source, build script, tests, README, and LICENSE belong in the repository. The generated executable is ignored by Git; distribute it separately as a GitHub release asset if desired. No ZIP is required to test the app locally.

## License

MIT. See [LICENSE](LICENSE).


