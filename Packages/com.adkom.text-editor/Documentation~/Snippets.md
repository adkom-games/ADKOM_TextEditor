# Snippets Reference

Snippets are your own text templates: type a trigger, press **Tab** (or pick it from the completion popup), and the body expands re-indented to where you are, with placeholders you jump through.

## The snippets file

All definitions live in ONE plain-text file you edit in ATE itself: **Tools → Edit Snippets…** opens it (stored machine-shared at `%APPDATA%/ADKOM/TextEditor/Snippets.txt`, next to the addons folder). Save the file and the set **hot-reloads** — no restart, your next Tab uses the new definitions. A default set of 12 C# snippets is written on first use.

## Format

```
[trigger]
body line
body line...
```

- A line like `[name]` **starts a snippet**; everything until the next such line is its body. Blank lines around the body are trimmed.
- **Triggers** are 1–32 characters: letters, digits, `_`, `-`. Matching is exact and case-sensitive.
- Text before the first trigger line is ignored — the top of the file is free space for your own notes.
- `$name$` marks a **tab stop**; the name doubles as the placeholder's default text. **Tab / Shift+Tab** cycle forward and back through the stops.
- `$END$` marks where the caret lands when you leave the snippet.

## Example

```
[foreach]
foreach (var $item$ in $collection$)
{
    $END$
}
```

Typing `foreach` and pressing Tab expands the block at your indentation, selects `item` first, Tab moves to `collection`, and a final Tab puts the caret on the empty line inside the braces.

## Shipped defaults

`prop`, `field`, `for`, `foreach`, `if`, `ifelse`, `while`, `switch`, `try`, `class`, `method`, `dbg` — all editable and removable; they are just entries in the same file.
