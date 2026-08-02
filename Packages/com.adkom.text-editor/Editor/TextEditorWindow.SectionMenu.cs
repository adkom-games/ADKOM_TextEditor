#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    // Section menu: Classes / Properties / Methods / Bookmarks submenus for
    // the ACTIVE tab, rebuilt on every click (the menu bar builds its
    // GenericMenu on demand) — so tab switches and bookmark edits are always
    // reflected. Selecting an item repositions the caret and view. The
    // symbol scan is a lightweight regex heuristic over the live buffer —
    // fast, works without Semantic Features, and deliberately tolerant: a
    // rare miss beats a per-click Roslyn query.
    public partial class TextEditorWindow
    {
        // "class Foo" (also struct/interface/enum/record), not after //.
        static readonly Regex SectionTypeRx = new Regex(
            @"(?m)^[ \t]*[^/\r\n]*?\b(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)",
            RegexOptions.Compiled);

        // "type Name(" at line start with only type-ish tokens before the
        // name — statement calls ("Foo(x);", "_a.B(x);") don't match because
        // they lack a preceding type token or contain '.'/'='.
        static readonly Regex SectionMethodRx = new Regex(
            @"(?m)^[ \t]*(?!(?:if|for|foreach|while|switch|catch|using|lock|return|else|do|new|throw|get|set|add|remove)\b)((?:[A-Za-z_]\w*(?:<[^>\r\n]*>)?[\[\]\?]*[ \t]+)+)([A-Za-z_]\w*)[ \t]*(?:<[^>\r\n]*>)?[ \t]*\(",
            RegexOptions.Compiled);

        // "type Name {" / "type Name =>" — the '{' variant is confirmed as a
        // property by a get/set lookahead in code.
        static readonly Regex SectionPropertyRx = new Regex(
            @"(?m)^[ \t]*(?!(?:if|for|foreach|while|switch|catch|using|lock|return|else|do|new|throw)\b)((?:[A-Za-z_]\w*(?:<[^>\r\n]*>)?[\[\]\?]*[ \t]+)+)([A-Za-z_]\w*)[ \t]*(\{|=>)",
            RegexOptions.Compiled);

        void FillSectionMenu(GenericMenu m)
        {
            var classes = new List<(string name, int offset)>();
            var props = new List<(string name, int offset)>();
            var methods = new List<(string name, int offset)>();
            if (CanEditDoc)
            {
                string content = _code.value ?? string.Empty;
                foreach (Match t in SectionTypeRx.Matches(content))
                    classes.Add((t.Groups[1].Value, t.Groups[1].Index));
                foreach (Match t in SectionMethodRx.Matches(content))
                    methods.Add((t.Groups[2].Value, t.Groups[2].Index));
                foreach (Match t in SectionPropertyRx.Matches(content))
                {
                    // '{' needs a get/set nearby to count as a property
                    // (expression-bodied "=>" always does).
                    if (t.Groups[3].Value == "{")
                    {
                        int brace = t.Groups[3].Index;
                        int end = Mathf.Min(content.Length, brace + 200);
                        string peek = content.Substring(brace, end - brace);
                        if (!Regex.IsMatch(peek, @"\b(?:get|set)\b")) continue;
                    }
                    props.Add((t.Groups[2].Value, t.Groups[2].Index));
                }
            }
            AddSectionGroup(m, L10n.Tr("Classes"), classes);
            AddSectionGroup(m, L10n.Tr("Properties"), props);
            AddSectionGroup(m, L10n.Tr("Methods"), methods);
            AddBookmarksGroup(m);
        }

        // "#pragma bookmark <label>": a bookmark declared in the source
        // itself. Matched per line; the label is the rest of the line.
        static readonly Regex PragmaBookmarkRx = new Regex(
            @"^[ \t]*#pragma[ \t]+bookmark[ \t]+(\S.*?)[ \t]*$",
            RegexOptions.Compiled);

        /// <summary>Section > Bookmarks: the ACTIVE document's bookmarks —
        /// toggled ones (labeled "line:  text preview") merged with
        /// "#pragma bookmark <label>" lines parsed from the source (labeled
        /// with their label; it wins when a line is both) — sorted by line.
        /// Selecting one jumps to (and centers) the line.</summary>
        void AddBookmarksGroup(GenericMenu m)
        {
            string title = L10n.Tr("Bookmarks");
            var marks = new SortedDictionary<int, string>();
            string[] lines = CanEditDoc ? (_code.value ?? string.Empty).Split('\n') : null;
            if (lines != null && Active.Bookmarks != null)
            {
                foreach (int l in Active.Bookmarks)
                {
                    if (l < 0 || l >= lines.Length) continue;
                    string text = lines[l].TrimEnd('\r').Trim();
                    if (text.Length > 60) text = text.Substring(0, 60) + "…";
                    marks[l] = text;
                }
            }
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var pm = PragmaBookmarkRx.Match(lines[i].TrimEnd('\r'));
                    if (pm.Success) marks[i] = pm.Groups[1].Value;
                }
            }
            if (marks.Count == 0)
            {
                m.AddDisabledItem(new GUIContent(title + "/" + L10n.Tr("(none)")));
                return;
            }
            foreach (var mark in marks)
            {
                string label = (mark.Key + 1) +
                    (mark.Value.Length > 0 ? ":  " + mark.Value.Replace('/', '∕') : "");
                int line = mark.Key;
                m.AddItem(new GUIContent(title + "/" + label), false, () =>
                {
                    if (!CanEditDoc) return;
                    PushNavLocation();
                    _code.GoToLine(line + 1, 1);
                });
            }
        }

        /// <summary>One submenu of symbols, sorted by name (ties in document
        /// order); a disabled "(none)" entry when the group is empty.</summary>
        void AddSectionGroup(GenericMenu m, string title, List<(string name, int offset)> items)
        {
            if (items.Count == 0)
            {
                m.AddDisabledItem(new GUIContent(title + "/" + L10n.Tr("(none)")));
                return;
            }
            items.Sort((a, b) =>
            {
                int byName = string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.offset.CompareTo(b.offset);
            });
            foreach (var (name, offset) in items)
            {
                int o = offset;
                m.AddItem(new GUIContent(title + "/" + name.Replace('/', '∕')), false, () =>
                {
                    if (!CanEditDoc) return;
                    PushNavLocation();
                    _code.IndexToLineCol(Mathf.Min(o, (_code.value ?? "").Length), out int line, out int col);
                    _code.GoToLine(line + 1, col + 1);
                });
            }
        }
    }
}
#endif
