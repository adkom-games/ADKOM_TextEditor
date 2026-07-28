#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;

namespace ADKOM.TextEditor
{
    // Non-modal prompts: the notify banner (file changed/deleted, unsaved changes) and the status-bar mini-buffer (Goto Line).
    public partial class TextEditorWindow
    {
        /// <summary>The non-modal in-window prompt: a message plus arbitrary
        /// action buttons in the notify banner. Never blocks Unity.</summary>
        void ShowBanner(string msg, params (string label, System.Action act)[] actions)
        {
            if (_notifyBar == null) return;
            _notifyLabel.text = msg;
            _notifyButtons.Clear();
            foreach (var (label, act) in actions)
            {
                var a = act;
                _notifyButtons.Add(new Button(() => a?.Invoke()) { text = label });
            }
            _notifyBar.style.display = DisplayStyle.Flex;
        }

        void HideBanner()
        {
            if (_notifyBar != null) _notifyBar.style.display = DisplayStyle.None;
        }

        /// <summary>Shows the non-modal banner when the active document's
        /// backing file changed on disk. Never blocks the editor: the old
        /// modal dialog froze Unity's main loop (and background tooling) any
        /// time the window regained focus with a changed file.</summary>
        bool CheckExternalChange(TextDocument doc)
        {
            if (doc != null && doc.FileDeletedOnDisk())
            {
                ShowBanner(string.Format(L10n.Tr("'{0}' was deleted from disk. Keep the buffer (Save can bring the file back), or close the tab?"), doc.DisplayName),
                    (L10n.Tr("Keep Buffer"), KeepDeletedBufferActive), (L10n.Tr("Close Tab"), CloseDeletedActive));
                return true;
            }
            if (doc == null || !doc.FileChangedOnDisk())
            {
                HideBanner();
                return false;
            }
            if (EditorConfig.AutoReloadFromDisk && !doc.IsDirty && doc == Active)
            {
                ReloadActiveFromDisk();
                return false;
            }
            ShowBanner(string.Format(L10n.Tr("'{0}' was modified outside the editor. Reload it? (unsaved changes here would be lost)"), doc.DisplayName),
                (L10n.Tr("Reload"), ReloadActiveFromDisk), (L10n.Tr("Keep Mine"), KeepMineActive));
            return true;
        }

        void ReloadActiveFromDisk()
        {
            if (!HasDocs || !Active.HasFile) return;
            Active.LoadFrom(Active.FilePath);
            _code?.SetValueWithoutNotify(Active.Content);
            RefreshFormatter();
            RebuildTabs();
            UpdateTitle();
            _notifyBar.style.display = DisplayStyle.None;
            PostStatus(string.Format(L10n.Tr("Reloaded {0} from disk."), Active.DisplayName));
        }

        void KeepMineActive()
        {
            if (!HasDocs || !Active.HasFile) return;
            // Stop re-prompting until the file changes again.
            Active.LastKnownWriteTimeUtcTicks = File.GetLastWriteTimeUtc(Active.FilePath).Ticks;
            Active.IsDirty = true;
            RebuildTabs();
            UpdateTitle();
            _notifyBar.style.display = DisplayStyle.None;
            PostStatus(string.Format(L10n.Tr("Kept in-editor version of {0}."), Active.DisplayName));
        }

        /// <summary>The backing file vanished but the buffer is intact — keep
        /// it (dirty, so the save guards protect it; Save recreates the file).</summary>
        void KeepDeletedBufferActive()
        {
            if (!HasDocs || !Active.HasFile) return;
            Active.DeletionNotified = true;
            Active.IsDirty = true;
            RebuildTabs();
            UpdateTitle();
            _notifyBar.style.display = DisplayStyle.None;
            PostStatus(string.Format(L10n.Tr("Kept buffer of deleted file {0} — Save to restore it to disk."), Active.DisplayName));
        }

        void CloseDeletedActive()
        {
            if (!HasDocs) return;
            _notifyBar.style.display = DisplayStyle.None;
            Active.IsDirty = false; // user chose to let the buffer go
            CloseTab(_active);
        }

        /// <summary>Addon security consent (AddonSecurity): the non-modal
        /// banner asking for the one-time approval. The risk report document
        /// is already open in a tab when this shows.</summary>
        internal void ShowAddonConsent(string message, System.Action onApprove,
            System.Action onDistrust = null)
        {
            if (onDistrust == null)
                ShowBanner(message,
                    (L10n.Tr("Approve and Run"), () => { HideBanner(); onApprove?.Invoke(); }),
                    (L10n.Tr("Not Now"), HideBanner));
            else
                ShowBanner(message,
                    (L10n.Tr("Approve and Run"), () => { HideBanner(); onApprove?.Invoke(); }),
                    (L10n.Tr("Not Now"), HideBanner),
                    (L10n.Tr("Distrust This Key"), () => { HideBanner(); onDistrust(); }));
        }

