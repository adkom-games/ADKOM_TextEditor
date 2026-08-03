# Unity bug report — ATG text job throws when a TextElement needs an emoji fallback atlas

Ready to paste into Unity's bug reporter (Help → Report a Bug). Everything below was measured on the reporter's own machine; the "ruled out" section exists so the triage engineer does not repeat it.

## Title

UI Toolkit: ATGTextJobSystem.ConvertMeshInfoToUIRVertex throws IndexOutOfRange / ArgumentOutOfRange when a TextElement containing an emoji is regenerated

## Environment

- Unity 6000.3.19f1, Windows
- UI Toolkit in an EditorWindow (IMGUI-hosted panel), Advanced Text Generator active
- Reproduced with the editor's default font assets — no custom TextSettings

## What happens

A `Label` whose text contains an emoji (a glyph supplied by OS font fallback rather than by a Unity `FontAsset`) throws from inside the text-generation job every time its mesh is regenerated. The first throw of a session is an `ArgumentOutOfRangeException` from `List<T>.get_Item`; subsequent ones are `IndexOutOfRangeException`. Both come from the same method.

Because a text selection drag regenerates the label under the pointer on **every** pointer-move event, pressing the mouse in such a label and dragging produces one exception per mouse-move — a continuous flood in the console.

## Minimal reproduction

1. Open an `EditorWindow` with a UI Toolkit root.
2. Add a single `Label` with `enableRichText = true` and `selection.isSelectable = true`.
3. Set its text to exactly:

   ```
   <size=17><b>🛡️ Safe by design</b></size>
   ```

   (U+1F6E1 U+FE0F — shield with emoji variation selector. 40 characters.)
4. Force the glyph to be cold: call `ClearFontAssetData(true)` on the loaded `UnityEngine.TextCore.Text.FontAsset` instances, **or** simply use an emoji not yet rendered this session.
5. Regenerate the label — `MarkDirtyRepaint()` followed by a repaint, or drag a text selection across it.

**Expected:** the label renders, and repeated regeneration is silent.
**Actual:** one exception per regeneration.

A caller-side repro is attached as `AteMdRepro.cs` in this report's zip: it dirties the labels of a rendered Markdown view and repaints synchronously via `EditorWindow.RepaintImmediately()`, printing the console-entry delta so the fault is countable rather than eyeballed.

## Stack traces

First occurrence in a session:

```
ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.
Parameter name: index
System.Collections.Generic.List`1[T].get_Item (System.Int32 index)
UnityEngine.UIElements.ATGTextJobSystem.ConvertMeshInfoToUIRVertex (System.Span`1[T] meshInfos, UnityEngine.UIElements.TempMeshAllocator alloc, UnityEngine.UIElements.TextElement visualElement, System.Collections.Generic.List`1[T] textElementIndicesByMesh, System.Collections.Generic.List`1[T] hasMultipleColorsByMesh, System.Collections.Generic.List`1[UnityEngine.Texture2D]& atlases, System.Collections.Generic.List`1[Unity.Collections.NativeSlice`1[UnityEngine.UIElements.Vertex]]& verticesArray, System.Collections.Generic.List`1[Unity.Collections.NativeSlice`1[System.UInt16]]& indicesArray, System.Collections.Generic.List`1[UnityEngine.TextCore.LowLevel.GlyphRenderMode]& renderModes, System.Collections.Generic.List`1[System.Single]& sdfScales)
UnityEngine.UIElements.ATGTextJobSystem+GenerateTextJobData.Execute (System.Int32 index)
Unity.Jobs.IJobForExtensions+ForJobStruct`1[T].Execute (T& jobData, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, Unity.Jobs.LowLevel.Unsafe.JobRanges& ranges, System.Int32 jobIndex)
```

Every subsequent occurrence:

```
IndexOutOfRangeException: Index was outside the bounds of the array.
UnityEngine.UIElements.ATGTextJobSystem.ConvertMeshInfoToUIRVertex (… same signature …)
UnityEngine.UIElements.ATGTextJobSystem+GenerateTextJobData.Execute (System.Int32 index)
Unity.Jobs.IJobForExtensions+ForJobStruct`1[T].Execute (…)
Unity.Jobs.JobHandle:ScheduleBatchedJobsAndComplete(JobHandle&)
Unity.Jobs.JobHandle:Complete()
UnityEngine.GUIUtility:ProcessEvent(Int32, IntPtr, Boolean&)
```

## Suspected cause

`ConvertMeshInfoToUIRVertex` keeps per-mesh bookkeeping in `textElementIndicesByMesh`, `hasMultipleColorsByMesh` and `atlases`. A text element produces one mesh per font atlas, and an emoji resolved through OS font fallback introduces an atlas beyond the ones those lists were sized for — so the per-mesh index runs off the end. The `List` overload throws first because it is the bounds-checked one; later regenerations trip the raw array/`Span` paths.

This also explains the state dependence: once the glyph is resident, the element regenerates cleanly, so the bug looks intermittent unless the atlas is cleared.

## Ruled out (measured)

| Hypothesis | Test | Result |
|---|---|---|
| Text length | Label with 17,261 plain ASCII characters | clean |
| Rich-text tag volume | 5,000 characters with 600 `<b>` pairs | clean |
| A specific character | Each distinct non-ASCII character in the document, 100 copies each | clean |
| Text selection | Same regeneration with `selection.isSelectable = false` on every label | still throws |
| Caller-applied styles | Same regeneration with no style writes at all | still throws |
| Element count | 1, 2, 4 … 120 elements regenerated in one repaint | clean; a single offending element is enough |

## Workarounds attempted, and why they fail

- **Splitting the text into smaller elements** — no help. The crashing unit is a single 40-character block.
- **Pre-warming glyphs with `FontAsset.TryAddCharacters`** — returns `false` for all 66 loaded font assets. The emoji come from OS-level fallback, not from any Unity `FontAsset`, so there is nothing to warm.
- **Opting the element out of the Advanced Text Generator** — the only switch found is `UITKTextHandle.useAdvancedText`, which is internal. Packages distributed on the Asset Store may not reflect into Editor internals, so this is not available to us.

The only mitigation available to a package author is to remove emoji from any text it renders — which is what the reporting project had to do to its own documentation.

## Impact

Any UI Toolkit editor extension that renders user-supplied text (a Markdown viewer, a log viewer, a chat/notes panel) floods the console when the text contains an emoji and the user drags a selection over it. The exception originates in engine code, inside a job, so it cannot be caught or suppressed by the package.
