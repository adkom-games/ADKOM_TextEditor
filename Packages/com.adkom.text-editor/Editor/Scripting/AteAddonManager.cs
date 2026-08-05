#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>Compiles an addon's source files into one in-memory
    /// assembly. Implemented in the Roslyn-gated semantics assembly and
    /// discovered via TypeCache (mirrors ISemanticProvider) so the core never
    /// hard-references Roslyn.</summary>
    public interface IAddonCompiler
    {
        /// <summary>Every file of the addon folder compiles into ONE assembly.</summary>
        bool TryCompileMany(string[] paths, out Assembly assembly, out string[] errors);
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
            public string File;        // addon folder (full path)
            public string ApiVersion;  // declared target version
            public Type Type;          // entry class (null when not loadable)
            public string Error;       // compile/compat/reflection failure
            public bool Compatible => Error == null && Type != null;

            // Security gate (see AddonSecurity): nothing executes until the
            // user approves this exact file content once.
            public bool Approved;
            internal string Hash;
            internal List<AddonSecurity.Finding> Findings;
            // Signing/endorsements (AddonSigning, issue #27).
            internal AddonSigning.SigningInfo Signing;
            /// <summary>This addon is an installed COPY of a sample shipped
            /// with the package, and the shipped one has changed since —
            /// the common cause of a "signature invalid" on a sample.</summary>
            public bool OutdatedSample;
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
            // Addons are FOLDER-based: each first-level subfolder (not
            // starting with '.') is ONE addon whose .cs files (recursive)
            // compile together — a folder makes it unambiguous which files
            // belong to which addon. Top-level .cs files from the retired
            // single-file era are migrated into folders of their own first.
            try
            {
                MigrateSingleFileAddons();
                foreach (var dir in Directory.GetDirectories(AddonsFolder))
                {
                    if (Path.GetFileName(dir).StartsWith(".", StringComparison.Ordinal)) continue;
                    var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                    if (files.Length == 0) continue;
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    LoadOne(files, dir);
                }
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addons folder unreadable: " + ex.Message);
                return;
            }
            FlagOutdatedSamples();
            DisambiguateNames();
            RunResidents();
            if (_entries.Count > 0)
                AteConsole.Log(string.Format(L10n.Tr("{0} addon(s) loaded."), _entries.Count));
            OfferSampleReinstall();
        }

        /// <summary>Single-file addons are retired: any top-level .cs left in
        /// the addons folder moves into a subfolder of its own name so it
        /// keeps loading. The move changes the addon's identity hash (the
        /// hash covers relative paths), so approval — and any signature —
        /// must be renewed for the folder; old sidecar .atesig files are left
        /// behind, since they could never verify against the new identity.</summary>
        static void MigrateSingleFileAddons()
        {
            foreach (var file in Directory.GetFiles(AddonsFolder, "*.cs", SearchOption.TopDirectoryOnly))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                string dir = Path.Combine(AddonsFolder, stem);
                try
                {
                    if (Directory.Exists(dir))
                    {
                        AteConsole.Warn("[ADKOM Text Editor] Addon file not migrated (folder '" +
                            stem + "' already exists — merge or remove by hand): " + file);
                        continue;
                    }
                    Directory.CreateDirectory(dir);
                    File.Move(file, Path.Combine(dir, Path.GetFileName(file)));
                    AteConsole.Log(string.Format(
                        L10n.Tr("Addon '{0}' moved into its own folder — addons are folder-based now. Approval (and any signature) must be renewed for the new folder identity."),
                        stem));
                }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Could not migrate addon file " +
                        file + ": " + ex.Message);
                }
            }
        }

        /// <summary>Loads one addon folder: all its .cs files compile
        /// together. <paramref name="key"/> is the folder path — the
        /// identity consent binds to.</summary>
        static void LoadOne(string[] files, string key)
        {
            var entry = new Entry
            {
                File = key,
                Name = Path.GetFileNameWithoutExtension(key),
                Category = "General",
                ApiVersion = "?"
            };
            _entries.Add(entry);
            try
            {
                // Security scan BEFORE anything else: hash the exact content
                // set, record per-file findings, and look up prior consent.
                // Compiling below is safe (nothing executes); execution is
                // gated on Approved.
                entry.Hash = AddonSecurity.HashFiles(files, key);
                entry.Findings = new List<AddonSecurity.Finding>();
                foreach (var f in files)
                {
                    var fs = AddonSecurity.Scan(File.ReadAllText(f));
                    foreach (var fd in fs) fd.File = f;
                    entry.Findings.AddRange(fs);
                }
                entry.Approved = AddonSecurity.IsApproved(key, entry.Hash);
                entry.Signing = AddonSigning.Evaluate(key, entry.Hash);

                if (!_compiler.TryCompileMany(files, out var asm, out var errors))
                {
                    entry.Error = string.Join("\n", errors);
                    AteConsole.Warn("[ADKOM Text Editor] Addon failed to compile: " +
                        Path.GetFileName(key) + "\n" + entry.Error);
                    return;
                }
                foreach (var t in asm.GetTypes())
                {
                    var attr = t.GetCustomAttribute<AteAddonAttribute>();
                    if (attr == null || !typeof(IAteAddon).IsAssignableFrom(t) || t.IsAbstract)
                        continue;
                    if (entry.Type != null)
                    {
                        entry.Error = L10n.Tr("more than one [AteAddon] class in the addon");
                        entry.Type = null;
                        return;
                    }
                    entry.Type = t;
                    entry.Name = string.IsNullOrEmpty(attr.Name) ? t.Name : attr.Name;
                    entry.Category = string.IsNullOrEmpty(attr.Category) ? "General" : attr.Category;
                    entry.ApiVersion = attr.ApiVersion ?? "1.0";
                    if (!IsCompatible(entry.ApiVersion, out string why)) { entry.Error = why; return; }
                }
                if (entry.Type == null)
                    entry.Error = L10n.Tr("no [AteAddon] class implementing IAteAddon found");
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
                AteConsole.Warn("[ADKOM Text Editor] Addon failed to load: " +
                    Path.GetFileName(key) + " — " + ex.Message);
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
                MaybeQueueState(e);
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addon OnLoad failed: " + e.Name + " — " + ex.Message);
            }
        }

        /// <summary>Tears down resident addons: SaveState on every stateful
        /// addon (API 1.2 — before OnUnload, mobile-style "this may be your
        /// last callback"), OnUnload on every lifecycle addon, then drop
        /// instances, running ticks, and input event subscriptions so stale
        /// assemblies never keep acting.</summary>
        static void UnloadResidents()
        {
            foreach (var kv in _residents)
            {
                if (!(kv.Value is IAteAddonStateful st)) continue;
                string folder = FolderNameOf(kv.Key);
                if (folder == null) continue;
                try
                {
                    // A state that arrived but was never delivered (the ATE
                    // window never opened this domain) outranks the addon's
                    // current emptiness — put it back instead of wiping it.
                    if (_pendingStates.TryGetValue(kv.Key, out string undelivered))
                        WriteStateFile(folder, undelivered);
                    else
                        WriteStateFile(folder, st.SaveState());
                }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Addon SaveState failed: " + ex.Message);
                }
            }
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

        // ---- Stateful-addon persistence (API 1.2) ----
        // The HOST owns storage (addons never touch disk): one state file per
        // addon folder name, written at teardown, read back at load, and
        // delivered once the ATE window (with its restored session documents)
        // exists.

        static string StateFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADKOM", "TextEditor", "AddonState");

        static string StateFile(string folderName) =>
            Path.Combine(StateFolder, folderName + ".state");

        static readonly Dictionary<Type, string> _pendingStates = new Dictionary<Type, string>();

        static string FolderNameOf(Type t)
        {
            foreach (var e in _entries)
                if (e.Type == t) return Path.GetFileName(e.File);
            return null;
        }

        static void WriteStateFile(string folderName, string state)
        {
            string f = StateFile(folderName);
            if (string.IsNullOrEmpty(state))
            {
                if (File.Exists(f)) File.Delete(f);
                return;
            }
            Directory.CreateDirectory(StateFolder);
            File.WriteAllText(f, state);
        }

        /// <summary>Reads a stateful addon's stored state (if any) into the
        /// pending set right after its OnLoad; delivery happens when the ATE
        /// window is up.</summary>
        static void MaybeQueueState(Entry e)
        {
            if (e.Type == null || !typeof(IAteAddonStateful).IsAssignableFrom(e.Type)) return;
            try
            {
                string f = StateFile(Path.GetFileName(e.File));
                if (!File.Exists(f)) return;
                _pendingStates[e.Type] = File.ReadAllText(f);
                File.Delete(f); // deliver-once; UnloadResidents re-persists undelivered state
                EditorApplication.delayCall += DeliverPendingStates;
            }
            catch (Exception ex)
            {
                AteConsole.Warn("[ADKOM Text Editor] Addon state unreadable: " + ex.Message);
            }
        }

        /// <summary>Hands stored state back to stateful residents — only once
        /// an ATE window exists, so RestoreState can re-find its session
        /// documents. Also invoked from the window's CreateGUI for the
        /// window-opened-later case; idempotent.</summary>
        internal static void DeliverPendingStates()
        {
            if (_pendingStates.Count == 0) return;
            if (UnityEngine.Resources.FindObjectsOfTypeAll<TextEditorWindow>().Length == 0) return;
            var pending = new List<KeyValuePair<Type, string>>(_pendingStates);
            _pendingStates.Clear();
            foreach (var kv in pending)
            {
                if (!_residents.TryGetValue(kv.Key, out var inst) || !(inst is IAteAddonStateful st)) continue;
                try { st.RestoreState(kv.Value); }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Addon RestoreState failed: " + ex.Message);
                }
            }
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
            string report = AddonSecurity.BuildReport(e.Name, e.File, e.Findings, e.Hash)
                + AddonSecurity.BuildSignatureSection(e.Signing, SigningLine(e.Signing));
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
            if (reportPath != null)
            {
                // The report file may already be open from an earlier consent
                // round — its tab would show the STALE report (the reload
                // prompt gets stomped by the consent banner below).
                w.ApiReloadFromDiskIfOpen(reportPath);
                TextEditorWindow.OpenExternal(reportPath, 1, 1);
            }
            int high = 0;
            if (e.Findings != null) foreach (var f in e.Findings) if (f.High) high++;
            string signLine = SigningLine(e.Signing);
            // Findings also land in the "<script> Scanner Results" console
            // tab: one clickable row per finding jumping to file:line.
            if (e.Findings != null && e.Findings.Count > 0)
            {
                var items = new List<TextEditorWindow.PickLocation>();
                foreach (var f in e.Findings)
                    items.Add(new TextEditorWindow.PickLocation
                    {
                        Path = f.File ?? e.File,
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
            // A stale sample install explains an invalid signature far better
            // than a tamper warning does — say so, and offer the real fix.
            if (e.OutdatedSample)
                msg = string.Format(L10n.Tr("This is an OLDER copy of a sample shipped with this ATE, so its signature no longer matches its code — reinstalling the samples is the fix. "))
                    + signLine + "  " + msg;
            else msg = signLine + "  " + msg;
            var sig = e.Signing;
            System.Action approve = () =>
            {
                AddonSecurity.Approve(e.File, e.Hash);
                e.Approved = true;
                // TOFU: approving pins the author and every endorser seen.
                if (sig != null && sig.Status == AddonSigning.SigStatus.Signed)
                {
                    AddonSigning.PinApproved(sig.AuthorName, sig.AuthorFingerprint);
                    foreach (var en in sig.Endorsements)
                        AddonSigning.PinApproved(en.Name, en.Fingerprint);
                }
                RunApproved(e);
            };
            bool impersonation = sig != null && sig.AuthorPin == AddonSigning.PinState.Impersonation;
            bool distrusted = sig != null && (sig.AuthorPin == AddonSigning.PinState.Distrusted
                || sig.Status == AddonSigning.SigStatus.Tampered);
            System.Action distrust = sig != null && sig.AuthorFingerprint != null
                ? () => { AddonSigning.Distrust(sig.AuthorFingerprint, sig.AuthorName); Reload(); }
                : (System.Action)null;
            if (impersonation || distrusted)
                w.ShowAddonConsentTyped(msg, e.Name, approve, distrust);
            else
                w.ShowAddonConsent(msg, approve, distrust);
        }

        /// <summary>Marks installed addons whose shipped counterpart in this
        /// ATE version has different content. A stale install is the usual
        /// cause of "SIGNATURE INVALID" on a sample — the code and its
        /// signature come from different releases.</summary>
        static void FlagOutdatedSamples()
        {
            var shipped = AddonSigning.ShippedSampleKeys(out string err);
            if (err != null || shipped.Count == 0) return;
            foreach (var e in _entries)
            {
                string mine = Path.GetFileName(e.File);
                foreach (var key in shipped)
                {
                    if (!string.Equals(Path.GetFileName(key), mine, StringComparison.OrdinalIgnoreCase)) continue;
                    try { e.OutdatedSample = AddonSigning.HashOfAddonAt(key) != e.Hash; }
                    catch { }
                    break;
                }
            }
        }

        /// <summary>Non-modal offer to refresh installed samples that this
        /// ATE ships a newer copy of.</summary>
        static void OfferSampleReinstall()
        {
            var stale = new List<Entry>();
            foreach (var e in _entries) if (e.OutdatedSample) stale.Add(e);
            if (stale.Count == 0) return;
            var names = new List<string>();
            foreach (var e in stale) names.Add(e.Name);
            string list = string.Join(", ", names.ToArray());
            AteConsole.Log(string.Format(
                L10n.Tr("{0} installed sample addon(s) are older than the copies shipped with this ATE: {1}."),
                stale.Count, list));
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            if (all.Length == 0) return; // console message is enough until a window exists
            all[0].ShowSampleReinstallOffer(string.Format(
                L10n.Tr("Your installed sample addon(s) — {0} — are older than the copies shipped with this ATE, so their signatures no longer match their code. Reinstall them?"),
                list));
        }

        /// <summary>Accessors for the signing menu (the hash and author
        /// fingerprint are internal detail elsewhere).</summary>
        internal static string HashOf(Entry e) => e.Hash;
        internal static string AuthorFingerprintOf(Entry e) =>
            e.Signing != null && e.Signing.Status == AddonSigning.SigStatus.Signed
                ? e.Signing.AuthorFingerprint : null;

        /// <summary>The one-line signing verdict for banner + report.</summary>
        static string SigningLine(AddonSigning.SigningInfo s)
        {
            if (s == null || s.Status == AddonSigning.SigStatus.Unsigned)
                return L10n.Tr("UNSIGNED — author unknown.");
            if (s.Status == AddonSigning.SigStatus.Tampered)
                return L10n.Tr("⚠ SIGNATURE INVALID — content does not match the author's signature.");
            string who = s.AuthorName + " (" + s.AuthorFingerprint + ")";
            string pin;
            switch (s.AuthorPin)
            {
                case AddonSigning.PinState.Known:
                    var d = AddonSigning.PinDetails(s.AuthorFingerprint);
                    pin = string.Format(L10n.Tr("✓ known since {0}, {1} approval(s)"), d.firstSeen, d.approvals);
                    break;
                case AddonSigning.PinState.Impersonation:
                    pin = L10n.Tr("⚠ NAME KNOWN WITH A DIFFERENT KEY — possible impersonation!");
                    break;
                case AddonSigning.PinState.Distrusted:
                    pin = L10n.Tr("⚠ KEY MARKED DISTRUSTED");
                    break;
                default:
                    pin = L10n.Tr("first sight — verify the fingerprint out-of-band if it matters");
                    break;
            }
            string line = string.Format(L10n.Tr("Signed: {0} — {1}."), who, pin);
            if (s.ContentEndorsements > 0 || s.PublisherEndorsements > 0)
                line += " " + string.Format(
                    L10n.Tr("Endorsed: {0} for this version, {1} vouch for the author."),
                    s.ContentEndorsements, s.PublisherEndorsements);
            return line;
        }

        /// <summary>Copies the sample addons shipped with the package
        /// (Addons~) into the shared folder and reloads. Existing
        /// files with the same names are overwritten (they are samples).</summary>
        public static void InstallSamples()
        {
            EnsureFolder();
            string src;
            try { src = AtePackage.DiskRoot == null ? null : Path.Combine(AtePackage.DiskRoot, "Addons~"); }
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
            // Every sample is a folder addon. Signature sidecars ship WITH
            // the samples (AddonSigning): copying only .cs would land every
            // shipped sample as "UNSIGNED".
            foreach (var dir in Directory.GetDirectories(src))
            {
                try
                {
                    string dst = Path.Combine(AddonsFolder, Path.GetFileName(dir));
                    foreach (var pattern in new[] { "*.cs", "*" + AddonSigning.SidecarExt })
                        foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                        {
                            string rel = Path.GetFullPath(f).Substring(Path.GetFullPath(dir).Length).TrimStart('\\', '/');
                            string target = Path.Combine(dst, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Copy(f, target, overwrite: true);
                        }
                    copied++;
                }
                catch (Exception ex)
                {
                    AteConsole.Warn("[ADKOM Text Editor] Could not copy sample folder " +
                        Path.GetFileName(dir) + ": " + ex.Message);
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
"Each SUBFOLDER here is one addon: every `.cs` file inside it (recursive) compiles together into one in-memory assembly — the folder makes it unambiguous which files belong to which addon. Every ATE instance on this machine loads them (no project changes; compiled by ATE's bundled Roslyn). Semantic Features must be enabled in ATE's Settings. Folders starting with `.` are ignored.\n\n" +
"Exactly one class in the folder carries the [AteAddon] attribute and implements IAteAddon (menu-invoked) or IAteAddonResident (also runs OnLoad at startup — subscribe to AteApi events there). The addon appears under ATE's Tools > Addons > {Category} > {Name}.\n\n" +
"Example — save as `HelloAddon/HelloAddon.cs`:\n\n" +
"```csharp\n" +
"using ADKOM.TextEditor.Scripting;\n\n" +
"[AteAddon(Name = \"Hello Addon\", Category = \"Samples\", ApiVersion = \"1.3\")]\n" +
"public class HelloAddon : IAteAddonResident\n" +
"{\n" +
"    public void OnLoad()\n" +
"    {\n" +
"        AteApi.documentSaved += d => AteApi.DebugLog(\"[Hello] saved: \" + d.DisplayName);\n" +
"    }\n\n" +
"    public void Run()\n" +
"    {\n" +
"        var doc = AteApi.ActiveDocument;\n" +
"        AteApi.DebugLog(\"[Hello] active: \" + (doc != null ? doc.DisplayName : \"none\"));\n" +
"    }\n" +
"}\n" +
"```\n\n" +
"AteApi.DebugLog is the addon counterpart of UnityEngine.Debug.Log: same call, but the line goes to ATE's console pane (View > Console). ATE keeps Unity's console for your project's own messages.\n\n" +
"API compatibility follows semver: your ApiVersion's MAJOR must match ATE's AteApi.ApiVersion (currently " + AteApi.ApiVersion + ") and its MINOR must not be newer. Incompatible addons appear disabled in the menu with the reason.\n");
            }
            catch (Exception) { }
        }
    }
}
#endif
