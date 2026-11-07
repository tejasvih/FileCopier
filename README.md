# iPhone Photo Copier

Windows console app that copies the photos and videos on an iPhone over USB. Requires the .NET 10 SDK to build and .NET 10 runtime to run.

By default it reads the camera roll through **AFC** (Apple File Conduit), the file service Apple's own tools use, via the Apple Mobile Device Service installed with the Apple Devices app or iTunes. `--wpd` selects the previous Windows Portable Devices (Explorer view) transport. See [AFC transport](#afc-transport-default).

## Configure and run

Unlock the phone and accept **Trust/Allow** if prompted. Apple's [Windows import instructions](https://support.apple.com/en-gb/120267) recommend installing Apple Devices. If iCloud Photos optimizes device storage, download the full-resolution originals onto the phone first; USB copying cannot back up files that are available only in iCloud.

For original-format backups, Apple's [USB transfer guidance](https://support.apple.com/en-us/116944) describes **Settings → Apps → Photos → Keep Originals**, which disables HEIF/HEVC conversion during import. Original availability and USB conversion are useful checks for a persistently unreadable file; neither has been established as the cause of the reported device error.

Edit `FileCopier/appsettings.json` before building. Each transport has its own backup folder because their layouts differ: AFC (default) copies to `AfcDestinationPath`, and `--wpd` copies `SourcePath` to `DestinationPath`. The two folders must differ. AFC always reads the camera roll `/DCIM` and edits under `/PhotoData/Mutations/DCIM`.

```json
{
  "SourcePath": "\\Internal Storage",
  "DestinationPath": "E:\\Photos\\DCIM\\",
  "AfcDestinationPath": "E:\\Photos\\AFC\\",
  "MinimumExpectedFiles": 64810
}
```

The app always loads `appsettings.json` beside its executable. When running an existing build, edit the settings in that build's output directory. Rebuilding copies the project's settings over the output settings.

```powershell
dotnet build iPhonePhotoCopier.sln --configuration Release

# No parameters in a console window (or double-click): choose copy, preview or
# connection check, for AFC or WPD, from a numbered menu. With redirected input,
# no parameters copies through AFC. --copy copies without the menu.
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe

# Command-line help: --help, -h or /?
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe /?

# Read-only connection and folder check; does not download photos.
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe --diagnose

# Full read-only source scan, with discovered/visited counts and file progress.
# Also previews how many files and bytes a copy would transfer.
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe --scan-only

# Copy to the configured destination without the menu.
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe --copy

# Skip one phone file (path relative to /DCIM for AFC).
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe --exclude-file "126APPLE\IMG_6742.HEIC"

# Previous transport: Windows Portable Devices, paths relative to SourcePath.
.\FileCopier\bin\Release\net10.0-windows7.0\iPhonePhotoCopier.exe --wpd --exclude-file "202409_a\IMG_E7443.MOV"
```

Only connect the Apple device you want to copy. With `--wpd`, files are copied recursively, preserving their paths relative to `SourcePath`. For example, `\Internal Storage\202611_a\IMG_0001.HEIC` becomes `E:\Photos\DCIM\202611_a\IMG_0001.HEIC`. A source containing a `DCIM` subfolder retains that subfolder in the destination.

Existing nonempty files are skipped when size and supplied modification time match. If the phone supplies no modification time, size alone determines whether to skip. Zero-byte destination files are always retried. Any size mismatch triggers recopy, so a suspiciously small or truncated backup is compared against the phone's exact advertised size. There is no universal minimum size: valid `.AAE` sidecars can be smaller than 1 KB.

`MinimumExpectedFiles` is an explicit expectation for the source; both transports list the same library (originals, `.AAE` sidecars and edited renders). This workspace uses **64,810**, the count from the earlier verified WPD scan; AFC listed 64,827 on 2026-11-07. A smaller scan fails before downloading files, including in `--scan-only` mode, rather than reporting a reduced USB view as complete. If files were intentionally removed or `SourcePath` changed, review and update this expectation. Omitting the setting or using `0` disables the guard. Meeting the minimum is not proof of full source visibility: a count cannot detect missing files offset by newly added files.

Downloads go to temporary sibling files, are checked against the advertised byte length, and receive available timestamps before replacing the destination. A zero-byte source or an empty/truncated transfer fails instead of replacing an existing backup. A failed transfer leaves an existing backup intact. This size check is not a content checksum. A forcibly terminated process can leave a `.iphone-photo-*.partial` file; it is never treated as a completed backup.

With `--wpd`, copying stops at the first failed file. The reported long run showed that, after a device-read error, later reads could return the preceding file's bytes. Different sizes were rejected, but equal-sized sidecars could be accepted. Reconnect the phone before retrying a failed transfer. Files accepted during an affected older run need content verification; rerunning with the normal size/time skip check does not verify them.