        /// <summary>Consent for a TAMPERED / possible-impersonation addon:
        /// approval requires typing the addon's name (deliberate friction —
        /// AddonSigning, issue #27).</summary>
        internal void ShowAddonConsentTyped(string message, string addonName,
            System.Action onApprove, System.Action onDistrust)
        {
            System.Action ask = null;
            ask = () => ShowBanner(message,
                (L10n.Tr("Approve Anyway…"), () =>
                {
                    HideBanner();
                    StartStatusPrompt(
                        string.Format(L10n.Tr("Type the addon name '{0}' to approve:"), addonName),
                        digitsOnly: false,
                        onCommit: s =>
                        {
                            if (string.Equals(s?.Trim(), addonName, System.StringComparison.Ordinal))
                                onApprove?.Invoke();
                            else
                            {
                                PostStatus(L10n.Tr("Name did not match — not approved."));
                                ask();
                            }
                        },
                        initialValue: "",
                        onCancel: ask);
                }),
                (L10n.Tr("Not Now"), HideBanner),
                (L10n.Tr("Distrust This Key"), () => { HideBanner(); onDistrust?.Invoke(); }));
            ask();
        }

        void BuildMiniBuffer(VisualElement statusBar)
        {
            _miniBuffer = new VisualElement { name = "mini-buffer" };
            _miniBuffer.style.flexDirection = FlexDirection.Row;
            _miniBuffer.style.alignItems = Align.Center;
            _miniBuffer.style.flexGrow = 1;
            _miniBuffer.style.display = DisplayStyle.None;
            _miniPrompt = new Label();
            _miniPrompt.style.unityFontStyleAndWeight = FontStyle.Bold;
            _miniPrompt.style.marginRight = 4;
            _miniInput = new TextField();
            _miniInput.style.minWidth = 80;
            _miniInput.style.marginTop = -2;
            _miniInput.style.marginBottom = -2;
            _miniInput.RegisterValueChangedCallback(e =>
            {
                if (!_miniDigitsOnly || e.newValue == null) return;
                string filtered = new string(System.Linq.Enumerable
                    .Where(e.newValue, char.IsDigit).ToArray());
                if (filtered != e.newValue) _miniInput.SetValueWithoutNotify(filtered);
            });
            _miniInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    var commit = _miniCommit;
                    string val = _miniInput.value;
                    _miniCancel = null; // committing is not a cancel
                    CloseMiniBuffer();
                    commit?.Invoke(val);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CloseMiniBuffer();
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            // Clicking elsewhere cancels, like emacs quitting the minibuffer.
            _miniInput.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (_miniBuffer.style.display == DisplayStyle.Flex) CloseMiniBuffer();
            });
            _miniBuffer.Add(_miniPrompt);
            _miniBuffer.Add(_miniInput);
            statusBar.Add(_miniBuffer);
        }

        /// <summary>Shows the status-bar prompt; Enter passes the entry to
        /// <paramref name="onCommit"/>, Escape (or focus loss) cancels —
        /// invoking <paramref name="onCancel"/> when one is given. Opening a
        /// prompt while one is showing cancels the first.</summary>
        void StartStatusPrompt(string prompt, bool digitsOnly, System.Action<string> onCommit,
            string initialValue = "", System.Action onCancel = null)
        {
            if (_miniBuffer == null) return;
            if (_miniBuffer.style.display == DisplayStyle.Flex) CloseMiniBuffer();
            _miniPrompt.text = prompt;
            _miniDigitsOnly = digitsOnly;
            _miniCommit = onCommit;
            _miniCancel = onCancel;
            _miniInput.SetValueWithoutNotify(initialValue ?? string.Empty);
            _statusLeft.style.display = DisplayStyle.None;
            _miniBuffer.style.display = DisplayStyle.Flex;
            _miniInput.schedule.Execute(() => _miniInput.Focus()).ExecuteLater(0);
        }

        void CloseMiniBuffer()
        {
            var cancel = _miniCancel;
            _miniCommit = null;
            _miniCancel = null;
            _miniBuffer.style.display = DisplayStyle.None;
            _statusLeft.style.display = DisplayStyle.Flex;
            _code?.schedule.Execute(() => _code.Focus()).ExecuteLater(0);
            cancel?.Invoke(); // after teardown: the handler may open a new prompt
        }

        /// <summary>Goto Line (Ctrl+G): status-bar prompt, numeric only,
        /// clamped to [1, line count]. Works without visible line numbers.</summary>
        void GotoLineCommand()
        {
            if (!CanEditDoc) return;
            StartStatusPrompt(L10n.Tr("Goto Line:"), digitsOnly: true, s =>
            {
                if (!int.TryParse(s, out int line)) return;
                int clamped = Mathf.Clamp(line, 1, _code.LineCount);
                PushNavLocation();
                _code.GoToLine(clamped, 1);
                PostStatus(clamped == line
                    ? string.Format(L10n.Tr("Line {0}."), clamped)
                    : string.Format(L10n.Tr("Line {0} is out of range — went to line {1} (1-{2})."), line, clamped, _code.LineCount));
            });
        }
    }
}
#endif
