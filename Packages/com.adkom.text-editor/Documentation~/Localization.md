# Localization

ATE's entire interface follows Unity's **Editor Language** setting (Preferences → Languages). English is the source language; **Japanese, Korean, Simplified Chinese, and Traditional Chinese** ship in the box. Menus, settings, dialogs, prompts, and status messages all translate; keyboard shortcuts stay universal.

## How it works

Every user-visible string in the code goes through `L10n.Tr("...")`, and an assembly-level `[assembly: UnityEditor.Localization]` attribute makes Unity load the translation catalogs from the package's `Editor/Localization/` folder for the language selected in Preferences. The catalogs are plain gettext **PO files**:

```
msgid "Open Manual"
msgstr "マニュアルを開く"
```

- `en.po` is an identity catalog (English → English). It exists deliberately: Unity's catalog loader falls back to the first PO file alphabetically when the current language has no catalog, which once put the whole UI in Japanese on English editors — the English catalog pins the fallback.
- Pluralization is handled as separate strings (proper singular/plural message pairs), not PO plural forms.
- Format strings keep their `{0}`-style placeholders in the translation — reorder them freely, but keep them present.

## Adding or improving a language

1. Copy `Editor/Localization/en.po` to `<code>.po` for your language (Unity's language codes, e.g. `ja`, `ko`, `zh-hans`, `zh-hant`).
2. Set the header's `"Language: <code>\n"` line, keep `charset=utf-8`.
3. Translate the `msgstr` lines. Untranslated entries fall back to the English `msgid`.
4. Restart the editor (catalogs load with the assembly) and switch Unity's Editor Language to test.

Note that Unity only offers the languages it supports in Preferences → Languages — a catalog for a language Unity cannot switch to will never be selected.

## Contributing

Corrections and new catalogs are welcome — open a pull request or an issue on the [repository](https://github.com/adkom-games/ADKOM_TextEditor). Every new user-facing string added to ATE lands in all five catalogs in the same change, so a catalog is always complete for its release.
