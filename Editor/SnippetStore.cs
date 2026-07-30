#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Customizable code snippets. Definitions live in one PLAIN-TEXT file the
    /// user edits in ATE itself (machine-shared, next to the addons folder):
    ///
    ///   [trigger]
    ///   body lines...
    ///
    /// A line matching [name] starts a snippet; everything until the next such
    /// line is its body (surrounding blank lines trimmed). $name$ marks a tab
    /// stop (its name is the placeholder's default text), $END$ the final
    /// caret. A default C# set is written on first use; the file reloads
    /// automatically when its timestamp changes.
    /// </summary>
    internal static class SnippetStore
    {
        internal struct Snippet
        {
            public string Trigger;
            public string Body;   // raw body, placeholders included
        }

        public static string SnippetsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "Snippets.txt");

        static List<Snippet> _snippets;
        static DateTime _loadedStamp;
        static double _lastStatTime;

        public static IReadOnlyList<Snippet> All
        {
            get
            {
                // Cheap freshness check, throttled to every 2 seconds.
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (_snippets == null || now - _lastStatTime > 2.0)
                {
                    _lastStatTime = now;
                    try
                    {
                        EnsureDefaultFile();
                        var stamp = File.GetLastWriteTimeUtc(SnippetsPath);
                        if (_snippets == null || stamp != _loadedStamp)
                        {
                            _snippets = Parse(File.ReadAllText(SnippetsPath));
                            _loadedStamp = stamp;
                        }
                    }
                    catch (Exception) { _snippets ??= new List<Snippet>(); }
                }
                return _snippets;
            }
        }

        public static bool TryGet(string trigger, out Snippet snippet)
        {
            foreach (var s in All)
                if (string.Equals(s.Trigger, trigger, StringComparison.Ordinal))
                { snippet = s; return true; }
            snippet = default;
            return false;
        }

        internal static List<Snippet> Parse(string text)
        {
            var list = new List<Snippet>();
            string trigger = null;
            var body = new List<string>();
            void Flush()
            {
                if (trigger == null) return;
                while (body.Count > 0 && body[0].Trim().Length == 0) body.RemoveAt(0);
                while (body.Count > 0 && body[body.Count - 1].Trim().Length == 0) body.RemoveAt(body.Count - 1);
                if (body.Count > 0)
                    list.Add(new Snippet { Trigger = trigger, Body = string.Join("\n", body) });
                body.Clear();
            }
            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw;
                string t = line.Trim();
                if (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']' && IsTrigger(t.Substring(1, t.Length - 2)))
                {
                    Flush();
                    trigger = t.Substring(1, t.Length - 2);
                    continue;
                }
                if (trigger == null) continue; // preamble / comments
                body.Add(line);
            }
            Flush();
            return list;
        }

        static bool IsTrigger(string s)
        {
            if (s.Length == 0 || s.Length > 32) return false;
            foreach (char c in s)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            return true;
        }

        static void EnsureDefaultFile()
        {
            if (File.Exists(SnippetsPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(SnippetsPath));
            File.WriteAllText(SnippetsPath,
@"ADKOM Text Editor snippets.
A line like [name] starts a snippet; the lines after it are the body.
$placeholder$ marks a tab stop (Tab jumps between them), $END$ the final
caret position. Type a trigger and press Tab, or pick it from completion.

[prop]
public $Type$ $Name$ { get; set; }$END$

[field]
private $Type$ _$name$;$END$

[for]
for (int $i$ = 0; $i$ < $count$; $i$++)
{
    $END$
}

[foreach]
foreach (var $item$ in $collection$)
{
    $END$
}

[if]
if ($condition$)
{
    $END$
}

[ifelse]
if ($condition$)
{
    $END$
}
else
{
}

[while]
while ($condition$)
{
    $END$
}

[switch]
switch ($value$)
{
    case $case$:
        $END$
        break;
    default:
        break;
}

[try]
try
{
    $END$
}
catch (System.Exception ex)
{
}

[class]
public class $Name$
{
    $END$
}

[method]
public $void$ $Name$($args$)
{
    $END$
}

[dbg]
Debug.Log($message$);$END$
");
        }
    }
}
#endif
