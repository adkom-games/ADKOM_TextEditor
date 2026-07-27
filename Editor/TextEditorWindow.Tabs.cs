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
    // Tab strip: rendering, switching, closing, drag-to-reorder, and the tab context menu.
    public partial class TextEditorWindow
    {
        // The document whose undo world currently lives in the code view.
        TextDocument _undoWorldDoc;

        // #10: skip the rebuild when nothing tab-visible changed (RebuildTabs
        // is called liberally on every state change). The signature captures
        // exactly what the strip renders: order, names, dirty stars, active.
        string _tabSignature;

        void InvalidateTabs() => _tabSignature = null;

        void RebuildTabs()
        {
            if (_tabBar == null) return;
            var sigB = new System.Text.StringBuilder();
            for (int i = 0; i < _docs.Count; i++)
                sigB.Append(_docs[i].IsDirty ? '*' : ' ').Append(_docs[i].DisplayName).Append(char.MinValue);
            sigB.Append(_active);
            string sig = sigB.ToString();
            if (sig == _tabSignature) return;
            _tabSignature = sig;
            _tabBar.Clear();
            for (int i = 0; i < _docs.Count; i++)
            {
                int index = i;
                var doc = _docs[i];

                var tab = new VisualElement();
                tab.AddToClassList("tab");
                if (i == _active) tab.AddToClassList("tab--active");
                tab.RegisterCallback<MouseDownEvent>(e =>
                {
                    // Button 0 (switch) is handled in the pointer-drag handler:
                    // switching rebuilds the tab bar, which would destroy the
                    // element holding the pointer capture and kill the drag.
                    if (e.button == 1) ShowTabContextMenu(index); // right-click
                    else if (e.button == 2) CloseTab(index); // middle-click close
                });
                RegisterTabDrag(tab, doc);

                var label = new Label((doc.IsDirty ? "*" : "") + doc.DisplayName)
                {
                    tooltip = doc.HasFile ? doc.FilePath : "New unsaved document"
                };
                tab.Add(label);

                var close = new Button(() => CloseTab(index)) { text = "×" };
                close.AddToClassList("tab__close");
                tab.Add(close);

                _tabBar.Add(tab);
            }
        }

        void RegisterTabDrag(VisualElement tab, TextDocument doc)
        {
            tab.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                // Switch FIRST (this rebuilds the tab bar), then start the
                // drag on the rebuilt element — capturing on `tab` would be
                // useless once RebuildTabs removes it from the hierarchy.
                SwitchTo(_docs.IndexOf(doc));
                int now = _docs.IndexOf(doc);
                if (now < 0 || now >= _tabBar.childCount) return;
                var liveTab = _tabBar[now];
                _dragDoc = doc;
                _dragActive = false;
                _dragStart = e.position;
                liveTab.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            tab.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragDoc == null || !tab.HasPointerCapture(e.pointerId)) return;
                if (!_dragActive)
                {
                    if (Mathf.Abs(e.position.x - _dragStart.x) < DragThreshold &&
                        Mathf.Abs(e.position.y - _dragStart.y) < DragThreshold) return;
                    _dragActive = true;
                    tab.AddToClassList("tab--dragging");
                }
                int from = _docs.IndexOf(_dragDoc);
                int to = TabIndexAt(e.position.x, from);
                if (to != from && to >= 0 && from >= 0)
                {
                    _docs.RemoveAt(from);
                    _docs.Insert(to, _dragDoc);
                    // MouseDown already switched to the dragged tab, so the
                    // active document IS the dragged one — track its new slot.
                    _active = to;
                    RebuildTabs();
                    // The rebuilt tab under the pointer continues the drag.
                    var newTab = _tabBar[to];
                    newTab.CapturePointer(e.pointerId);
                    newTab.AddToClassList("tab--dragging");
                }
            });
            tab.RegisterCallback<PointerUpEvent>(e =>
            {
                if (tab.HasPointerCapture(e.pointerId)) tab.ReleasePointer(e.pointerId);
                tab.RemoveFromClassList("tab--dragging");
                bool wasDrag = _dragActive;
                _dragDoc = null;
                _dragActive = false;
                if (wasDrag) { InvalidateTabs(); RebuildTabs(); } // clean styling
            });
            tab.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                tab.RemoveFromClassList("tab--dragging");
            });
        }

        /// <summary>Target slot for the dragged tab: crossing another tab's
        /// horizontal midpoint claims that tab's index (it shifts aside);
        /// otherwise the tab stays at <paramref name="current"/>.</summary>
        int TabIndexAt(float worldX, int current)
        {
            for (int i = 0; i < _tabBar.childCount; i++)
            {
                if (i == current) continue;
                var r = _tabBar[i].worldBound;
                if (worldX < r.xMin || worldX > r.xMax) continue;
                bool pastMidpoint = i < current ? worldX < r.center.x : worldX > r.center.x;
                if (pastMidpoint) return i;
            }
            return current;
        }

        void ShowTabContextMenu(int index)
        {
            if (index < 0 || index >= _docs.Count) return;
            var doc = _docs[index];
            var m = new GenericMenu();
            if (!doc.IsSettings)
            {
                m.AddItem(new GUIContent(L10n.Tr("Save")), false, () => SaveTabAt(index, saveAs: false));
                m.AddItem(new GUIContent(L10n.Tr("Save As...")), false, () => SaveTabAt(index, saveAs: true));
            }
            else
            {
                m.AddDisabledItem(new GUIContent(L10n.Tr("Save")));
                m.AddDisabledItem(new GUIContent(L10n.Tr("Save As...")));
            }
            m.AddSeparator("");
            m.AddItem(new GUIContent(L10n.Tr("Close")), false, () => CloseTab(index));
            if (_docs.Count > 1)
                m.AddItem(new GUIContent(L10n.Tr("Close Other Tabs")), false, () => CloseOtherTabs(index));
            else
                m.AddDisabledItem(new GUIContent(L10n.Tr("Close Other Tabs")));
            m.ShowAsContext();
        }

        void SaveTabAt(int index, bool saveAs)
        {
            if (index < 0 || index >= _docs.Count || _docs[index].IsSettings) return;
            var doc = _docs[index]; // background docs' Content is kept in sync
            bool saved = saveAs ? FileService.SaveAs(doc) : FileService.Save(doc);
            if (saved)
            {
                if (index == _active) RefreshFormatter(); // Save As can change language
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
            }
        }

        /// <summary>Closes every tab except <paramref name="keep"/>. Clean
        /// tabs close immediately; dirty ones raise one non-modal banner for
        /// the whole batch (Save All / Discard All / Cancel).</summary>
        void CloseOtherTabs(int keep)
        {
            var keepDoc = keep >= 0 && keep < _docs.Count ? _docs[keep] : null;
            var dirty = new List<TextDocument>();
            for (int i = _docs.Count - 1; i >= 0; i--)
            {
                if (_docs[i] == keepDoc) continue;
                if (_docs[i].IsDirty && !_docs[i].IsSettings) { dirty.Add(_docs[i]); continue; }
                _docs.RemoveAt(i);
            }
            _active = Mathf.Clamp(_docs.IndexOf(keepDoc), 0, _docs.Count - 1);
            SwitchTo(_active);
            if (dirty.Count == 0) return;

            void CloseAll(bool save)
            {
                HideBanner();
                foreach (var d in dirty)
                {
                    if (save && !FileService.Save(d)) continue; // cancelled Save As keeps the tab
                    CloseTabForce(d);
                }
            }
            ShowBanner(dirty.Count == 1
                    ? string.Format(L10n.Tr("'{0}' has unsaved changes."), dirty[0].DisplayName)
                    : string.Format(L10n.Tr("{0} tabs have unsaved changes."), dirty.Count),
                (L10n.Tr("Save All"), () => CloseAll(true)),
                (L10n.Tr("Discard All"), () => CloseAll(false)),
                (L10n.Tr("Cancel"), HideBanner));
        }

        void SwitchTo(int index)
        {
            if (!HasDocs)
            {
                _active = 0;
                if (_editorArea != null) _editorArea.style.display = DisplayStyle.Flex;
                if (_settingsScroll != null) _settingsScroll.style.display = DisplayStyle.None;
                if (_code != null) { _code.SetValueWithoutNotify(string.Empty); _code.style.display = DisplayStyle.None; }
                if (_mdView != null) _mdView.style.display = DisplayStyle.None;
                if (_mdToggle != null) _mdToggle.style.display = DisplayStyle.None;
                if (_mdFormatBar != null) _mdFormatBar.style.display = DisplayStyle.None;
                if (_emptyHint != null) _emptyHint.style.display = DisplayStyle.Flex;
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
                Scripting.AteApi.NotifyActiveChanged(this, null);
                return;
            }
            if (_code != null) _code.style.display = DisplayStyle.Flex;
            if (_emptyHint != null) _emptyHint.style.display = DisplayStyle.None;

            EnsureDocs();
            _active = Mathf.Clamp(index, 0, _docs.Count - 1);

            bool settings = Active.IsSettings;
            if (_editorArea != null)
                _editorArea.style.display = settings ? DisplayStyle.None : DisplayStyle.Flex;
            if (_settingsPane != null)
                _settingsScroll.style.display = settings ? DisplayStyle.Flex : DisplayStyle.None;
            if (settings)
            {
                if (_mdView != null) _mdView.style.display = DisplayStyle.None;
                if (_mdToggle != null) _mdToggle.style.display = DisplayStyle.None;
                if (_mdFormatBar != null) _mdFormatBar.style.display = DisplayStyle.None;
                SyncSettingsControls();
                RebuildTabs();
                UpdateTitle();
                UpdateStatus();
                return;
            }

            CheckExternalChange(Active);
            // Undo history is per document: park the outgoing document's undo
            // world and attach the incoming one (fixes the latent cross-tab
            // undo bleed the old full-snapshot model had).
            if (_code != null && !ReferenceEquals(_undoWorldDoc, Active))
            {
                if (_undoWorldDoc != null) _undoWorldDoc.UndoWorld = _code.DetachUndoWorld();
                _code.AttachUndoWorld(Active.UndoWorld);
                _undoWorldDoc = Active;
            }
            _code?.SetValueWithoutNotify(Active.Content);
            RefreshFormatter();
            UpdateMdUi();
            RebuildTabs();
            UpdateTitle();
            UpdateStatus();
            // Ready for typing immediately (New, Open, tab click). Deferred a
            // frame so the view is laid out/visible before taking focus.
            if (!(ActiveIsMarkdown && Active.MdRendered))
                _code?.schedule.Execute(() => _code.Focus()).ExecuteLater(0);
            Scripting.AteApi.NotifyActiveChanged(this, Active);
        }

        void CloseTabForce(TextDocument doc)
        {
            int index = _docs.IndexOf(doc);
            if (index < 0) return;
            _docs.RemoveAt(index);
            Scripting.AteApi.NotifyClosed(this, doc);
            if (index < _active || _active >= _docs.Count)
                _active = Mathf.Max(0, _active - 1);
            SwitchTo(_active); // handles the now-empty case without auto-Untitled
        }

        void CloseTab(int index)
        {
            if (index < 0 || index >= _docs.Count) return;
            var doc = _docs[index];
            if (doc.IsDirty && !doc.IsSettings)
            {
                // Non-modal unsaved-changes prompt (the old modal froze
                // Unity's main loop). Navigating away cancels implicitly.
                if (index != _active) SwitchTo(index);
                ShowBanner(string.Format(L10n.Tr("'{0}' has unsaved changes."), doc.DisplayName),
                    (L10n.Tr("Save"), () => { HideBanner(); if (FileService.Save(doc)) CloseTabForce(doc); }),
                    (L10n.Tr("Discard"), () => { HideBanner(); CloseTabForce(doc); }),
                    (L10n.Tr("Cancel"), HideBanner));
                return;
            }
            CloseTabForce(doc);
        }

        void StepTab(int dir)
        {
            if (!HasDocs) return;
            SwitchTo((_active + dir + _docs.Count) % _docs.Count);
        }
    }
}
#endif
