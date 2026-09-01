// =============================================================================
//  ADKOM Text Editor — the ATE half of the ALS co-editing bridge
//  (ALS_ATE_CoEditing_Contract.md v1.0 §6.1), plus the reflection proxy that
//  reaches ALS's half (§6.2).
//
//  Reflection in both directions is deliberate: neither product takes a package
//  dependency on the other, neither fails to load because the other is absent
//  or old, and both store listings stay independent. A version mismatch or a
//  missing member turns co-editing off with one log line and leaves ATE a
//  perfectly ordinary text editor.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor.CoEdit
{
    /// <summary>The surface ALS calls. Every member is main-thread.</summary>
    public static class Bridge
    {
        /// <summary>Bumped only for incompatible changes; ALS checks it.</summary>
        public const int ContractVersion = 1;

        public static bool IsAvailable => true;

        static string _selfId = "";
        static readonly Dictionary<string, CoEditDocument> _docs =
            new Dictionary<string, CoEditDocument>();
        static readonly Dictionary<string, (string name, Color color, string role)> _peers =
            new Dictionary<string, (string, Color, string)>();
        // path → (userId → caret/anchor), so a tab change can republish the set.
        static readonly Dictionary<string, Dictionary<string, (int caret, int anchor)>> _carets =
            new Dictionary<string, Dictionary<string, (int, int)>>();

        static long _nextTemp = 1;

        // ------------------------------------------------------ ALS → ATE (§6.1)

        public static void SessionStarted(string selfUserId, string selfName)
        {
            _selfId = selfUserId ?? "";
            foreach (var d in _docs.Values)
                d.SelfId = _selfId;
            // Re-announce every open document: ALS decides which become shared.
            var w = Window();
            if (w == null)
                return;
            w.CoEditAttach();
            foreach (var doc in w.OpenDocuments())
            {
                var path = TextEditorWindow.CoEditPathOf(doc);
                if (path != null)
                    AlsProxy.DocOpened(path);
            }
        }

        public static void SessionEnded()
        {
            var w = Window();
            _docs.Clear();
            _carets.Clear();
            _peers.Clear();
            if (w?.CoEditActiveView != null)
            {
                w.CoEditActiveView.ClearAllRemoteCarets();
                w.CoEditActiveView.readOnly = false;
            }
        }

        public static void PeerUpdated(string userId, string name, float r, float g, float b, string role)
        {
            _peers[userId ?? ""] = (name, new Color(r, g, b), role);
        }

        public static void PeerLeft(string userId)
        {
            _peers.Remove(userId ?? "");
            foreach (var byUser in _carets.Values)
                byUser.Remove(userId ?? "");
            var win = Window();
            win?.CoEditActiveView?.ClearRemoteCaret(userId);
            win?.CoEditRefreshTabs();
        }

        /// <summary>Every project document ATE currently has open, project-
        /// relative. ALS polls this so document registration self-heals: a push
        /// at open time alone is hostage to window-creation and domain-reload
        /// ordering, and a missed push means a document silently never shares.</summary>
        public static string[] ListOpenDocuments()
        {
            var w = Window();
            if (w == null)
                return System.Array.Empty<string>();
            // The poll doubles as the capture-hook heartbeat: re-attaching to the
            // current CodeView here means a rebuilt view can never leave local
            // edits uncaptured.
            w.CoEditAttach();
            var list = new List<string>();
            foreach (var doc in w.OpenDocuments())
            {
                var path = TextEditorWindow.CoEditPathOf(doc);
                if (path != null)
                    list.Add(path);
            }
            return list.ToArray();
        }

        /// <summary>The document the user is actually looking at, or null. Only
        /// the ACTIVE document publishes a caret: a peer who switches tabs must
        /// stop appearing in the document they left, and per-document pushes
        /// cannot express that on their own.</summary>
        public static string ActiveDocumentPath()
        {
            var w = Window();
            return w == null ? null : TextEditorWindow.CoEditPathOf(w.ActiveDocument());
        }

        /// <summary>Live caret and selection anchor of the active document, as
        /// {caret, anchor}, or null when that is not this document. Read on
        /// demand rather than pushed on an event: selections are made by many
        /// paths (double-click a word, triple-click a line, drag to extend) and
        /// any of them that updates the anchor without moving the caret would
        /// otherwise never reach peers.</summary>
        public static int[] GetLocalSelection(string docPath)
        {
            var w = Window();
            if (w == null || string.IsNullOrEmpty(docPath))
                return null;
            if (TextEditorWindow.CoEditPathOf(w.ActiveDocument()) != docPath)
                return null;
            var view = w.CoEditActiveView;
            return view == null ? null : new[] { view.cursorIndex, view.selectIndex };
        }

        /// <summary>The host's ALS asks for this editor's live buffer — a live
        /// buffer beats disk as the document's authority (contract §2.1).</summary>
        public static string GetLiveBuffer(string docPath) => Window()?.CoEditTextOf(docPath);

        /// <summary>Converged text for the host's save (contract §3).</summary>
        public static string SnapshotFor(string docPath) => Window()?.CoEditTextOf(docPath);

        /// <summary>A path became shared: adopt the promoted base content and
        /// start merging from `baseSeq`.</summary>
        public static void BecameShared(string docPath, long baseSeq, string content, bool readOnly)
        {
            if (string.IsNullOrEmpty(docPath))
                return;
            if (!_docs.TryGetValue(docPath, out var doc))
                _docs[docPath] = doc = new CoEditDocument(docPath);
            doc.SelfId = _selfId;
            doc.Reset(baseSeq, content, readOnly);
            Window()?.CoEditSetText(docPath, content, readOnly);
        }

        public static void ShareRefused(string docPath, string reason)
        {
            Debug.Log($"[ATE] {docPath} is not co-editable: {reason}");
        }

        public static void SharingEnded(string docPath, string reason)
        {
            _docs.Remove(docPath ?? "");
            _carets.Remove(docPath ?? "");
            var w = Window();
            if (w != null)
            {
                w.CoEditSetReadOnly(docPath, false);
                w.CoEditActiveView?.ClearAllRemoteCarets();
            }
            Debug.Log($"[ATE] co-editing ended for {docPath}: {reason}");
        }

        public static void ApplyRemoteEdit(string docPath, long seq, string authorId, string batchJson)
        {
            if (!_docs.TryGetValue(docPath ?? "", out var doc))
                return;
            var edits = doc.IncorporateRemote(seq, authorId, batchJson);
            if (edits == null)
            {
                Debug.LogWarning($"[ATE] dropped a malformed co-edit batch for {docPath}.");
                return;
            }
            Window()?.CoEditApply(docPath, edits);
        }

        public static void LocalEditAcknowledged(string docPath, long tempSeq, long seq)
        {
            if (_docs.TryGetValue(docPath ?? "", out var doc))
                doc.Acknowledged(tempSeq, seq);
        }

        public static void RemoteCaret(string docPath, string userId, int caret, int anchor)
        {
            if (string.IsNullOrEmpty(docPath) || userId == _selfId)
                return;
            if (!_carets.TryGetValue(docPath, out var byUser))
                _carets[docPath] = byUser = new Dictionary<string, (int, int)>();
            byUser[userId ?? ""] = (caret, anchor);

            var w = Window();
            if (w == null || !IsActive(w, docPath))
                return;
            var (name, color, _) = PeerInfo(userId);
            w.CoEditActiveView?.SetRemoteCaret(userId, name, color, caret, anchor);
            w.CoEditRefreshTabs();
        }

        public static void RemoteCaretGone(string docPath, string userId)
        {
            if (_carets.TryGetValue(docPath ?? "", out var byUser))
                byUser.Remove(userId ?? "");
            var w = Window();
            if (w == null)
                return;
            if (IsActive(w, docPath))
                w.CoEditActiveView?.ClearRemoteCaret(userId);
            w.CoEditRefreshTabs();
        }

        // ------------------------------------------------------ ATE → ALS (§6.2)

        /// <summary>A local edit landed in the buffer. Only shared documents
        /// broadcast; everything else stays a purely local file (contract §2).</summary>
        internal static void LocalEdit(string docPath, int at, int del, string ins)
        {
            if (string.IsNullOrEmpty(docPath) || !_docs.TryGetValue(docPath, out var doc))
                return;
            if (doc.ReadOnly || !AlsProxy.CanEdit)
                return;
            var batch = new EditBatch();
            batch.Edits.Add(new Edit(at, del, ins));
            var temp = _nextTemp++;
            var json = doc.RecordLocal(batch, temp);
            if (json == null)
                return;
            var seq = AlsProxy.SendLocalEdit(docPath, json);
            if (seq < 0)
                return;
            // ALS's correlation id wins so acks match; re-key the pending entry.
            doc.Reindex(temp, seq);
        }

        internal static void LocalCaret(string docPath, int caret, int anchor)
        {
            if (string.IsNullOrEmpty(docPath) || !_docs.ContainsKey(docPath))
                return;
            AlsProxy.SendLocalCaret(docPath, caret, anchor);
        }

        /// <summary>ATE is about to save. True means ALS handled it and ATE must
        /// not write the file (contract §3).</summary>
        internal static bool InterceptSave(string docPath) =>
            !string.IsNullOrEmpty(docPath) && AlsProxy.InterceptSave(docPath);

        internal static void DocumentOpened(string docPath)
        {
            if (!string.IsNullOrEmpty(docPath))
                AlsProxy.DocOpened(docPath);
        }

        internal static void DocumentClosed(string docPath)
        {
            if (string.IsNullOrEmpty(docPath))
                return;
            _docs.Remove(docPath);
            _carets.Remove(docPath);
            AlsProxy.DocClosed(docPath);
        }

        /// <summary>True when the document is shared and this user may not edit —
        /// observers get a read-only buffer (contract §8).</summary>
        internal static bool IsReadOnly(string docPath) =>
            !string.IsNullOrEmpty(docPath)
            && _docs.TryGetValue(docPath, out var d) && d.ReadOnly;

        /// <summary>Re-draw every known caret for a document — used after a tab
        /// change, since one CodeView serves all tabs.</summary>
        internal static void RepublishCarets(string docPath)
        {
            var w = Window();
            if (w?.CoEditActiveView == null || string.IsNullOrEmpty(docPath))
                return;
            if (!_carets.TryGetValue(docPath, out var byUser))
                return;
            foreach (var kv in byUser)
            {
                var (name, color, _) = PeerInfo(kv.Key);
                w.CoEditActiveView.SetRemoteCaret(kv.Key, name, color, kv.Value.caret, kv.Value.anchor);
            }
        }

        internal static bool IsShared(string docPath) =>
            !string.IsNullOrEmpty(docPath) && _docs.ContainsKey(docPath);

        /// <summary>Is someone ELSE in this document right now? Distinct from
        /// IsShared, which is sticky for the whole session by design (contract
        /// §2) and therefore accumulates: after a while every document you have
        /// touched reads as "shared", which is useless as an at-a-glance signal.
        /// Live presence is the honest answer to "is anybody here", and it
        /// clears the moment they leave or switch tabs.</summary>
        internal static bool HasRemotePresence(string docPath) =>
            !string.IsNullOrEmpty(docPath)
            && _carets.TryGetValue(docPath, out var byUser) && byUser.Count > 0;

        // ------------------------------------------------------------- helpers

        static (string name, Color color, string role) PeerInfo(string userId)
        {
            if (_peers.TryGetValue(userId ?? "", out var p))
                return (p.name ?? userId, p.color, p.role);
            return (userId, Color.gray, "participant");
        }

        static bool IsActive(TextEditorWindow w, string docPath) =>
            TextEditorWindow.CoEditPathOf(w.ActiveDocument()) == docPath;

        static TextEditorWindow Window()
        {
            var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            return all != null && all.Length > 0 ? all[0] : null;
        }
    }

    /// <summary>Reflection proxy for ALS's bridge. Resolves once per domain and
    /// degrades to "no session" whenever ALS is absent, older, or unhappy.</summary>
    internal static class AlsProxy
    {
        const string TypeName = "Adkom.LinkedScenes.CoEdit.Bridge, AdkomLinkedScenes.Editor";
        const int RequiredVersion = 1;

        static bool _resolved;
        static Type _type;
        static PropertyInfo _sessionLive, _canEdit;
        static MethodInfo _docOpened, _docClosed, _sendLocalEdit, _sendLocalCaret, _interceptSave;

        static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            _type = Type.GetType(TypeName, throwOnError: false);
            if (_type == null)
                return; // ALS not installed — the normal case, not an error

            var version = _type.GetField("ContractVersion", BindingFlags.Public | BindingFlags.Static)
                ?.GetRawConstantValue() as int?;
            if (version != RequiredVersion)
            {
                Debug.Log($"[ATE] ADKOM Linked Scenes speaks co-editing contract v{version?.ToString() ?? "?"}, " +
                          $"this build speaks v{RequiredVersion} — shared document editing is off.");
                _type = null;
                return;
            }
            _sessionLive = _type.GetProperty("SessionLive", BindingFlags.Public | BindingFlags.Static);
            _canEdit = _type.GetProperty("CanEdit", BindingFlags.Public | BindingFlags.Static);
            _docOpened = M("DocOpened");
            _docClosed = M("DocClosed");
            _sendLocalEdit = M("SendLocalEdit");
            _sendLocalCaret = M("SendLocalCaret");
            _interceptSave = M("InterceptSave");
        }

        static MethodInfo M(string name) =>
            _type?.GetMethod(name, BindingFlags.Public | BindingFlags.Static);

        internal static bool SessionLive
        {
            get
            {
                Resolve();
                return Get(_sessionLive);
            }
        }

        internal static bool CanEdit
        {
            get
            {
                Resolve();
                return Get(_canEdit);
            }
        }

        static bool Get(PropertyInfo p)
        {
            if (_type == null || p == null)
                return false;
            try
            {
                return (bool)p.GetValue(null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void DocOpened(string path) => Call(_docOpened, path);
        internal static void DocClosed(string path) => Call(_docClosed, path);
        internal static void SendLocalCaret(string path, int caret, int anchor) =>
            Call(_sendLocalCaret, path, caret, anchor);

        internal static long SendLocalEdit(string path, string batchJson)
        {
            Resolve();
            if (_sendLocalEdit == null)
                return -1;
            try
            {
                return (long)_sendLocalEdit.Invoke(null, new object[] { path, batchJson });
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal static bool InterceptSave(string path)
        {
            Resolve();
            if (_interceptSave == null)
                return false;
            try
            {
                return (bool)_interceptSave.Invoke(null, new object[] { path });
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Call(MethodInfo m, params object[] args)
        {
            Resolve();
            if (m == null)
                return;
            try
            {
                m.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogWarning($"[ATE] Linked Scenes bridge threw in {m.Name}: " +
                                 $"{e.InnerException?.Message ?? e.Message}");
            }
        }
    }
}
