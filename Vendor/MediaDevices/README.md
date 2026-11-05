# Pinned MediaDevices source

Upstream: https://github.com/Bassman2/MediaDevices

Source commit: `0d36056524ffcfa70c1a5cedac44d566f002cc4f`.
This is the commit recorded in the informational version of the NuGet 2.0.0
assembly used by this application. The upstream `2.0.0` release tag is a
different merge commit; this copy is pinned to the installed assembly's source.

The upstream MIT license is preserved in [LICENSE](LICENSE). Only the shared
library source and its custom field-finder source generator are included.
The generator has build-only Roslyn dependencies; the library has no runtime
package dependencies. The .NET SDK provides generated COM and LibraryImport
support. The local project targets only `net10.0-windows7.0` and is unsigned;
the upstream signing key is not distributed in the source repository.

## Local changes

- `Internal/Item.cs`: both child-enumeration overloads share one checked path.
  Object metadata errors are propagated, including `FileNotFoundException`, so
  unreadable objects cannot silently disappear from a successful scan.
- `Internal/CheckedObjectEnumerator.cs`: checks the initial `EnumObjects` result,
  rejects a null enumerator, resets the fetched count and checks every `Next`
  result, and validates returned counts and IDs. Normal `S_FALSE` final pages
  remain valid; an empty page without `S_FALSE` fails rather than claiming a
  complete listing. This fixes upstream paths that ignored later-page failures.
- `AssemblyInfo.cs`: grants the application test assembly access to the native
  pagination boundary for deterministic tests without an attached phone.
- `MediaFileInfo.cs` and `Internal/ProtocolHandler.FileSystem.cs`: add
  `MediaFileInfo.CopyTo(Stream)`, reusing the scanned object instead of looking
  up its path through every ancestor and sibling before each transfer. The
  destination must be writable and remains open. Opening, reading, and disposing
  the native source stream all stay on the device worker. The existing file-path
  overload shares this transfer implementation. Both direct copy overloads
  refresh metadata on the worker and compare it with an immutable scan snapshot
  before opening the stream; changed identity, name, size, or timestamps aborts
  the copy rather than adopting a different object behind a cached ID.
- `Interfaces/IStream.cs`, `Internal/StreamWrapper.cs`, and native resource
  bindings: match native 32-bit read/write counts, validate native counts and
  buffer arguments, handle partial writes, and enforce idempotent disposal.
  Transfer resources use unique COM wrappers and remain owned by the source
  stream until it closes. Stream and resource wrappers release in that order,
  including failed opens. This replaces forced collection and a fixed sleep
  with deterministic resource ownership; it does not establish that a driver
  can recover after returning a wrong or failed resource stream.
- `MediaDevices.csproj`: minimal unsigned, deterministic .NET 10 build using the
  preserved upstream generator. The local file version is `2.0.0.4001`.
- `Generator/FindGenerator/FindGenerator.csproj`: build-only Roslyn dependencies
  updated to 5.9.0, matching the installed .NET 10 SDK compiler.

Regression tests live in `FileCopier.Tests/DeviceEnumerationTests.cs` at the
repository root. They cover a 128-object listing, second-page failure, initial
enumeration failure, null enumerators, final partial pages, count resets,
metadata failures, and malformed counts/IDs.

Keep these changes narrow when updating upstream. Remove this source copy once
an upstream release includes equivalent enumeration failure handling and passes
the regression tests. A repeated scan through the unpatched public API cannot
reliably detect a consistently truncated listing.