To copy the remaining files, explicitly pass `--exclude-file` with an original phone file path relative to `SourcePath`. Repeat the option for additional files. Exclusions are matched before destination checks and downloads, and are reported as excluded errors with exit code `1`; the run cannot claim a complete backup. An exclusion that does not match any scanned file aborts before copying, preventing a typo from silently opening the problematic source. Collision suffixes are backup names; use the original phone path for exclusions. Diagnostic and scan-only modes cannot be combined with exclusions.

For the observed zero-byte video read, logical disconnect/reconnect and cancel/reconnect did not reliably restore subsequent known-good reads. Unplug and reconnect the unlocked phone before starting a run that excludes the unreadable file. The app does not automatically continue a failed transfer session.

Different photos or videos can share the same original filename inside an iPhone folder. Each such object receives a stable suffix derived from its persistent device ID, for example `IMG_6746__<16 hexadecimal characters>.MOV`. Every colliding object is preserved; an existing ambiguous `IMG_6746.MOV` is left untouched. If one object later disappears from the phone, an existing suffixed copy keeps its name. Repeated entries for the same native object are deduplicated only when their metadata agrees. Missing or conflicting identity information fails the plan rather than overwriting another object.

The app requests read-only phone access and never deletes source photos or unrelated destination files. Press **Q** or **Ctrl+C** to stop after the current device operation. Rerun to continue. Exit codes: `0` completed, `1` failed or incomplete, `2` canceled. Folder-reading failures count as errors, even when other files copied successfully.

## AFC transport (default)

The WPD failures described below (stale bytes after a device error, a zero-byte edited video, a listing that shrank to 87 folders) come from the phone's PTP responder behind the Explorer view, not from this app. AFC bypasses it: it reads the media partition as plain files, byte for byte, with no on-the-fly conversion.

The client in [FileCopier/Afc](FileCopier/Afc) has no third-party dependency: usbmuxd (`127.0.0.1:27015`), a lockdownd session (TLS with the pairing record created when the phone was trusted, the device certificate pinned to that record), and read-only AFC requests. It never pairs, writes or deletes on the phone. If the phone is not trusted, unlock it, tap **Trust**, and open the Apple Devices app once.

- **Reliability.** Every AFC reply must carry its request's number and expected type, so a reply can never be credited to another file. Any transport or framing error discards the session; the operation reconnects and retries after 2, 5 and 15 s. A download retry truncates the partial temporary file first. Because a failure cannot leak into later transfers, a file that still fails is recorded and copying continues; three consecutive failed files stop the run (phone unplugged or locked). The exit code stays `1` while any error remains.
- **Layout.** Originals keep the phone's folders: `/DCIM/126APPLE/IMG_6742.HEIC` becomes `<AfcDestinationPath>\126APPLE\IMG_6742.HEIC`. AFC shows the phone's real 40 `1xxAPPLE` folders of about 300 to 1,800 files each; the 128 `YYYYMM_a` month folders of the WPD view are generated by the phone and hold the same files. Edits are stored beside them under the names Windows exports: `Mutations/DCIM/126APPLE/IMG_6164/Adjustments/FullSizeRender.heic` becomes `IMG_E6164.HEIC`, `Adjustments.plist` becomes `IMG_6164.AAE`, and an edited item's camera-roll `IMG_6164.AAE` becomes `IMG_O6164.AAE`. Other files in `Adjustments` (`Penultimate*`, previews, poster frames) are Photos working files that Windows does not export either. `/DCIM` also holds a small `ispRegDump.bin`, which is copied like any other file. The 4 photos synced onto the phone from a computer (iTunes/Finder sync) live outside the camera roll in `/PhotoData/Sync/100SYNCD` and are copied to `<AfcDestinationPath>\100SYNCD`.
- **Separate from the WPD backup.** The AFC folder is a complete, self-contained copy; the WPD backup in `DestinationPath` is never read or changed by an AFC run and keeps photos that are no longer on the phone. A first AFC copy therefore transfers the whole library (64,827 files, 131 GB on 2026-11-07); later runs skip files whose size and timestamp match.

Verification on 2026-11-07, read-only unless stated: AFC scan of **44/44 folders, 64,827 files, zero errors in 68 s**. Compared by name and size with the WPD backup in `E:\Photos\DCIM` (91,510 files), 51,410 phone files were already there and 13,417 (39.7 GB) were not: photos and videos missing from that backup, or different photos that reuse an `IMG_` number from another year, plus their sidecars. A copy into a scratch folder transferred 1,801 files (1.8 GB) without retries; **all 798 that had a same-name, same-size file in the WPD backup were SHA-256 identical to it**, including 205 edited renders, and sidecars matched under the `IMG_n.AAE`/`IMG_On.AAE` names above. A rerun treated every copied file as up to date. The phone's own library database (`/PhotoData/Photos.sqlite`, read into a scratch copy) lists 26,203 items, none trashed and none stored only in iCloud; every item's original is on the phone, 26,199 in `/DCIM` and 4 in `/PhotoData/Sync/100SYNCD`. The other `/DCIM` files are Live Photo videos and `.AAE` sidecars. Albums, favorites, people and captions are database records, not files, and are not backed up by either transport. A full AFC copy into `E:\Photos\AFC` has not been run.

