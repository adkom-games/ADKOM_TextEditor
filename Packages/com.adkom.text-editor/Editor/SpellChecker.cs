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

        /// <summary>Replacement candidates for a misspelling, best first.
        ///
        /// Classic edit-distance generation: build every one-edit variant of the
        /// word (delete, transpose, replace, insert) and keep the ones the
        /// dictionary knows. Only if that finds nothing do we pay for two-edit
        /// variants, because that search is roughly the square of the first and
        /// is not worth it when the user has merely fat-fingered one key.
        ///
        /// With no frequency data to rank by, ordering falls back to what
        /// actually helps a reader scanning a short menu: fewer edits first,
        /// then candidates that keep the first letter (typos rarely change it),
        /// then the shortest, then alphabetical so the list is stable between
        /// invocations rather than shuffling.</summary>
        public static List<string> Suggest(string word, int max = 6)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(word) || max <= 0)
                return result;

            HashSet<string> words;
            lock (_lock) words = _words;
            if (words == null || words.Count == 0)
                return result; // dictionary still loading — offer nothing rather than nonsense

            var lower = word.ToLowerInvariant();
            var near = new List<string>();
            var seen = new HashSet<string>();

            foreach (var candidate in Edits(lower))
                if (words.Contains(candidate) && candidate != lower && seen.Add(candidate))
                    near.Add(candidate);

            // Two-edit search costs roughly the square of the first pass, so it
            // is a fallback only, it is capped, and it is skipped for long words
            // where it is both slowest and least likely to be what was meant.
            if (near.Count == 0 && lower.Length <= 10)
            {
                var budget = max * 4;
                foreach (var once in Edits(lower))
                {
                    foreach (var twice in Edits(once))
                        if (words.Contains(twice) && twice != lower && seen.Add(twice))
                            near.Add(twice);
                    if (near.Count >= budget)
                        break;
                }
            }

            near.Sort((a, b) =>
            {
                // Everyday words win. Without frequency data the ordering was
                // alphabetical among equals, which buried the one answer that
                // is almost always right: "teh" offered tea, ted, tee, ten, tet
                // and only then "the". A short common-word list fixes exactly
                // that class of typo and costs nothing.
                var aCommon = Common.Contains(a);
                var bCommon = Common.Contains(b);
                if (aCommon != bCommon) return aCommon ? -1 : 1;
                var aFirst = a.Length > 0 && lower.Length > 0 && a[0] == lower[0];
                var bFirst = b.Length > 0 && lower.Length > 0 && b[0] == lower[0];
                if (aFirst != bFirst) return aFirst ? -1 : 1;
                var byLen = Math.Abs(a.Length - lower.Length).CompareTo(Math.Abs(b.Length - lower.Length));
                if (byLen != 0) return byLen;
                return string.CompareOrdinal(a, b);
            });

            for (var i = 0; i < near.Count && result.Count < max; i++)
                result.Add(MatchCase(word, near[i]));
            return result;
        }

        /// <summary>The most frequent English words, used only to break ties
        /// between equally-close candidates. Deliberately small: it is a nudge
        /// toward the obvious answer, not a language model.</summary>
        static readonly HashSet<string> Common = new HashSet<string>(StringComparer.Ordinal)
        {
            "the","be","to","of","and","a","in","that","have","it","for","not","on","with","he",
            "as","you","do","at","this","but","his","by","from","they","we","say","her","she","or",
            "an","will","my","one","all","would","there","their","what","so","up","out","if","about",
            "who","get","which","go","me","when","make","can","like","time","no","just","him","know",
            "take","people","into","year","your","good","some","could","them","see","other","than",
            "then","now","look","only","come","its","over","think","also","back","after","use","two",
            "how","our","work","first","well","way","even","new","want","because","any","these",
            "give","day","most","us","is","are","was","were","been","has","had","did","said","made",
            "set","get","file","line","text","code","name","type","value","data","list","string",
        };

        /// <summary>Every one-edit variant of a lowercase word.</summary>
        static IEnumerable<string> Edits(string w)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz";
            for (var i = 0; i < w.Length; i++)                       // deletions
                yield return w.Remove(i, 1);
            for (var i = 0; i < w.Length - 1; i++)                   // transpositions
                yield return w.Substring(0, i) + w[i + 1] + w[i] + w.Substring(i + 2);
            for (var i = 0; i < w.Length; i++)                       // replacements
                foreach (var c in alphabet)
                    if (c != w[i])
                        yield return w.Substring(0, i) + c + w.Substring(i + 1);
            for (var i = 0; i <= w.Length; i++)                      // insertions
                foreach (var c in alphabet)
                    yield return w.Substring(0, i) + c + w.Substring(i);
        }

        /// <summary>Give the suggestion the original word's capitalisation, so
        /// replacing "Teh" offers "The" rather than "the" at the start of a
        /// sentence.</summary>
        static string MatchCase(string original, string suggestion)
        {
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(suggestion))
                return suggestion;
            var hasLower = false;
            foreach (var c in original)
                if (char.IsLower(c)) { hasLower = true; break; }
            if (!hasLower && original.Length > 1)
                return suggestion.ToUpperInvariant();          // ALL CAPS
            if (char.IsUpper(original[0]))
                return char.ToUpperInvariant(suggestion[0]) + suggestion.Substring(1);
            return suggestion;
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
