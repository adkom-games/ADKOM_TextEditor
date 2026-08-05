#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// Spell-check dictionary service. The bundled English list is
    /// SCOWL-derived (permissive license — see SpellCheckData~/SCOWL-Copyright
    /// and THIRD-PARTY-NOTICES); on top of it load, when present:
    ///  - the GLOBAL user dictionary  (%APPDATA%/ADKOM/TextEditor/UserDictionary.txt),
    ///  - the PROJECT dictionary      (ProjectSettings/AteDictionary.txt — committable),
    ///  - extra dictionaries the user drops into
    ///    %APPDATA%/ADKOM/TextEditor/Dictionaries/ (*.txt one word per line,
    ///    or Hunspell *.dic — first count line skipped, /flags stripped).
    /// Lookup is case-insensitive. Loading happens once on a background
    /// thread; until it finishes, checks report every word as known (no
    /// false squiggles during startup).
    /// </summary>
    internal static class SpellChecker
    {
        static readonly object _lock = new object();
        static HashSet<string> _words;         // null until loaded
        static bool _loading;
        static string _bundledPath;            // resolved on the main thread

        public static string GlobalUserDictPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "UserDictionary.txt");

        public static string ProjectDictPath =>
            Path.GetFullPath(Path.Combine("ProjectSettings", "AteDictionary.txt"));

        public static string ExtraDictFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "Dictionaries");

        [UnityEditor.InitializeOnLoadMethod]
        static void ResolveBundledPath()
        {
            try
            {
                // By assembly, never by package name (the store build ships
                // under com.adkomgames.text-editor).
                var p = AtePackage.Info;
                if (p != null)
                    _bundledPath = Path.Combine(p.resolvedPath, "Editor", "SpellCheckData~", "words-en.txt");
            }
            catch (Exception) { }
        }

        public static bool Loaded { get { lock (_lock) return _words != null; } }

        /// <summary>Kicks the background load once; safe from any thread.</summary>
        public static void EnsureLoading()
        {
            lock (_lock)
            {
                if (_words != null || _loading) return;
                _loading = true;
            }
            System.Threading.Tasks.Task.Run(() =>
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                void AddFile(string path, bool hunspell)
                {
                    try
                    {
                        if (!File.Exists(path)) return;
                        bool first = true;
                        foreach (var raw in File.ReadLines(path))
                        {
                            string line = raw.Trim();
                            if (hunspell && first) { first = false; if (int.TryParse(line, out _)) continue; }
                            first = false;
                            if (line.Length == 0) continue;
                            int slash = line.IndexOf('/');
                            if (slash > 0) line = line.Substring(0, slash);
                            set.Add(line.ToLowerInvariant());
                        }
                    }
                    catch (Exception) { }
                }
                AddFile(_bundledPath, hunspell: false);
                AddFile(GlobalUserDictPath, hunspell: false);
                AddFile(ProjectDictPath, hunspell: false);
                try
                {
                    if (Directory.Exists(ExtraDictFolder))
                        foreach (var f in Directory.GetFiles(ExtraDictFolder))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".txt") AddFile(f, hunspell: false);
                            else if (ext == ".dic") AddFile(f, hunspell: true);
                        }
                }
                catch (Exception) { }
                lock (_lock)
                {
                    _words = set;
                    _loading = false;
                }
            });
        }

        /// <summary>Case-insensitive membership. True while the dictionary is
        /// still loading (never flag words before we can actually judge).</summary>
        public static bool IsKnown(string word)
        {
            HashSet<string> words;
            lock (_lock) words = _words;
            if (words == null || words.Count == 0) return true;
            return words.Contains(word.ToLowerInvariant());
        }

        /// <summary>Adds a word to the global or project dictionary (file +
        /// live set), so it stops being flagged everywhere immediately.</summary>
        public static void Add(string word, bool project)
        {
            if (string.IsNullOrEmpty(word)) return;
            string path = project ? ProjectDictPath : GlobalUserDictPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, word + "\n");
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not update the dictionary: " + ex.Message);
                return;
            }
            lock (_lock) _words?.Add(word.ToLowerInvariant());
        }
    }
}
#endif
