# Photo Copier for iPhone

Back up every photo and video on an iPhone to a Windows folder over USB: original files, byte for byte, including edited versions and Live Photo videos. The phone is only read, never changed, and reruns copy only what is new.

> Not affiliated with, endorsed by, or sponsored by Apple Inc. iPhone is a trademark of Apple Inc.

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
2. Open `appsettings.json` in Notepad and set `AfcDestinationPath` to your backup folder. Double every backslash:
   ```json
   "AfcDestinationPath": "D:\\iPhone Backup\\",
   ```
3. Connect the phone by USB, unlock it, and tap **Trust**. If the phone has never been connected to this PC, open the Apple Devices app once.
4. Double-click `PhotoCopier.exe` and choose from the menu:
   ```
   1. Copy photos to D:\iPhone Backup\ (AFC, recommended)
   2. Preview what would be copied, read-only (AFC)
   3. Check the iPhone connection, read-only (AFC)
   4-6. The same using Windows Portable Devices (previous method)
   7. Show command-line help
   ```
   A good first run is 3, then 2, then 1. Press **Q** or **Ctrl+C** to stop after the current phone operation; run again to continue.

Windows SmartScreen may warn that the program is unrecognized because it is not code-signed. Choose **More info**, then **Run anyway**.

Before copying:

- If iCloud Photos is set to **Optimize iPhone Storage**, the phone holds only small previews of many photos. Switch to **Download and Keep Originals** in the phone's Photos settings and wait for the download to finish. Files that exist only in iCloud cannot be copied over USB.
- Albums, favorites, people and captions are records in the Photos app's database, not files. They are not backed up.

## Command line

```powershell
PhotoCopier                    # menu; copies without the menu when input is redirected
PhotoCopier --copy             # copy without the menu
PhotoCopier --scan-only        # read-only: list every file and preview what a copy would transfer
PhotoCopier --diagnose         # read-only connection and folder check
PhotoCopier --exclude-file "126APPLE\IMG_6742.HEIC"   # skip one file; repeat for more
PhotoCopier --wpd [...]        # use Windows Portable Devices instead of AFC
PhotoCopier --help             # also -h or /?
```

Exit codes: `0` completed, `1` failed or incomplete, `2` canceled.

`--exclude-file` is for a file the phone repeatedly fails to send. The path is relative to `/DCIM` (or to `SourcePath` with `--wpd`). Excluded files are reported as errors, so the run still ends as incomplete. An exclusion that matches no file stops the run before copying, so a typo cannot hide the wrong file. Exclusions cannot be combined with `--diagnose` or `--scan-only`.

## Settings

`appsettings.json` sits beside `PhotoCopier.exe`. Comments (`//`) are allowed.

| Setting | Used by | Meaning |
|---|---|---|
| `AfcDestinationPath` | AFC (default) | Backup folder. Required for copying and preview. |
| `MinimumExpectedFiles` | both | Optional. Stop before copying if the phone lists fewer files than this. `0` turns it off. |
| `SourcePath` | `--wpd` | Phone folder to copy, normally `\Internal Storage`. |
| `DestinationPath` | `--wpd` | Backup folder for `--wpd`. Must differ from `AfcDestinationPath`. |

`MinimumExpectedFiles` guards against a USB glitch where the phone shows only part of the library and the run would otherwise look complete. Run `--scan-only` once, then set it to about the number of files found. It cannot detect missing files that are offset by newly added ones.

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

### Windows Portable Devices (`--wpd`)

The previous method, kept for comparison. It reads `SourcePath` through the same camera interface File Explorer uses and mirrors that folder layout into `DestinationPath`. Because of the problems described above, copying stops at the first failed file. Unplug and reconnect the phone before retrying. Before each download, fresh metadata must match the scanned file, and every page of each folder listing is checked, so an incomplete listing cannot look successful. Prefer AFC.

## Troubleshooting

| Message | What to do |
|---|---|
| `Set AfcDestinationPath in appsettings.json ...` | Set the backup folder (see Quick start). |
| `Apple Mobile Device Service (usbmuxd) is not running ...` | Install or open the Apple Devices app, then reconnect the phone. |
| `No iPhone found through Apple Mobile Device Service ...` | Connect by USB, unlock the phone and tap **Trust**. Try another cable or port. |
| `This computer has no pairing record for the iPhone ...` | Unlock the phone, tap **Trust**, and open the Apple Devices app once. |
| `Multiple Apple devices found ...` | Connect only the iPhone you want to copy. |
| `Source scan found N files, below configured MinimumExpectedFiles ...` | Reconnect and rerun. If photos were deliberately deleted, lower the setting. |
| `COMException (0x80042008)` with `--wpd` | Invalid storage ID. Reconnect the unlocked phone, allow access, and rerun `--diagnose`. |
| One file fails on every run | Unplug and reconnect the phone and rerun. If it still fails, skip it with `--exclude-file`. |

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build PhotoCopier.sln --configuration Release
dotnet test PhotoCopier.sln --configuration Release
.\FileCopier\bin\Release\net10.0-windows7.0\PhotoCopier.exe
```

The tests need no phone. They cover the AFC protocol client, the property-list codec, edit naming, settings validation, the menu and command line, collision-safe file naming, interrupted, empty and truncated downloads, folder-listing failures, and stream handling.

Windows Portable Devices access uses [MediaDevices](https://github.com/Bassman2/MediaDevices), built from pinned source with fixes so that listing errors are reported instead of silently skipped. See [Vendor/MediaDevices/README.md](Vendor/MediaDevices/README.md).

### Releasing

Pushing a version tag runs the tests and publishes a self-contained Windows zip as a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## License

[MIT](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This software is provided as is, without warranty. Photos are irreplaceable: keep more than one backup, and check the backup before deleting anything from the phone.
