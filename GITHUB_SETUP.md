# Put Ducky Multi VRC Launcher on GitHub

## 1. Create the repository

Open https://github.com/new and enter:

- Repository name: `Ducky-Multi-VRC-Launcher`
- Description: `Windows launcher for multiple VRChat clients with automatic monitor tiling and one-click close all.`
- Visibility: **Public**

Leave the options for adding a README, license, and .gitignore off. Those files already exist here. Click **Create repository**.

## 2. Upload the source

Click **uploading an existing file** on the new repository page. Drag in only these files and folders from the Army folder:

- `src` folder
- `tests` folder
- `Build.ps1`
- `README.md`
- `GITHUB_SETUP.md`
- `LICENSE`
- `.gitignore`

Preserve the `src` and `tests` folder structure. In Windows Explorer, enable **View > Show > Hidden items** if you cannot see `.gitignore`.

Write `Initial release` in the commit message box, then click **Commit changes**.

Do not upload `private`. It contains local diagnostic data. The executable goes in the release in step 3, rather than in the source files. Browser uploads do not automatically honor .gitignore.

## 3. Add the downloadable app

On your repository page, click **Releases**, then **Create a new release** (or **Draft a new release**).

- Create a tag: `v0.1.0`
- Release title: `Ducky Multi VRC Launcher v0.1.0`
- Description: copy the release notes below.
- Drag `Ducky Multi VRC Launcher.exe` into the release attachment area.
- Mark this first experimental release as a **pre-release**.
- Click **Publish release**.

No ZIP is needed. People download the EXE directly from Releases and open it.

### Release notes to copy

Ducky Multi VRC Launcher provides a Windows desktop interface for opening multiple VRChat clients.

- Choose 1-16 additional clients with no default count.
- Select a monitor and see the grid before launching.
- Automatic tiling fills the usable monitor area.
- New clients are capped at 20 FPS.
- A launch budget and single-session controls prevent automatic duplicate launch requests.
- Stop launching cancels the queue.
- Close all clients closes this launcher's identified clients while leaving previously open clients alone.
- Steam installations are detected automatically; Browse supports manual selection.

Requires Windows 10/11, .NET Framework 4.x, and the Steam version of VRChat. Separate online accounts and sufficient system resources are required. The executable is unsigned.

Known limitation: the development setup could place only three accounts total in one online instance; additional accounts received error 32752 and were redirected. This remains unresolved and is not established as a universal limit. Opening multiple clients does not guarantee they can all join the same instance.

## 4. Share it

Share the repository's Releases page so people can find the EXE. The source page's Download ZIP option contains source code, not a built application.

## Official GitHub instructions

- Create a repository: https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository
- Upload files: https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository
- Manage releases: https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository
