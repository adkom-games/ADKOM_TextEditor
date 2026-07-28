#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor.Scripting
{
    /// <summary>Tools → Addons → Signing: create an identity, sign an addon
    /// you distribute, endorse someone else's (this version, or vouch for
    /// the author's key). Endorsements are sidecar files postable anywhere
    /// (also copied to the clipboard). See Addon Signing Spec, issue #27.</summary>
    internal static class AddonSigningMenu
    {
        internal static void Fill(GenericMenu m, string root)
        {
            string sub = root + L10n.Tr("Signing") + "/";
            string who = AddonSigning.HasIdentity
                ? string.Format(L10n.Tr("Identity: {0} ({1})"), AddonSigning.IdentityName,
                    AddonSigning.Fingerprint(AddonSigning.IdentityPublicKey))
                : L10n.Tr("Identity: none");
            m.AddDisabledItem(new GUIContent(sub + who.Replace('/', '∕')));
            m.AddItem(new GUIContent(sub + L10n.Tr("Create Identity...")), false, CreateIdentity);
            m.AddItem(new GUIContent(sub + L10n.Tr("Restore Identity from Backup...")), false, ImportIdentity);
            if (AddonSigning.HasIdentity)
            {
                m.AddItem(new GUIContent(sub + L10n.Tr("Copy My Public Identity")), false, CopyIdentity);
                m.AddItem(new GUIContent(sub + L10n.Tr("Back Up Identity...")), false, ExportIdentity);
                m.AddSeparator(sub);
                foreach (var e in AteAddonManager.Entries)
                {
                    var entry = e;
                    string safe = e.Name.Replace('/', '∕');
                    m.AddItem(new GUIContent(sub + L10n.Tr("Sign as Author") + "/" + safe),
                        false, () => SignAuthor(entry));
                    m.AddItem(new GUIContent(sub + L10n.Tr("Endorse This Version") + "/" + safe),
                        false, () => EndorseContent(entry));
                    m.AddItem(new GUIContent(sub + L10n.Tr("Vouch for the Author's Key") + "/" + safe),
                        false, () => EndorsePublisher(entry));
                }
            }
        }

        static void CreateIdentity()
        {
            var w = ActiveWindow();
            if (w == null) return;
            w.ApiPrompt(L10n.Tr("Your signing name:"), false, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (AddonSigning.HasIdentity &&
                    !EditorUtility.DisplayDialog(L10n.Tr("Replace signing identity?"),
                        L10n.Tr("You already have a signing identity. Creating a new one REPLACES it — anything signed with the old key keeps its old signature, and people who pinned your old key will see an impersonation warning."),
                        L10n.Tr("Replace"), L10n.Tr("Cancel")))
                    return;
                if (AddonSigning.CreateIdentity(name.Trim(), out string err))
                {
                    AteConsole.Log(string.Format(L10n.Tr("Signing identity created: {0} ({1}). Publish that fingerprint where people can check it."),
                        name.Trim(), AddonSigning.Fingerprint(AddonSigning.IdentityPublicKey)));
                    CopyIdentity();
                }
                else AteConsole.Warn("[ADKOM Text Editor] Could not create identity: " + err);
            }, null, "");
        }

        static void CopyIdentity()
        {
            if (!AddonSigning.HasIdentity) return;
            string text = AddonSigning.IdentityName + "  " +
                AddonSigning.Fingerprint(AddonSigning.IdentityPublicKey) + "\n" +
                AddonSigning.IdentityPublicKey;
            EditorGUIUtility.systemCopyBuffer = text;
            AteConsole.Log(L10n.Tr("Public identity copied to the clipboard."));
        }

        /// <summary>Backup: the private key re-wrapped under a passphrase so
        /// it restores on ANY machine (identity.json itself is bound to this
        /// Windows user). Keep the file AND the passphrase safe.</summary>
        static void ExportIdentity()
        {
            var w = ActiveWindow();
            if (w == null) return;
            string suggested = SafeFile(AddonSigning.IdentityName) + AddonSigning.ExportExt;
            string path = EditorUtility.SaveFilePanel(L10n.Tr("Back Up Signing Identity"),
                "", suggested, AddonSigning.ExportExt.TrimStart('.'));
            if (string.IsNullOrEmpty(path)) return;
            w.ApiPrompt(L10n.Tr("Passphrase to protect the backup (visible while typing):"), false, pass =>
            {
                if (string.IsNullOrEmpty(pass))
                { AteConsole.Warn(L10n.Tr("Backup cancelled: a passphrase is required.")); return; }
                if (AddonSigning.ExportIdentity(path, pass, out string err))
                    AteConsole.Log(string.Format(L10n.Tr("Identity backed up to {0}. Store it off this machine — with the passphrase, it restores your signing key anywhere. Without it, nobody can use the file."), path));
                else AteConsole.Warn("[ADKOM Text Editor] " + L10n.Tr("Backup failed: ") + err);
            }, null, "");
        }

        static void ImportIdentity()
        {
            var w = ActiveWindow();
            if (w == null) return;
            string path = EditorUtility.OpenFilePanel(L10n.Tr("Restore Signing Identity"),
                "", AddonSigning.ExportExt.TrimStart('.'));
            if (string.IsNullOrEmpty(path)) return;
            if (AddonSigning.HasIdentity &&
                !EditorUtility.DisplayDialog(L10n.Tr("Replace signing identity?"),
                    L10n.Tr("Restoring REPLACES the identity on this machine. The current key is lost unless you have backed it up."),
                    L10n.Tr("Replace"), L10n.Tr("Cancel")))
                return;
            w.ApiPrompt(L10n.Tr("Backup passphrase:"), false, pass =>
            {
                if (AddonSigning.ImportIdentity(path, pass, out string name, out string fp, out string err))
                    AteConsole.Log(string.Format(L10n.Tr("Identity restored: {0} ({1}). Signatures you make here are now identical to those from your other machine."), name, fp));
                else AteConsole.Warn("[ADKOM Text Editor] " + L10n.Tr("Restore failed: ") + err);
            }, null, "");
        }

        static string SafeFile(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "identity")
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            return sb.Length > 0 ? sb.ToString() : "identity";
        }

        static void SignAuthor(AteAddonManager.Entry e)
        {
            var env = AddonSigning.SignAuthor(AteAddonManager.HashOf(e), out string err);
            if (env == null) { AteConsole.Warn("[ADKOM Text Editor] " + err); return; }
            AddonSigning.WriteSidecar(e.File, AddonSigning.KindAuthor, env);
            AteConsole.Log(string.Format(L10n.Tr("Signed '{0}' as author. Re-sign after any change to its files."), e.Name));
            AteAddonManager.Reload();
        }

        static void EndorseContent(AteAddonManager.Entry e)
        {
            var env = AddonSigning.SignEndorseContent(AteAddonManager.HashOf(e), out string err);
            if (env == null) { AteConsole.Warn("[ADKOM Text Editor] " + err); return; }
            AddonSigning.WriteSidecar(e.File, AddonSigning.KindEndorseContent, env);
            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(env, true);
            AteConsole.Log(string.Format(L10n.Tr("Endorsed '{0}' (this exact version). The endorsement is on the clipboard — post it anywhere; it stays valid for every copy with this content."), e.Name));
            AteAddonManager.Reload();
        }

        static void EndorsePublisher(AteAddonManager.Entry e)
        {
            string fp = AteAddonManager.AuthorFingerprintOf(e);
            if (fp == null)
            {
                AteConsole.Warn(string.Format(L10n.Tr("'{0}' has no valid author signature to vouch for."), e.Name));
                return;
            }
            var env = AddonSigning.SignEndorsePublisher(fp, out string err);
            if (env == null) { AteConsole.Warn("[ADKOM Text Editor] " + err); return; }
            AddonSigning.WriteSidecar(e.File, AddonSigning.KindEndorsePublisher, env);
            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(env, true);
            AteConsole.Log(string.Format(L10n.Tr("Vouched for the author key {0}. This endorsement survives new versions and applies to any addon signed with that key."), fp));
            AteAddonManager.Reload();
        }

        static TextEditorWindow ActiveWindow()
        {
            TextEditorWindow.Open();
            var all = Resources.FindObjectsOfTypeAll<TextEditorWindow>();
            return all.Length > 0 ? all[0] : null;
        }
    }
}
#endif