Known gaps: Windows exports some edited items' adjustments twice (`IMG_n.AAE` and an identical `IMG_On.AAE`); AFC stores them once. Files available only in iCloud are not on the phone and cannot be copied by either transport.

## Storage error fix

The reported `COMException (0x80042008)` originated in `GetDrives()` while requesting storage properties, before any photos were scanned. Microsoft identifies this as [Invalid StorageID](https://learn.microsoft.com/en-us/windows/win32/wpd_sdk/error-constants). The app now opens the configured folder directly without requesting drive metadata. This fixes the observed failure path; it does not establish an iOS version as the cause.

Configuration dependencies use Microsoft.Extensions.Configuration.Binder/Json 10.0.14. MediaDevices is built from the exact source commit recorded in the official 2.0.0 package, with a local enumeration correction. The stock package ignored some native page errors and silently skipped objects whose metadata could not be read. That allowed an incomplete listing to appear successful. The [pinned source and patch notes](Vendor/MediaDevices/README.md) document the correction and retained MIT license. Every native enumeration page is now checked, normal end-of-list must be confirmed, and metadata failures mark the scan incomplete. A pinned source project is necessary because the package's public API does not expose these discarded errors; it should be replaced when an upstream package provides equivalent checks.

MediaDevices 2 changes device enumeration to `MediaDeviceManager.Instance.GetDevices()`. Phone operations remain sequential. The old parallel scan and redundant copying implementations were removed. Files and subfolders now share one native directory enumeration, avoiding repeated metadata reads. Downloads reuse the scanned object through a worker-owned stream-copy overload, avoiding repeated source-tree searches and correctly distinguishing different objects with identical paths. Before opening a stream, fresh metadata must match the scanned object. Native streams and transfer resources have deterministic ownership and release order; native read/write counts use their correct 32-bit ABI.

Other fixes include recursive traversal, honoring the configured source, handling missing timestamps, safe destination-path validation, reliable copied/skipped/error counts, handled startup errors, and redirected-console support. The former `ThreadCount` setting is no longer used. Device connections request fresh folder listings instead of the driver's cached file list. Child folders are queued before reading files, so a file-reading error cannot hide descendants. Discovery failures retain folders already found and mark the run incomplete. Folder progress counts include the source root; 128 immediate subfolders therefore produce 129 visited folders if there are no deeper subfolders.

## Verify

```powershell
dotnet test iPhonePhotoCopier.sln --configuration Release
```

The 195 regression tests cover the AFC client (request/reply matching, broken sessions, handle release, file metadata), the property-list codec (including ignored DTDs), edit naming, per-transport destinations, the copy preview, failure isolation and the consecutive-failure stop, and interrupted and truncated downloads, zero-byte source rejection, retrying empty or undersized backups, preserving existing backups, timestamp failures, locked destination files, missing metadata, safe relative paths, configuration validation and minimum source count, command-line modes and exclusions, exclusion matching after collision renaming, folder traversal, native enumeration failures, collision-safe backup planning, stream ownership and byte-count handling, and immutable source identity checks. All pass in Debug and Release.

A complete read-only scan of the attached iPhone visited **129/129 folders** (128 source subfolders plus the source root), found **64,810 files**, and completed with **zero errors**, including backup-name planning. Two distinct videos sharing `IMG_6746.MOV` transferred by object reference into memory: **5,078,282 bytes** and **38,270,328 bytes**. Both matched their metadata sizes, had different content, and retained stable persistent IDs across connections. An earlier 78,046-byte photo transfer also matched its reported size.

A subsequent user-run backup exposed the device-error cascade described above. A read-only SHA-256 audit of all 802 sidecars accepted after its first error found **776 content mismatches** and 26 matches. With the guarded transfer implementation, **5,000 additional sidecar reads** matched their first-read hashes; a four-file AAE/HEIC/MOV sequence also matched advertised lengths. The first failing MOV still returned zero bytes on a fresh connection, so these checks do not establish that the phone can export every file or that a complete backup will succeed. No existing backup files were modified by verification. A full-library copy has not been validated.

After user approval, all **776 mismatched sidecars were repaired** from verified staged copies. An independent read-only check confirmed that **802/802 audited sidecars** match the audited source hashes and sizes, and **776/776 rollback copies** retain the previous backup hashes and sizes. Repair artifacts and journals remain under `FileCopier/bin/verification/staged-aae-repair/`. The unreadable video and older zero-byte backups remain unresolved; this repair does not establish a complete backup.

Another run exposed only **87 subfolders and 4,930 files**. A subsequent read-only native ID count returned **128 subfolders and 64,810 unique direct-child entries**, with every per-folder count matching the earlier verified scan, no missing folders, and no duplicated IDs. An extra native page request after nonempty `S_FALSE` pages returned no additional IDs in that probe. This confirms that source visibility recovered; it does not establish the trigger for the earlier reduced view or verify media contents. The report is `FileCopier/bin/verification/native-count/native-count-report.json`. The configured minimum now prevents this known count collapse from being reported as successful.
