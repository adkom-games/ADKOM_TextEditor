#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>Compiles a single addon source file into an in-memory
    /// assembly. Implemented in the Roslyn-gated semantics assembly and
    /// discovered via TypeCache (mirrors ISemanticProvider) so the core never
    /// hard-references Roslyn.</summary>
    public interface IAddonCompiler
    {
        bool TryCompile(string path, out Assembly assembly, out string[] errors);
    }

    /// <summary>
    /// The addons registry: scans the machine-shared folder, compiles each
    /// .cs with the bundled Roslyn, applies the semver gate, runs resident
    /// OnLoad hooks, and feeds the Tools → Addons menu. Addon failures never
    /// break the editor — they land in the ATE console.
    /// </summary>
    public static class AteAddonManager
    {
        public class Entry
        {
            public string Name;        // display name (attribute or class name)
            public string Category;    // attribute category, "General" default
            public string File;        // source file (full path)
            public string ApiVersion;  // declared target version
            public Type Type;          // entry class (null when not loadable)
            public string Error;       // compile/compat/reflection failure
            public bool Compatible => Error == null && Type != null;

            // Security gate (see AddonSecurity): nothing executes until the
            // user approves this exact file content once.
            public bool Approved;
            internal string Hash;
            internal List<AddonSecurity.Finding> Findings;
        }

        static readonly List<Entry> _entries = new List<Entry>();
        static bool _loaded;
        static IAddonCompiler _compiler;
        static bool _compilerSearched;

        public static string AddonsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "Addons");

        public static IReadOnlyList<Entry> Entries
        {
            get { if (!_loaded) Reload(); return _entries; }
        }

        public static bool CompilerAvailable
        {
            get
            {
                if (!_compilerSearched)
                {
                    _compilerSearched = true;
                    foreach (var t in TypeCache.GetTypesDerivedFrom<IAddonCompiler>())
                    {
                        if (t.IsAbstract || t.IsInterface) continue;
                        try { _compiler = (IAddonCompiler)Activator.CreateInstance(t); break; }
                        catch (Exception ex)
                        {
                            AteConsole.Warn("[ADKOM Text Editor] Addon compiler failed to load: " + ex.Message);
                        }
                    }
                }
                return _compiler != null;
            }
        }

        [InitializeOnLoadMethod]
        static void AutoLoad()
        {
            // Lifecycle contract: OnUnload always runs before the assemblies
            // that hold addon state go away (domain reload / editor quit).
            AssemblyReloadEvents.beforeAssemblyReload += UnloadResidents;
            EditorApplication.quitting += UnloadResidents;
            EditorApplication.delayCall += () => { if (!_loaded) Reload(); };
        }

        /// <summary>Rescans the folder, recompiles everything, and re-runs
        /// resident OnLoad hooks. Safe to call any time.</summary>
        public static void Reload()
        {
            UnloadResidents();
            _loaded = true;
            _entries.Clear();
            EnsureFolder();
            if (!CompilerAvailable) return; // menu explains the requirement
            string[] files;
            try { files = Directory.GetFiles(AddonsFolder, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addons folder unreadable: " + ex.Message);
                return;
            }
            foreach (var file in files)
                LoadOne(file);
            DisambiguateNames();
            RunResidents();
            if (_entries.Count > 0)
                AteConsole.Log(string.Format(L10n.Tr("{0} addon(s) loaded."), _entries.Count));
        }

        static void LoadOne(string file)
        {
            var entry = new Entry
            {
                File = file,
                Name = Path.GetFileNameWithoutExtension(file),
                Category = "General",
                ApiVersion = "?"
            };
            _entries.Add(entry);
            try
            {
                // Security scan BEFORE anything else: hash the exact content,
                // record findings, and look up prior consent. Compiling below
                // is safe (nothing executes); execution is gated on Approved.
                string source = File.ReadAllText(file);
                entry.Hash = AddonSecurity.Hash(source);
                entry.Findings = AddonSecurity.Scan(source);
                entry.Approved = AddonSecurity.IsApproved(file, entry.Hash);

                if (!_compiler.TryCompile(file, out var asm, out var errors))
                {
                    entry.Error = string.Join("\n", errors);
                    AteConsole.Warn("[ADKOM Text Editor] Addon failed to compile: " +
                        Path.GetFileName(file) + "\n" + entry.Error);
                    return;
                }
                foreach (var t in asm.GetTypes())
                {
                    var attr = t.GetCustomAttribute<AteAddonAttribute>();
                    if (attr == null || !typeof(IAteAddon).IsAssignableFrom(t) || t.IsAbstract)
                        continue;
                    entry.Type = t;
                    entry.Name = string.IsNullOrEmpty(attr.Name) ? t.Name : attr.Name;
                    entry.Category = string.IsNullOrEmpty(attr.Category) ? "General" : attr.Category;
                    entry.ApiVersion = attr.ApiVersion ?? "1.0";
                    if (!IsCompatible(entry.ApiVersion, out string why)) entry.Error = why;
                    return; // one addon class per file
                }
                entry.Error = L10n.Tr("no [AteAddon] class implementing IAteAddon found");
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
                AteConsole.Warn("[ADKOM Text Editor] Addon failed to load: " +
                    Path.GetFileName(file) + " — " + ex.Message);
            }
        }

        /// <summary>Semver gate: the addon's MAJOR must equal AteApi's, and
        /// its MINOR must not exceed it (an addon built for a NEWER minor may
        /// call API we don't have).</summary>
        static bool IsCompatible(string declared, out string why)
        {
            why = null;
            ParseVersion(AteApi.ApiVersion, out int curMaj, out int curMin);
            if (!ParseVersion(declared, out int maj, out int min))
            {
                why = string.Format(L10n.Tr("bad ApiVersion '{0}'"), declared);
                return false;
            }
            if (maj != curMaj || min > curMin)
            {
                why = string.Format(L10n.Tr("needs API {0}, this ATE has {1}"),
                    declared, AteApi.ApiVersion);
                return false;
            }
            return true;
        }

        static bool ParseVersion(string v, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrEmpty(v)) return false;
            var parts = v.Split('.');
            if (!int.TryParse(parts[0], out major)) return false;
            if (parts.Length > 1 && !int.TryParse(parts[1], out minor)) return false;
            return true;
        }

        static void DisambiguateNames()
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _entries)
            {
                string key = e.Category + "/" + e.Name;
                if (seen.TryGetValue(key, out int n))
                {
                    seen[key] = n + 1;
                    e.Name += " (" + (n + 1) + ")";
                }
                else seen[key] = 1;
            }
        }

        // Resident addons are SINGLE instances (API 1.1): the same object
        // gets OnLoad, every menu Run, focus events, and OnUnload — so a game
        // keeps its state across the whole lifecycle.
        static readonly Dictionary<Type, IAteAddonResident> _residents =
            new Dictionary<Type, IAteAddonResident>();

        static void RunResidents()
        {
            foreach (var e in _entries)
            {
                if (!e.Compatible || !typeof(IAteAddonResident).IsAssignableFrom(e.Type)) continue;
                if (!e.Approved)
                {
                    AteConsole.Log(string.Format(
                        L10n.Tr("Addon '{0}' is awaiting your one-time approval — run it from Tools > Addons to review."),
                        e.Name));
                    continue;
                }
                StartResident(e);
            }
        }

        static void StartResident(Entry e)
        {
            try
            {
                var inst = (IAteAddonResident)Activator.CreateInstance(e.Type);
                _residents[e.Type] = inst;
                inst.OnLoad();
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addon OnLoad failed: " + e.Name + " — " + ex.Message);
            }
        }

        /// <summary>Tears down resident addons: OnUnload on every lifecycle
        /// addon, then drop instances, running ticks, and input event
        /// subscriptions so stale assemblies never keep acting.</summary>
        static void UnloadResidents()
        {
            foreach (var inst in _residents.Values)
            {
                if (!(inst is IAteAddonLifecycle lc)) continue;
                try { lc.OnUnload(); }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Addon OnUnload failed: " + ex.Message);
                }
            }
            _residents.Clear();
            AteApi.StopAllTicks();
            AteApi.DropInputSubscribers();
        }

        /// <summary>The ATE window's focus changed — forward to lifecycle addons.</summary>
        internal static void NotifyFocus(bool focused)
        {
            foreach (var inst in _residents.Values)
            {
                if (!(inst is IAteAddonLifecycle lc)) continue;
                try { if (focused) lc.OnFocusGained(); else lc.OnFocusLost(); }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Addon focus handler failed: " + ex.Message);
                }
            }
        }

        /// <summary>Menu entry point. Unapproved addons get the consent flow
        /// (risk report + banner) instead of running. Residents Run on their
        /// single lifecycle instance; plain addons are instantiated per Run.</summary>
        public static void Run(Entry e)
        {
            if (!e.Compatible) return;
            if (!e.Approved) { RequestConsent(e); return; }
            RunApproved(e);
        }

        static void RunApproved(Entry e)
        {
            try
            {
                if (_residents.TryGetValue(e.Type, out var resident)) resident.Run();
                else if (typeof(IAteAddonResident).IsAssignableFrom(e.Type))
                {
                    // First run after an in-session approval: bring the
                    // resident up properly (OnLoad, single instance), then Run.
                    StartResident(e);
                    if (_residents.TryGetValue(e.Type, out var started)) started.Run();
                }
                else ((IAteAddon)Activator.CreateInstance(e.Type)).Run();
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addon failed: " + e.Name + " — " + ex.Message);
            }
        }

        /// <summary>The one-time consent flow: writes the risk summary
        /// document, opens it in ATE, and shows the non-modal approval
        /// banner. Approval persists (keyed to the file's content hash) and
        /// the addon then runs.</summary>
        static void RequestConsent(Entry e)
        {
            string report = AddonSecurity.BuildReport(e.Name, e.File, e.Findings, e.Hash);
            string reportPath = null;
            try
            {
                string dir = Path.Combine(AddonsFolder, ".security-reports");
                Directory.CreateDirectory(dir);
                reportPath = Path.Combine(dir,
                    Path.GetFileNameWithoutExtension(e.File) + ".security.md");
                File.WriteAllText(reportPath, report);
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Could not write security report: " + ex.Message);
            }

            TextEditorWindow.Open();
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            var w = all.Length > 0 ? all[0] : null;
            if (w == null) return;
            if (reportPath != null) TextEditorWindow.OpenExternal(reportPath, 1, 1);
            int high = 0;
            if (e.Findings != null) foreach (var f in e.Findings) if (f.High) high++;
            // Findings also land in the "<script> Scanner Results" console
            // tab: one clickable row per finding jumping to file:line.
            if (e.Findings != null && e.Findings.Count > 0)
            {
                var items = new List<TextEditorWindow.PickLocation>();
                foreach (var f in e.Findings)
                    items.Add(new TextEditorWindow.PickLocation
                    {
                        Path = e.File,
                        Line = f.Line - 1,
                        Col = 0,
                        Preview = (f.High ? "HIGH      " : "moderate  ") + f.Api + " — " + f.Risk
                    });
                w.ShowScannerResults(e.Name, items);
            }
            string msg = e.Findings != null && e.Findings.Count > 0
                ? string.Format(L10n.Tr("Addon '{0}' uses {1} potentially dangerous API(s) ({2} high severity) — review the report, then approve to run it. Approval is one-time for this exact file content."),
                    e.Name, e.Findings.Count, high)
                : string.Format(L10n.Tr("Addon '{0}': no dangerous APIs detected, but addons run with full editor privileges. Approve to run it (one-time for this exact file content)."),
                    e.Name);
            w.ShowAddonConsent(msg, () =>
            {
                AddonSecurity.Approve(e.File, e.Hash);
                e.Approved = true;
                RunApproved(e);
            });
        }

        /// <summary>Copies the sample addons shipped with the package
        /// (Samples~/Addons) into the shared folder and reloads. Existing
        /// files with the same names are overwritten (they are samples).</summary>
        public static void InstallSamples()
        {
            EnsureFolder();
            string src;
            try { src = Path.GetFullPath("Packages/com.adkom.text-editor/Samples~/Addons"); }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Sample addons not found: " + ex.Message);
                return;
            }
            if (!Directory.Exists(src))
            {
                AteConsole.Warn("[ADKOM Text Editor] Sample addons folder missing: " + src);
                return;
            }
            int copied = 0;
            foreach (var f in Directory.GetFiles(src, "*.cs"))
            {
                try
                {
                    File.Copy(f, Path.Combine(AddonsFolder, Path.GetFileName(f)), overwrite: true);
                    copied++;
                }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Could not copy sample " +
                        Path.GetFileName(f) + ": " + ex.Message);
                }
            }
            AteConsole.Log(string.Format(L10n.Tr("{0} sample addon(s) installed."), copied));
            Reload();
        }

        public static void OpenFolder()
        {
            EnsureFolder();
            EditorUtility.RevealInFinder(AddonsFolder);
        }

        static void EnsureFolder()
        {
            try
            {
                if (Directory.Exists(AddonsFolder)) return;
                Directory.CreateDirectory(AddonsFolder);
                File.WriteAllText(Path.Combine(AddonsFolder, "README.md"),
"# ATE Addons\n\n" +
"Drop `.cs` addon files here — every ATE instance on this machine loads\n" +
"them (no project changes; compiled in-memory by ATE's bundled Roslyn).\n" +
"Semantic Features must be enabled in ATE's Settings.\n\n" +
"An addon is one class with the [AteAddon] attribute implementing\n" +
"IAteAddon (menu-invoked) or IAteAddonResident (also runs OnLoad at\n" +
"startup — subscribe to AteApi events there). It appears under\n" +
"ATE's Tools > Addons > {Category} > {Name}.\n\n" +
"```csharp\n" +
"using ADKOM.TextEditor.Scripting;\n\n" +
"[AteAddon(Name = \"Hello Addon\", Category = \"Samples\", ApiVersion = \"1.0\")]\n" +
"public class HelloAddon : IAteAddonResident\n" +
"{\n" +
"    public void OnLoad()\n" +
"    {\n" +
"        AteApi.documentSaved += d => UnityEngine.Debug.Log(\"[Hello] saved: \" + d.DisplayName);\n" +
"    }\n\n" +
"    public void Run()\n" +
"    {\n" +
"        var doc = AteApi.ActiveDocument;\n" +
"        UnityEngine.Debug.Log(\"[Hello] active: \" + (doc != null ? doc.DisplayName : \"none\"));\n" +
"    }\n" +
"}\n" +
"```\n\n" +
"API compatibility follows semver: your ApiVersion's MAJOR must match\n" +
"ATE's AteApi.ApiVersion (currently " + AteApi.ApiVersion + ") and its MINOR must not be\n" +
"newer. Incompatible addons appear disabled in the menu with the reason.\n");
            }
            catch (Exception) { }
        }
    }
}
#endif
