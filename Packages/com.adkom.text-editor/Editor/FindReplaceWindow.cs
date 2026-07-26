#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Modeless Find/Replace dialog for the ADKOM Text Editor. Supports match
    /// case, whole word, normal or regex search, wrap around, backwards
    /// direction, and a scope of the current tab or all open tabs. Search
    /// state persists for the session (F3 / Shift+F3 repeat from the editor).
    /// </summary>
    public class FindReplaceWindow : EditorWindow
    {
        static FindReplaceWindow _instance;

        // Session-persistent state.
        static string _sFind = string.Empty, _sReplace = string.Empty;
        static bool _sCase, _sWord, _sRegex, _sBack;
        static bool _sWrap = true;
        static bool _sAllTabs;

        TextEditorWindow _owner;
        TextField _find, _replace;
        Toggle _case, _word, _regex, _wrap, _back;
        PopupField<string> _scope;
        Label _status;

        public static void Open(TextEditorWindow owner, bool replaceFocus, bool allTabs)
        {
            _sAllTabs = allTabs;
            bool creating = _instance == null;
            var w = _instance != null ? _instance : (_instance = CreateInstance<FindReplaceWindow>());
            w._owner = owner;
            w.titleContent = new GUIContent("Find / Replace");
            if (creating)
            {
                w.minSize = new Vector2(360, 200);
                w.ShowUtility(); // floating, modeless
            }
            w.Focus();
            w.SyncFromState();
            var target = replaceFocus ? w._replace : w._find;
            target?.schedule.Execute(() => { target.Focus(); target.SelectAll(); }).ExecuteLater(50);
        }

        /// <summary>F3 / Shift+F3 support: repeats the last search if any.</summary>
        public static bool FindAgain(TextEditorWindow owner, bool reverse)
        {
            if (string.IsNullOrEmpty(_sFind)) return false;
            if (_instance == null)
            {
                var probe = new FindReplaceWindow { _owner = owner };
                probe.FindNextCore(reverse);
                DestroyImmediate(probe);
                return true;
            }
            _instance._owner = owner;
            _instance.FindNextCore(reverse);
            return true;
        }

        void OnDestroy() { SaveState(); _instance = null; }

        void SyncFromState()
        {
            _find?.SetValueWithoutNotify(_sFind);
            _replace?.SetValueWithoutNotify(_sReplace);
            _case?.SetValueWithoutNotify(_sCase);
            _word?.SetValueWithoutNotify(_sWord);
            _regex?.SetValueWithoutNotify(_sRegex);
            _wrap?.SetValueWithoutNotify(_sWrap);
            _back?.SetValueWithoutNotify(_sBack);
            _scope?.SetValueWithoutNotify(_sAllTabs ? "All Tabs" : "Current Tab");
        }

        void SaveState()
        {
            if (_find == null) return;
            _sFind = _find.value; _sReplace = _replace.value;
            _sCase = _case.value; _sWord = _word.value; _sRegex = _regex.value;
            _sWrap = _wrap.value; _sBack = _back.value;
            _sAllTabs = _scope.value == "All Tabs";
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;

            _find = new TextField("Find");
            _replace = new TextField("Replace");
            root.Add(_find);
            root.Add(_replace);

            var opts = new VisualElement();
            opts.style.flexDirection = FlexDirection.Row;
            opts.style.flexWrap = Wrap.Wrap;
            _case = MakeOpt(opts, "Match case", _sCase);
            _word = MakeOpt(opts, "Whole word", _sWord);
            _regex = MakeOpt(opts, "Regex", _sRegex);
            _wrap = MakeOpt(opts, "Wrap around", _sWrap);
            _back = MakeOpt(opts, "Backwards", _sBack);
            root.Add(opts);

            _scope = new PopupField<string>("Scope",
                new System.Collections.Generic.List<string> { "Current Tab", "All Tabs" },
                _sAllTabs ? "All Tabs" : "Current Tab");
            root.Add(_scope);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            buttons.Add(new Button(() => { SaveState(); FindNextCore(false); }) { text = "Find Next" });
            buttons.Add(new Button(() => { SaveState(); ReplaceOnce(); }) { text = "Replace" });
            buttons.Add(new Button(() => { SaveState(); ReplaceAll(); }) { text = "Replace All" });
            root.Add(buttons);

            _status = new Label();
            _status.style.marginTop = 4;
            _status.style.opacity = 0.75f;
            root.Add(_status);

            // Enter in the Find field = Find Next.
            _find.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return) { SaveState(); FindNextCore(false); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);

            SyncFromState();
        }

        static Toggle MakeOpt(VisualElement parent, string label, bool value)
        {
            var t = new Toggle(label) { value = value };
            t.style.marginRight = 10;
            parent.Add(t);
            return t;
        }

        // ---------- Search core ----------

        bool OwnerOk()
        {
            if (_owner == null)
                _owner = Resources.FindObjectsOfTypeAll<TextEditorWindow>() is var w && w.Length > 0 ? w[0] : null;
            if (_owner == null) { SetStatus("No editor window."); return false; }
            if (_owner.DocCount == 0) { SetStatus("No open tabs."); return false; }
            return true;
        }

        void SetStatus(string s) { if (_status != null) _status.text = s; AteConsole.Log("Find/Replace: " + s); }

        Regex BuildRegex(bool rightToLeft, out string error)
        {
            error = null;
            string pat = _sRegex ? _sFind : Regex.Escape(_sFind);
            if (_sWord) pat = @"\b(?:" + pat + @")\b";
            var o = RegexOptions.None;
            if (!_sCase) o |= RegexOptions.IgnoreCase;
            if (rightToLeft) o |= RegexOptions.RightToLeft;
            try { return new Regex(pat, o); }
            catch (System.ArgumentException ex) { error = "Invalid regex: " + ex.Message; return null; }
        }

        string ReplacementFor(Match m) => _sRegex ? m.Result(_sReplace) : _sReplace;

        /// <summary>Next searchable doc index in direction, skipping Settings.</summary>
        int NextDoc(int from, int dir)
        {
            int n = _owner.DocCount;
            for (int k = 1; k <= n; k++)
            {
                int i = ((from + dir * k) % n + n) % n;
                if (!_owner.IsSettingsTab(i)) return i;
            }
            return from;
        }

        public void FindNextCore(bool reverseOnce)
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            bool back = _sBack ^ reverseOnce;
            var rx = BuildRegex(back, out string err);
            if (rx == null) { SetStatus(err); return; }

            int active = _owner.ActiveIndex;
            bool allTabs = _sAllTabs;
            _owner.GetSelectionSpan(out int selS, out int selE);

            // 1. From the caret within the active tab.
            string content = _owner.GetDocContent(active);
            int from = back ? selS : selE;
            var m = back ? rx.Match(content, 0, Mathf.Clamp(from, 0, content.Length))
                         : rx.Match(content, Mathf.Clamp(from, 0, content.Length));
            if (m.Success) { Select(active, m); return; }

            // 2. Other tabs, in direction order.
            if (allTabs)
            {
                int i = active;
                for (int k = 1; k < _owner.DocCount; k++)
                {
                    i = NextDoc(i, back ? -1 : 1);
                    if (i == active) break;
                    string c = _owner.GetDocContent(i);
                    var mm = back ? rx.Match(c, 0, c.Length) : rx.Match(c, 0);
                    if (mm.Success) { Select(i, mm); return; }
                }
            }

            // 3. Wrap around within the starting tab.
            if (_sWrap)
            {
                var mw = back ? rx.Match(content, 0, content.Length) : rx.Match(content, 0);
                if (mw.Success) { Select(active, mw); SetStatus("Wrapped."); return; }
            }
            SetStatus("Not found: " + _sFind);
        }

        void Select(int doc, Match m)
        {
            if (doc != _owner.ActiveIndex) _owner.SwitchTab(doc);
            _owner.SelectSpan(m.Index, m.Index + m.Length);
            SetStatus(_owner.GetDocName(doc) + "  " + (m.Index + 1));
        }

        void ReplaceOnce()
        {
            if (!OwnerOk()) return;
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }
            _owner.GetSelectionSpan(out int s, out int e);
            if (e > s)
            {
                string content = _owner.GetDocContent(_owner.ActiveIndex);
                var m = rx.Match(content, s);
                if (m.Success && m.Index == s && m.Length == e - s)
                    _owner.ReplaceSpanInActive(s, e, ReplacementFor(m));
            }
            FindNextCore(false);
        }

        void ReplaceAll()
        {
            if (!OwnerOk()) return;
            if (string.IsNullOrEmpty(_sFind)) { SetStatus("Nothing to find."); return; }
            var rx = BuildRegex(false, out string err);
            if (rx == null) { SetStatus(err); return; }

            int total = 0, docsTouched = 0;
            int n = _sAllTabs ? _owner.DocCount : 1;
            for (int k = 0; k < n; k++)
            {
                int i = _sAllTabs ? k : _owner.ActiveIndex;
                if (_owner.IsSettingsTab(i)) continue;
                string content = _owner.GetDocContent(i);
                int count = rx.Matches(content).Count;
                if (count == 0) continue;
                _owner.SetDocContent(i, rx.Replace(content, ReplacementFor));
                total += count;
                docsTouched++;
            }
            SetStatus(total == 0 ? "Not found: " + _sFind
                : total + " replaced in " + docsTouched + (docsTouched == 1 ? " tab." : " tabs."));
        }
    }
}
#endif
