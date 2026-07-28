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

        // Single-line tab strip (Defects round 2026-07-27, item 1): tabs live
        // in a clipped viewport; arrows scroll it when tabs overflow, and the
        // tab-list dropdown stays pinned at the far right.
        VisualElement _tabViewport, _tabStrip;
        Button _tabLeftBtn, _tabRightBtn;
        float _tabScrollOffset;

        void EnsureTabChrome()
        {
            if (_tabViewport != null && _tabBar.Contains(_tabViewport)) return;
            _tabBar.Clear();

            _tabLeftBtn = new Button(() => ScrollTabsBy(-160f)) { text = "\u25C2" };
            _tabLeftBtn.AddToClassList("tab-scroll-btn");
            _tabLeftBtn.tooltip = L10n.Tr("Scroll tabs left");
            _tabBar.Add(_tabLeftBtn);

            _tabViewport = new VisualElement { name = "tab-viewport" };
            _tabViewport.style.flexGrow = 1;
            _tabViewport.style.flexShrink = 1;
            _tabViewport.style.flexDirection = FlexDirection.Row;
            _tabViewport.style.overflow = Overflow.Hidden;
            _tabStrip = new VisualElement { name = "tab-strip" };
            _tabStrip.style.flexDirection = FlexDirection.Row;
            _tabStrip.style.flexShrink = 0;
            _tabViewport.Add(_tabStrip);
            _tabViewport.RegisterCallback<GeometryChangedEvent>(_ => ClampTabScroll());
            _tabStrip.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // EnsureActiveTabVisible owns the pending flag: it clears it
                // only once it actually read a resolved layout (issue #9 —
                // clearing it here lost the retry when children had no
                // layout yet, leaving the selected tab off-screen).
                if (_tabEnsureActivePending) EnsureActiveTabVisible();
                else ClampTabScroll();
            });
            _tabBar.Add(_tabViewport);

            _tabRightBtn = new Button(() => ScrollTabsBy(160f)) { text = "\u25B8" };
            _tabRightBtn.AddToClassList("tab-scroll-btn");
            _tabRightBtn.tooltip = L10n.Tr("Scroll tabs right");
            _tabBar.Add(_tabRightBtn);

            var listBtn = new Button(() => BuildTabListMenu().DropDown(_tabListBtnRect))
            {
                text = "\u25BE",
                tooltip = L10n.Tr("Open Tabs")
            };
            listBtn.AddToClassList("tab-list-btn");
            listBtn.RegisterCallback<GeometryChangedEvent>(_ => _tabListBtnRect = listBtn.worldBound);
            _tabBar.Add(listBtn);
        }

        void ScrollTabsBy(float delta)
        {
            _tabScrollOffset += delta;
            ClampTabScroll();
        }

        void ClampTabScroll()
        {
            if (_tabViewport == null || _tabStrip == null) return;
            float vw = _tabViewport.contentRect.width;
            float cw = _tabStrip.contentRect.width;
            if (float.IsNaN(vw) || float.IsNaN(cw)) return;
            float max = Mathf.Max(0f, cw - vw);
            _tabScrollOffset = Mathf.Clamp(_tabScrollOffset, 0f, max);
            _tabStrip.style.translate = new Translate(-_tabScrollOffset, 0f);
            bool overflow = max > 0.5f;
            _tabLeftBtn.style.display = _tabRightBtn.style.display =
                overflow ? DisplayStyle.Flex : DisplayStyle.None;
            _tabLeftBtn.SetEnabled(_tabScrollOffset > 0.5f);
            _tabRightBtn.SetEnabled(_tabScrollOffset < max - 0.5f);
        }

        /// <summary>Scrolls the strip so the active tab is fully in view.</summary>
        void EnsureActiveTabVisible()
        {
            if (_tabStrip == null || _active < 0 || _active >= _tabStrip.childCount)
            { _tabEnsureActivePending = false; return; } // nothing to scroll to
            var r = _tabStrip[_active].layout;
            float vw = _tabViewport.contentRect.width;
            // No layout yet: stay pending so the retry ticker / next
            // GeometryChanged tries again. An un-laid-out tab reports
            // x = 0 with width = NaN (issue #9): x alone passes the guard,
            // NaN xMax makes the scroll-right test always false, and the
            // bogus x=0 drags the strip fully LEFT — so width must be
            // checked too or the flag clears on garbage.
            if (float.IsNaN(r.x) || float.IsNaN(r.width) || r.width <= 0 ||
                float.IsNaN(vw) || vw <= 0) return;
            _tabEnsureActivePending = false;
            if (r.xMax - _tabScrollOffset > vw) _tabScrollOffset = r.xMax - vw;
            if (r.x < _tabScrollOffset) _tabScrollOffset = r.x;
            ClampTabScroll();
        }

        void RebuildTabs()
        {
            if (_tabBar == null) return;
            Color baseColor = EditorConfig.TabColor;
            var sigB = new System.Text.StringBuilder();
            for (int i = 0; i < _docs.Count; i++)
                sigB.Append(_docs[i].IsDirty ? '*' : ' ').Append(_docs[i].DisplayName).Append(char.MinValue);
            sigB.Append(_active).Append('#').Append(ColorUtility.ToHtmlStringRGB(baseColor));
            string sig = sigB.ToString();
            if (sig == _tabSignature) return;
            _tabSignature = sig;
            EnsureTabChrome();
            _tabStrip.Clear();
            for (int i = 0; i < _docs.Count; i++)
            {
                int index = i;
                var doc = _docs[i];

                var tab = new VisualElement();
                tab.AddToClassList("tab");
                if (i == _active) tab.AddToClassList("tab--active");
                tab.style.backgroundColor = TabShade(baseColor, doc, i == _active);
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

                _tabStrip.Add(tab);
            }
            // The freshly built tabs have no layout yet. GeometryChanged only
            // fires when the strip's SIZE changes — switching the active tab
            // rebuilds the same doc set at the same width, so it may never
            // fire (issue #9). A short bounded ticker retries until the new
            // children have layout and the scroll actually happened.
            _tabEnsureActivePending = true;
            rootVisualElement.schedule.Execute(EnsureActiveTabVisible)
                .Every(16)
                .Until(() => !_tabEnsureActivePending);
        }

        bool _tabEnsureActivePending;

        Rect _tabListBtnRect;

        /// <summary>Every open tab, dirty-starred, active checked; picking
        /// one jumps to it. Used by the strip's dropdown button.</summary>
        internal GenericMenu BuildTabListMenu()
        {
            var m = new GenericMenu();
            if (_docs.Count == 0)
            {
                m.AddDisabledItem(new GUIContent(L10n.Tr("(no open tabs)")));
                return m;
            }
            for (int i = 0; i < _docs.Count; i++)
            {
                int idx = i;
                string name = (_docs[i].IsDirty ? "*" : "") +
                              _docs[i].DisplayName.Replace('/', '∕');
                m.AddItem(new GUIContent($"{i + 1}  {name}"), i == _active, () => SwitchTo(idx));
            }
            return m;
        }

        /// <summary>The settings tab color, uniform for every tab (per-doc
        /// shade variation was tried and retired 2026-07-27 — too busy). The
        /// active tab is brighter and fully opaque so it pops; the accent top
        /// border comes from the .tab--active USS rule.</summary>
        static Color TabShade(Color baseColor, TextDocument doc, bool active)
        {
            var c = baseColor;
            if (active) { c = Color.Lerp(c, Color.white, 0.25f); c.a = 1f; }
            else c.a = 0.45f;
            return c;
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
                if (now < 0 || now >= _tabStrip.childCount) return;
                var liveTab = _tabStrip[now];
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
                    var newTab = _tabStrip[to];
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
            int n = Mathf.Min(_tabStrip.childCount, _docs.Count);
            for (int i = 0; i < n; i++)
            {
                if (i == current) continue;
                var r = _tabStrip[i].worldBound;
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
            // Game state is per document, like the undo world (AteApi 1.1).
            if (_code != null)
            {
                _code.gameMode = Active.GameMode;
                _code.wordWrap = Active.GameMode ? false : _wordWrap;
                _code.AttachOverlay(Active.Overlay);
                _code.SetFontOverride(Active.FontName, Active.FontSize);
                ApplyViewChrome();
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
