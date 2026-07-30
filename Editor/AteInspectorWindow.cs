#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Reflection inspector for the type under the caret: static fields and
    /// properties with LIVE values (polled — play-mode state included),
    /// parameterless static methods with Run buttons (results go to the ATE
    /// console), and — for Component/MonoBehaviour types — a scene-instance
    /// picker with the instance's fields and properties live as well.
    /// Writable primitive/string/enum values are editable in place. Survives
    /// domain reloads by re-resolving the type from its stored name.
    /// </summary>
    public class AteInspectorWindow : EditorWindow
    {
        static AteInspectorWindow _instance;

        [SerializeField] string _metadataName;
        [SerializeField] string _assemblyName;
        [SerializeField] string _word;

        Type _type;
        UnityEngine.Object _target;           // selected instance (null = none)
        Label _header, _status;
        ScrollView _scroll;
        PopupField<string> _instancePicker;
        List<UnityEngine.Object> _instances = new List<UnityEngine.Object>();

        sealed class Row
        {
            public MemberInfo Member;
            public bool Static;
            public Label ValueLabel;   // read-only display
            public TextField Edit;     // editable display (delayed)
        }
        readonly List<Row> _rows = new List<Row>();

        public static void Open(string metadataName, string assemblyName, string word)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<AteInspectorWindow>();
                _instance.titleContent = new GUIContent(L10n.Tr("Inspect Type"));
                _instance.minSize = new Vector2(430, 300);
                _instance.ShowUtility();
            }
            _instance._metadataName = metadataName;
            _instance._assemblyName = assemblyName;
            _instance._word = word;
            _instance._type = null;
            _instance._target = null;
            _instance.BuildUI();
            _instance.Focus();
        }

        void OnEnable()
        {
            if (_instance == null) _instance = this;
            if (!string.IsNullOrEmpty(_metadataName) || !string.IsNullOrEmpty(_word))
                rootVisualElement.schedule.Execute(BuildUI); // reload survivor
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        static Type Resolve(string metadataName, string assemblyName, string word)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            if (!string.IsNullOrEmpty(metadataName))
            {
                foreach (var a in asms)
                    if (a.GetName().Name == assemblyName)
                    { var t = a.GetType(metadataName); if (t != null) return t; }
                foreach (var a in asms)
                { var t = a.GetType(metadataName); if (t != null) return t; }
            }
            if (!string.IsNullOrEmpty(word))
                foreach (var a in asms)
                {
                    Type[] types;
                    try { types = a.GetTypes(); } catch (Exception) { continue; }
                    var t = types.FirstOrDefault(x => x.Name == word);
                    if (t != null) return t;
                }
            return null;
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _rows.Clear();
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            _type = Resolve(_metadataName, _assemblyName, _word);
            _header = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal } };
            root.Add(_header);
            _status = new Label { style = { opacity = 0.75f, marginBottom = 4 } };
            root.Add(_status);
            if (_type == null)
            {
                _header.text = string.Format(L10n.Tr("Type not found: {0}"),
                    _metadataName ?? _word ?? "?");
                return;
            }
            _header.text = _type.FullName + "   (" + _type.Assembly.GetName().Name + ")";

            _scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(_scroll);

            Section(L10n.Tr("Static Fields & Properties"));
            int added = AddMemberRows(_type, isStatic: true, target: null);
            if (added == 0) Note(L10n.Tr("(none)"));

            Section(L10n.Tr("Static Methods (parameterless)"));
            int methods = 0;
            foreach (var m in StaticMethods(_type))
            {
                var mi = m;
                var row = RowBox();
                var run = new Button(() => RunStatic(mi)) { text = L10n.Tr("Run") };
                row.Add(run);
                row.Add(new Label(mi.Name + "()") { style = { unityTextAlign = TextAnchor.MiddleLeft } });
                _scroll.Add(row);
                methods++;
            }
            if (methods == 0) Note(L10n.Tr("(none)"));

            if (typeof(Component).IsAssignableFrom(_type))
            {
                Section(L10n.Tr("Scene Instances"));
                var pickRow = RowBox();
                _instancePicker = null;
                var refresh = new Button(() => { RefreshInstances(); BuildUI(); }) { text = L10n.Tr("Refresh") };
                RefreshInstances();
                if (_instances.Count == 0)
                {
                    pickRow.Add(refresh);
                    pickRow.Add(new Label(L10n.Tr("(no instances in the loaded scenes)")) { style = { opacity = 0.7f } });
                    _scroll.Add(pickRow);
                }
                else
                {
                    var names = _instances.Select((o, i) => i + ": " + (o != null ? o.name : "?")).ToList();
                    if (_target == null || !_instances.Contains(_target)) _target = _instances[0];
                    _instancePicker = new PopupField<string>(names, Mathf.Max(0, _instances.IndexOf(_target)));
                    _instancePicker.RegisterValueChangedCallback(_ =>
                    {
                        int idx = names.IndexOf(_instancePicker.value);
                        _target = idx >= 0 ? _instances[idx] : null;
                        BuildUI();
                    });
                    pickRow.Add(_instancePicker);
                    pickRow.Add(refresh);
                    _scroll.Add(pickRow);
                    if (_target != null)
                        if (AddMemberRows(_type, isStatic: false, target: _target) == 0)
                            Note(L10n.Tr("(none)"));
                }
            }

            root.schedule.Execute(PollValues).Every(500);
        }

        void Section(string title) => _scroll.Add(new Label(title)
        { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 2 } });

        void Note(string text) => _scroll.Add(new Label(text) { style = { opacity = 0.6f, paddingLeft = 6 } });

        static VisualElement RowBox() => new VisualElement
        { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 1 } };

        // ---- Members ----

        static IEnumerable<MemberInfo> Members(Type t, bool isStatic)
        {
            var flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance)
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var seen = new HashSet<string>();
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                foreach (var f in cur.GetFields(flags))
                    if (!f.Name.StartsWith("<", StringComparison.Ordinal) && seen.Add(f.Name))
                        yield return f;
                foreach (var p in cur.GetProperties(flags))
                    if (p.GetIndexParameters().Length == 0 && p.GetMethod != null && seen.Add(p.Name))
                        yield return p;
                if (seen.Count > 200) yield break;
            }
        }

        static IEnumerable<MethodInfo> StaticMethods(Type t)
        {
            var seen = new HashSet<string>();
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var m in cur.GetMethods(BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!m.IsSpecialName && m.GetParameters().Length == 0 &&
                        !m.Name.StartsWith("<", StringComparison.Ordinal) &&
                        !m.ContainsGenericParameters && seen.Add(m.Name))
                        yield return m;
        }

        int AddMemberRows(Type t, bool isStatic, object target)
        {
            int added = 0;
            foreach (var member in Members(t, isStatic))
            {
                var row = RowBox();
                row.Add(new Label(member.Name)
                { style = { width = 180, overflow = Overflow.Hidden, unityTextAlign = TextAnchor.MiddleLeft } });
                var r = new Row { Member = member, Static = isStatic };
                if (IsEditable(member))
                {
                    r.Edit = new TextField { isDelayed = true, style = { flexGrow = 1 } };
                    r.Edit.RegisterValueChangedCallback(e => TrySet(member, isStatic ? null : _target, e.newValue));
                    row.Add(r.Edit);
                }
                else
                {
                    r.ValueLabel = new Label
                    { style = { flexGrow = 1, overflow = Overflow.Hidden, unityTextAlign = TextAnchor.MiddleLeft, opacity = 0.9f } };
                    row.Add(r.ValueLabel);
                }
                _rows.Add(r);
                _scroll.Add(row);
                added++;
            }
            return added;
        }

        static bool IsEditable(MemberInfo m)
        {
            Type vt = m is FieldInfo f ? f.FieldType : (m as PropertyInfo)?.PropertyType;
            bool writable = m is FieldInfo fi ? !fi.IsInitOnly && !fi.IsLiteral
                : (m as PropertyInfo)?.SetMethod != null;
            if (!writable || vt == null) return false;
            return vt == typeof(string) || vt == typeof(int) || vt == typeof(float) ||
                   vt == typeof(double) || vt == typeof(bool) || vt == typeof(long) || vt.IsEnum;
        }

        static object GetValue(MemberInfo m, object target) =>
            m is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)m).GetValue(target);

        void TrySet(MemberInfo m, object target, string text)
        {
            try
            {
                Type vt = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
                object v =
                    vt == typeof(string) ? text :
                    vt == typeof(int) ? (object)int.Parse(text) :
                    vt == typeof(float) ? (object)float.Parse(text) :
                    vt == typeof(double) ? (object)double.Parse(text) :
                    vt == typeof(bool) ? (object)bool.Parse(text) :
                    vt == typeof(long) ? (object)long.Parse(text) :
                    vt.IsEnum ? Enum.Parse(vt, text, ignoreCase: true) : null;
                if (m is FieldInfo fi) fi.SetValue(target, v);
                else ((PropertyInfo)m).SetValue(target, v);
                _status.text = "";
            }
            catch (Exception ex)
            {
                _status.text = string.Format(L10n.Tr("{0} threw: {1}"), m.Name,
                    (ex.InnerException ?? ex).Message);
            }
        }

        void PollValues()
        {
            if (_type == null) return;
            foreach (var r in _rows)
            {
                object target = r.Static ? null : _target as object;
                if (!r.Static && _target == null) continue;
                string text;
                try
                {
                    object v = GetValue(r.Member, target);
                    text = v == null ? "null"
                        : v is string s ? "\"" + s + "\""
                        : v.ToString();
                    if (text.Length > 160) text = text.Substring(0, 157) + "...";
                }
                catch (Exception ex) { text = "(" + (ex.InnerException ?? ex).GetType().Name + ")"; }
                if (r.ValueLabel != null) r.ValueLabel.text = text;
                else if (r.Edit != null &&
                         r.Edit.focusController?.focusedElement != r.Edit)
                    r.Edit.SetValueWithoutNotify(text.Trim('"'));
            }
        }

        void RefreshInstances()
        {
            _instances = UnityEngine.Object
                .FindObjectsByType(_type, FindObjectsInactive.Include, FindObjectsSortMode.None)
                .ToList();
        }

        void RunStatic(MethodInfo m)
        {
            try
            {
                object result = m.Invoke(null, null);
                string shown = m.ReturnType == typeof(void) ? "void"
                    : result == null ? "null" : result.ToString();
                string line = string.Format(L10n.Tr("Ran {0}.{1}() → {2}"), _type.Name, m.Name, shown);
                _status.text = line;
                AteConsole.Info("[ADKOM Text Editor] " + line);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                _status.text = string.Format(L10n.Tr("{0} threw: {1}"), m.Name, inner.Message);
                AteConsole.Warn("[ADKOM Text Editor] " + _type.Name + "." + m.Name + "(): " + inner);
            }
        }
    }
}
#endif
