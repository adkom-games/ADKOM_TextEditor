#if UNITY_EDITOR
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// ATE's shared cursor-following tooltip. Unity's native editor tooltip
    /// anchors to the hovered ELEMENT's box (far from the pointer on tall
    /// elements) and its style is engine-owned. For every root attached
    /// here, the TooltipEvent is intercepted BEFORE the hovered element can
    /// fill it — so the native popup never shows — and a styled label
    /// appears just off the pointer with the same text, following the mouse
    /// while the hover lasts. Elements keep declaring plain
    /// <c>tooltip</c> strings; nothing changes at the declaration sites.
    /// </summary>
    internal static class AteTooltip
    {
        internal const float OffsetX = 8f, OffsetY = 12f;

        static readonly ConditionalWeakTable<VisualElement, Runtime> _attached =
            new ConditionalWeakTable<VisualElement, Runtime>();

        sealed class Runtime
        {
            public Label Tip;
            public Vector2 Pointer;
            public string Text;
        }

        /// <summary>Enables the shared tooltip for everything under
        /// <paramref name="root"/>. Idempotent — window rebuilds that Clear()
        /// the root just call it again (the callbacks live on the root and
        /// survive; only the tip label is re-added).</summary>
        public static void Attach(VisualElement root)
        {
            if (_attached.TryGetValue(root, out var existing))
            {
                if (existing.Tip.parent != root)
                {
                    existing.Tip.style.display = DisplayStyle.None;
                    root.Add(existing.Tip);
                }
                return;
            }
            var rt = new Runtime { Tip = MakeTip() };
            _attached.Add(root, rt);
            root.Add(rt.Tip);

            root.RegisterCallback<TooltipEvent>(e =>
            {
                // Stopping the event here starves the native path: the
                // hovered element's own handler (which would fill
                // evt.tooltip for the editor to display) never runs.
                string text = TooltipOf(e.target as VisualElement);
                e.StopImmediatePropagation();
                if (string.IsNullOrEmpty(text)) { Hide(rt); return; }
                rt.Text = text;
                rt.Tip.text = text;
                rt.Tip.style.display = DisplayStyle.Flex;
                Place(root, rt);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                rt.Pointer = e.position;
                if (rt.Tip.style.display != DisplayStyle.Flex) return;
                // Moving within the same tooltip source drags the tip along;
                // crossing onto anything else hides it (a fresh TooltipEvent
                // re-shows it there after the editor's usual hover delay).
                if (TooltipOf(e.target as VisualElement) != rt.Text) { Hide(rt); return; }
                Place(root, rt);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_ => Hide(rt), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => Hide(rt));
        }

        /// <summary>The tooltip look, shared with hand-driven tips (the
        /// rendered-Markdown link tip) so every tooltip in ATE matches.</summary>
        internal static Label MakeTip()
        {
            var border = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            return new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, display = DisplayStyle.None,
                    backgroundColor = new Color(0.14f, 0.14f, 0.14f, 0.97f),
                    color = new Color(0.85f, 0.85f, 0.85f),
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = border, borderBottomColor = border,
                    borderLeftColor = border, borderRightColor = border,
                    paddingLeft = 6, paddingRight = 6, paddingTop = 3, paddingBottom = 3,
                    fontSize = 11, whiteSpace = WhiteSpace.Normal, maxWidth = 480
                }
            };
        }

        /// <summary>Places a tip label just below-right of a panel-space
        /// pointer position, pulled back inside <paramref name="host"/> at
        /// the right edge and flipped above the pointer at the bottom.</summary>
        internal static void Place(VisualElement host, Label tip, Vector2 panelPos)
        {
            Vector2 local = host.WorldToLocal(panelPos);
            float x = local.x + OffsetX, y = local.y + OffsetY;
            float w = tip.resolvedStyle.width, h = tip.resolvedStyle.height;
            if (!float.IsNaN(w) && w > 0 && x + w > host.layout.width) x = Mathf.Max(0, host.layout.width - w - 2);
            if (!float.IsNaN(h) && h > 0 && y + h > host.layout.height) y = Mathf.Max(0, local.y - h - 6);
            tip.style.left = x;
            tip.style.top = y;
        }

        static void Place(VisualElement root, Runtime rt) => Place(root, rt.Tip, rt.Pointer);

        static void Hide(Runtime rt)
        {
            rt.Text = null;
            rt.Tip.style.display = DisplayStyle.None;
        }

        static string TooltipOf(VisualElement v)
        {
            for (; v != null; v = v.parent)
                if (!string.IsNullOrEmpty(v.tooltip)) return v.tooltip;
            return null;
        }
    }
}
#endif
