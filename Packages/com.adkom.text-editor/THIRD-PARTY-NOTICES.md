# Third-Party Notices

The `RoslynBinaries~` folder of this package redistributes the following
unmodified binary assemblies, which are installed into your project only
when you enable semantic features in the ADKOM Text Editor settings:

- Microsoft.CodeAnalysis.dll (Roslyn, v4.8.0)
- Microsoft.CodeAnalysis.CSharp.dll (Roslyn, v4.8.0)
- System.Buffers.dll
- System.Collections.Immutable.dll
- System.Memory.dll
- System.Numerics.Vectors.dll
- System.Reflection.Metadata.dll
- System.Runtime.CompilerServices.Unsafe.dll
- System.Text.Encoding.CodePages.dll
- System.Threading.Tasks.Extensions.dll

These are © .NET Foundation and Contributors, obtained from nuget.org,
and licensed under the MIT License:

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

Roslyn: https://github.com/dotnet/roslyn
Runtime libraries: https://github.com/dotnet/runtime

---

## Zork I / II / III story files (downloaded on request, not bundled)

The Games menu can fetch three Infocom story files for the bundled Z-Machine interpreter. **Nothing is downloaded unless you pick a game**, and no story file ships inside this package. Each download is pinned to a specific commit of the corresponding `historicalsource` repository:

- Zork I — https://github.com/historicalsource/zork1
- Zork II — https://github.com/historicalsource/zork2
- Zork III — https://github.com/historicalsource/zork3

These three repositories carry an MIT license; they are the only Infocom titles in that collection that do, and no other game there is offered. The files are written to a per-user folder outside the Unity project, are never imported as assets, and can be deleted at any time. The interpreter also opens any Z-Machine story file you already own.

---

## SCOWL (Spell Checker Oriented Word Lists)

The bundled English spell-check dictionary (`Editor/SpellCheckData~/words-en.txt`)
is derived from SCOWL by Kevin Atkinson (http://wordlist.aspell.net/).

The collective work is Copyright 2000-2018 by Kevin Atkinson. Permission to
use, copy, modify, distribute and sell these word lists, the associated
scripts, the output created from the scripts, and its documentation for any
purpose is hereby granted without fee, provided that the above copyright
notice appears in all copies and that both that copyright notice and this
permission notice appear in supporting documentation. Kevin Atkinson makes
no representations about the suitability of this array for any purpose. It
is provided "as is" without express or implied warranty.

The complete SCOWL copyright statement, including its component word lists'
notices, ships alongside the dictionary as
`Editor/SpellCheckData~/SCOWL-Copyright.txt`.
