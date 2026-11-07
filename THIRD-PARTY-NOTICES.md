# Third-party notices

Photo Copier for iPhone includes or redistributes the components below. Each is
used under the license shown.

## MediaDevices

- Source: https://github.com/Bassman2/MediaDevices
- Included as modified source in `Vendor/MediaDevices` (see its README for the changes) and
  compiled into the released program.

```
MIT License

Copyright (c) 2017 - 2023 Ralf Beckers

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## .NET runtime and Microsoft.Extensions.Configuration

- Source: https://github.com/dotnet/runtime
- The released program is self-contained: it bundles the .NET runtime and the
  Microsoft.Extensions.Configuration packages (Binder, Json and their dependencies).
- Components the .NET runtime itself incorporates are listed in
  https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Build and test tools (not redistributed)

Used only to build or test from source:

- Roslyn (Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.Analyzers): MIT
- xUnit.net (xunit, xunit.runner.visualstudio): Apache License 2.0
- Microsoft.NET.Test.Sdk: MIT

## Apple software

The program talks to the Apple Mobile Device Service, which users install separately with
Apple's Apple Devices app or iTunes. No Apple software is included or redistributed.
iPhone is a trademark of Apple Inc., registered in the U.S. and other countries. This
project is not affiliated with, endorsed by, or sponsored by Apple Inc.
