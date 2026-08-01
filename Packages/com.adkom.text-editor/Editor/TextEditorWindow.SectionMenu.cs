#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    // Section menu: Classes / Properties / Methods submenus listing the
    // symbols of the ACTIVE tab, rebuilt on every click (the menu bar builds
    // its GenericMenu on demand). Selecting a symbol repositions the caret
    // and view to its declaration. The scan is a lightweight regex heuristic
    // over the live buffer — fast, works without Semantic Features, and
    // deliberately tolerant: a rare miss beats a per-click Roslyn query.
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
