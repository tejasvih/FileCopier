# Photo Copier for iPhone

Back up every photo and video on an iPhone to a Windows folder over USB: original files, byte for byte, including edited versions and Live Photo videos. The phone is only read, never changed, and reruns copy only what is new.

> Not affiliated with, endorsed by, or sponsored by Apple Inc. iPhone is a trademark of Apple Inc.
>
> Use at your own risk. The authors are not responsible for any loss of photos or other data. See [Disclaimer](#disclaimer).

## Why

Windows' own import (the Photos app or File Explorer) goes through the phone's camera interface (PTP, exposed to Windows as a Windows Portable Device). With large libraries it can stop partway, convert HEIC to JPEG, or skip files. While building this tool on a library of about 65,000 files, the camera interface also showed a folder list that temporarily shrank, a video that always read as zero bytes, and, after a device error, later reads that returned the previous file's bytes.

Photo Copier reads the camera roll through **AFC** (Apple File Conduit), the file service Apple's own tools use. Files are copied exactly as stored on the phone.

- **Originals, no conversion:** HEIC, JPEG, PNG, HEVC/MOV, Live Photo videos, and `.AAE` edit files.
- **Edits included:** edited versions are saved beside the originals under the names Windows uses (`IMG_E1234.HEIC`).
- **Resumable:** files already backed up with the same size and date are skipped.
- **Safe writes:** each file downloads to a temporary file, must match the size the phone reports, and only then replaces the backup copy. A failed or cut-off transfer never overwrites an existing backup.
- **Read-only:** never writes to or deletes from the phone, and never deletes anything in the backup folder.
- **No lost duplicates:** different photos that share a file name in the same phone folder are all kept.

## Requirements

- Windows 10 or 11, 64-bit.
- **Apple Devices** app from the Microsoft Store, or iTunes. It installs the Apple Mobile Device Service this tool talks to. No Apple software is bundled.
- A USB cable, the phone unlocked, and **Trust** tapped when the phone asks.
- Enough free disk space for the whole library.

## Quick start

1. Download `PhotoCopier-<version>-win-x64.zip` from [Releases](https://github.com/tejasvih/FileCopier/releases) and extract it. No .NET installation is needed.
2. Connect the phone by USB, unlock it, and tap **Trust**. If the phone has never been connected to this PC, open the Apple Devices app once.
3. Double-click `PhotoCopier.exe`. The menu shows the backup folder in use:
   ```
   Using AFC (recommended).
   Backup folder: D:\iPhone Backup\
   Choose an action:
     1. Copy new and changed photos
     2. Preview what would be copied (read-only)
     3. Verify the backup against the iPhone (read-only; reads every file, about as long as a full copy)
     4. Check the iPhone connection (read-only)
     5. Copy, skipping files you name
     6. Show command-line help
     B. Change backup folder
     W. Switch to WPD (previous method)
     Q. Quit
   ```
4. Choose **B** and type your backup folder, for example `D:\iPhone Backup\`. Press Enter without typing to keep the current one. The folder is saved in the settings file and shown on the next start. If no folder is set yet, choosing 1, 2 or 3 asks for one first.
5. A good first run is 4, then 2, then 1. Run 3 before deleting anything from the phone. Press **Q** or **Ctrl+C** to stop after the current file; run again to continue. Pressing **Ctrl+C** a second time stops at once, which is also safe: a backup file is only replaced after its download is complete, so at most a `.partial` file is left. **W** switches every action to Windows Portable Devices and its own backup folder, and **A** switches back.

While copying, each line shows the position in the run, for example `File 1234/64831, folder 12/44`. Copied and verified files are green, files already in the backup with the same size and date are gray (`Skipped`), warnings are yellow and errors red.

Windows SmartScreen may warn that the program is unrecognized because it is not code-signed. Choose **More info**, then **Run anyway**.

Before copying:

- If iCloud Photos is set to **Optimize iPhone Storage**, the phone holds only small previews of many photos. Switch to **Download and Keep Originals** in the phone's Photos settings and wait for the download to finish. Files that exist only in iCloud cannot be copied over USB.
- Albums, favorites, people and captions are records in the Photos app's database, not files. They are not backed up.

## Command line

```powershell
PhotoCopier                    # menu; copies without the menu when input is redirected
PhotoCopier --copy             # copy without the menu
PhotoCopier --verify           # read-only: read every file again and compare it byte for byte with the backup
PhotoCopier --scan-only        # read-only: list every file and preview what a copy would transfer
PhotoCopier --diagnose         # read-only connection and folder check
PhotoCopier --exclude-file "126APPLE\IMG_6742.HEIC"   # skip one file; repeat for more
PhotoCopier --backup-folder "E:\Other Backup"         # another backup folder for this run only (not saved)
PhotoCopier --wpd [...]        # use Windows Portable Devices instead of AFC
PhotoCopier --help             # also -h or /?
```

Exit codes: `0` completed, `1` failed or incomplete, `2` canceled.

`--exclude-file` is for a file the phone repeatedly fails to send. The path is relative to `/DCIM` (or to `SourcePath` with `--wpd`). Excluded files are reported as errors, so the run still ends as incomplete. An exclusion that matches no file stops the run before copying, so a typo cannot hide the wrong file. Exclusions cannot be combined with `--verify`, `--diagnose` or `--scan-only`.

`--verify` reads every file from the phone again and compares it with its backup copy as the bytes arrive, without writing to the phone or the backup. It takes about as long as a full copy. Each problem is reported as missing from the backup, a different size, or different at a given byte. A copy that differs but has the same size and date is not replaced by copying again, so delete that backup copy first. Verification ends with exit code `0` only when every file matches.

## Settings

`appsettings.json` sits beside `PhotoCopier.exe`. Comments (`//`) are allowed. Settings in an optional `appsettings.local.json` beside it override `appsettings.json`.

Changing the backup folder in the menu (**B**) saves it to the file it takes effect from: `appsettings.local.json` when that file sets it, otherwise `appsettings.json`. Only that value changes; comments and other settings are kept. To edit by hand, double every backslash: `"AfcDestinationPath": "D:\\iPhone Backup\\"`.

| Setting | Used by | Meaning |
|---|---|---|
| `AfcDestinationPath` | AFC (default) | Backup folder. Required for copying, preview and verify. Set it in the menu (**B**). |
| `SourcePath` | `--wpd` | Phone folder to copy, normally `\Internal Storage`. |
| `DestinationPath` | `--wpd` | Backup folder for `--wpd`. Must differ from `AfcDestinationPath`. Set it in the menu after **W**, then **B**. |

When building from source, put personal settings in `FileCopier/appsettings.local.json`. That file overrides `appsettings.json`, is ignored by git, and is never included in a published release.

## Backup layout (AFC)

- **Originals** keep the phone's folders: `/DCIM/126APPLE/IMG_6742.HEIC` becomes `<AfcDestinationPath>\126APPLE\IMG_6742.HEIC`. These `1xxAPPLE` folders are the phone's real folders. The month folders (`202409_a`) that Windows shows are generated by the camera interface and hold the same files.
- **Edits** are stored beside the originals, named the way Windows exports them. For an edited `IMG_6164`:
  - `FullSizeRender.heic` becomes `IMG_E6164.HEIC`;
  - `Adjustments.plist` becomes `IMG_6164.AAE`;
  - the camera roll's own `IMG_6164.AAE` becomes `IMG_O6164.AAE`.

  Other files in the phone's edit folders (`Penultimate*`, previews) are the Photos app's working files. Windows does not export them either.
- **Photos synced from a computer** (iTunes/Finder sync) live outside the camera roll in `/PhotoData/Sync/100SYNCD` and are copied to `<AfcDestinationPath>\100SYNCD`.
- **Same name, different photo:** each one gets a stable suffix derived from its device ID, for example `IMG_6746__<16 hex characters>.MOV`.
- A program that is forcibly ended can leave a `.iphone-photo-*.partial` file. It is never treated as a finished backup and can be deleted.

## How it works

The AFC client in [FileCopier/Afc](FileCopier/Afc) is written from scratch and uses no third-party code:

1. It connects to the Apple Mobile Device Service (usbmuxd) on `127.0.0.1:27015`.
2. It opens a lockdownd session over TLS, using the pairing record the Apple Mobile Device Service created when the phone was trusted. The phone's certificate is pinned to that record.
3. It starts AFC and sends read-only requests. It never pairs, writes or deletes.

Reliability:

- Every reply must carry its request's number and expected type, so a reply can never be credited to another file.
- Any transport or framing error discards the session. The operation reconnects and retries after 2, 5 and 15 seconds.
- A file that still fails is recorded and copying continues. Three failed files in a row stop the run (phone unplugged or locked). The exit code stays `1` while any error remains.
- A file is skipped when the backup copy is not empty and has the same size and modification time. Empty backup files are always retried, and any size difference triggers a fresh copy. This is a size and date check, not a checksum.
- A file the phone's file system lists as 0 bytes has nothing to copy. It is shown as `empty on the iPhone` and is not an error. Such files are usually left over from an interrupted capture, and the Photos app does not show them. With `--wpd`, a zero size is an error, because the camera interface can list zero for a file that has content.

### Windows Portable Devices (`--wpd`)

The previous method, kept for comparison. It reads `SourcePath` through the same camera interface File Explorer uses and mirrors that folder layout into `DestinationPath`. Because of the problems described above, copying stops at the first failed file. Unplug and reconnect the phone before retrying. Before each download, fresh metadata must match the scanned file, and every page of each folder listing is checked, so an incomplete listing cannot look successful. Prefer AFC.

## Troubleshooting

| Message | What to do |
|---|---|
| `No backup folder is set ...` | Choose **B** in the menu and type the backup folder (see Quick start). |
| `Apple Mobile Device Service (usbmuxd) is not running ...` | Install or open the Apple Devices app, then reconnect the phone. |
| `No iPhone found through Apple Mobile Device Service ...` | Connect by USB, unlock the phone and tap **Trust**. Try another cable or port. |
| `This computer has no pairing record for the iPhone ...` | Unlock the phone, tap **Trust**, and open the Apple Devices app once. |
| `Multiple Apple devices found ...` | Connect only the iPhone you want to copy. |
| `COMException (0x80042008)` with `--wpd` | Invalid storage ID. Reconnect the unlocked phone, allow access, and rerun `--diagnose`. |
| One file fails on every run | Unplug and reconnect the phone and rerun. If it still fails, skip it with `--exclude-file`. |

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
./build.ps1                                # tests, then a self-contained PhotoCopier.exe in .\publish
./build.ps1 -Output D:\Tools\PhotoCopier   # another folder
./build.ps1 -SkipTests
```

[build.ps1](build.ps1) uses the same settings as the release build, so the single `PhotoCopier.exe` runs on a PC without .NET. Rebuilding keeps the `appsettings.json` already in the output folder, copies `FileCopier/appsettings.local.json` there if the folder has none, and replaces files from an earlier build. Close Photo Copier first.

For development:

```powershell
dotnet build PhotoCopier.sln --configuration Release
dotnet test PhotoCopier.sln --configuration Release
.\FileCopier\bin\Release\net10.0-windows7.0\PhotoCopier.exe
```

The tests need no phone. They cover the AFC protocol client, the property-list codec, edit naming, settings validation, the menu and command line, collision-safe file naming, byte-for-byte verification, interrupted, empty and truncated downloads, folder-listing failures, and stream handling.

Windows Portable Devices access uses [MediaDevices](https://github.com/Bassman2/MediaDevices), built from pinned source with fixes so that listing errors are reported instead of silently skipped. See [Vendor/MediaDevices/README.md](Vendor/MediaDevices/README.md).

### Releasing

Pushing a version tag runs the tests and publishes a self-contained Windows zip as a GitHub Release. [release.ps1](release.ps1) picks the next version, checks that `main` is committed and pushed, runs the tests, and pushes the tag:

```powershell
./release.ps1                  # next patch version, e.g. v1.0.0 -> v1.0.1
./release.ps1 -Bump minor      # v1.0.1 -> v1.1.0 (or -Bump major)
./release.ps1 -Version 2.0.0   # a specific version
./release.ps1 -WhatIf          # show the next version without releasing
```

## Disclaimer

This software is provided "as is", without warranty of any kind, and you use it at your own risk. To the extent permitted by law, the authors and contributors are not liable for any loss, corruption or deletion of photos, videos or other data, for damage to any phone, computer or storage device, or for any other damages arising from its use. See [LICENSE](LICENSE) for the full terms.

- **Keep more than one backup.** Photos are irreplaceable. A backup made by this tool can be incomplete or wrong even when it reports success, for example after a hardware, USB, driver or disk fault.
- **Check the backup before deleting anything from the phone or from iCloud.** Run Verify (menu 3 or `--verify`), open a sample of photos and videos from the backup folder, and compare the file count with the phone.
- **Apple can change the phone's interfaces at any time.** This tool uses the Apple Mobile Device Service and the file services Apple's own software uses. These are not public, documented interfaces, and an iOS or Apple Devices update may stop the tool from working or change what it can read.
- This project is not affiliated with, endorsed by, or sponsored by Apple Inc., and is not supported by Apple.

## License

[MIT](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
