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
            if (AddonSigning.HasIdentity)
            {
                m.AddItem(new GUIContent(sub + L10n.Tr("Copy My Public Identity")), false, CopyIdentity);
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
