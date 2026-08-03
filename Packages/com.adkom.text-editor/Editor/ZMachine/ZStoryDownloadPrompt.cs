#if UNITY_EDITOR
// Informed consent before ATE fetches anything from the internet: what is
// being downloaded, from where, at which pinned commit, under which license,
// and where it lands on disk. Deliberately a utility window rather than a
// modal dialog — ATE's prompts never freeze the Unity editor — and shown
// every time, because a remembered consent is not a consent you can point to.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AteZMachine
{
    public sealed class ZStoryDownloadPrompt : EditorWindow
    {
        ZStory.Game _game;
        Action<string> _onReady;
        Label _status;
        Button _download;

        /// <summary>Asks about <paramref name="game"/>; on confirmation
        /// downloads it and calls <paramref name="onReady"/> with the local
        /// path. Nothing happens at all if the user cancels or closes it.</summary>
        public static void Open(ZStory.Game game, Action<string> onReady)
        {
            var w = CreateInstance<ZStoryDownloadPrompt>();
            w._game = game;
            w._onReady = onReady;
            w.titleContent = new GUIContent("ADKOM Text Editor — Download Story File");
            w.minSize = w.maxSize = new Vector2(640, 380);
            w.ShowUtility();
            w.BuildUI();
        }

        void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 14;
            root.style.paddingRight = 14;
            root.style.paddingTop = 12;

            var title = new Label(string.Format(L10n.Tr("Download {0}?"), _game.Title));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            root.Add(title);

            var blurb = new Label(L10n.Tr(
                "This story file is not part of the ADKOM Text Editor. It is downloaded from GitHub to your computer, outside the Unity project, and is never imported as an asset."));
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.marginTop = 6;
            blurb.style.opacity = 0.85f;
            root.Add(blurb);

            var table = new VisualElement();
            table.style.marginTop = 10;
            root.Add(table);

            table.Add(LinkRow(L10n.Tr("Source repository"), _game.Repo, _game.RepoUrl,
                L10n.Tr("Open the source repository on GitHub — source, history and its license.")));
            table.Add(LinkRow(L10n.Tr("Story file"), "COMPILED/" + _game.File, _game.FileUrl,
                L10n.Tr("View this story file on GitHub, at the exact commit it is fetched from."),
                CopyButton(_game.Url, L10n.Tr("Copy the exact URL this download fetches (the raw file link) to the clipboard."))));
            table.Add(TextRow(L10n.Tr("Pinned commit"), _game.ShortCommit,
                L10n.Tr("The download is pinned to this commit, so its contents cannot change.")));
            table.Add(TextRow(L10n.Tr("License"), "MIT",
                L10n.Tr("These three Zork titles are the only MIT-licensed Infocom games in that collection.")));
            table.Add(TextRow(L10n.Tr("Size"), string.Format("{0:n0} KB", Math.Round(_game.Bytes / 1024.0)),
                L10n.Tr("The exact size at the pinned commit. A download of any other size is rejected.")));
            table.Add(TextRow(L10n.Tr("SHA-256"), _game.Sha256,
                L10n.Tr("The fingerprint of the file at the pinned commit. What arrives is hashed and must match this exactly, or it is deleted instead of played.")));
            table.Add(TextRow(L10n.Tr("Saves to"), ZStory.LocalPath(_game),
                L10n.Tr("Where the file is written. Delete it any time; nothing is stored in your project.")));

            _status = new Label();
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 10;
            _status.style.minHeight = 28;
            root.Add(_status);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 4;
            var cancel = new Button(Close)
            {
                text = L10n.Tr("Cancel"),
                tooltip = L10n.Tr("Close without downloading anything.")
            };
            _download = new Button(StartDownload)
            {
                text = L10n.Tr("Download"),
                tooltip = L10n.Tr("Download the story file now and start the game.")
            };
            buttons.Add(cancel);
            buttons.Add(_download);
            root.Add(buttons);
        }

        static VisualElement Row(string label, VisualElement value, string tooltip, VisualElement trailing = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;
            row.tooltip = tooltip;
            var l = new Label(label) { tooltip = tooltip };
            l.style.width = 120;
            l.style.opacity = 0.7f;
            l.style.flexShrink = 0;
            row.Add(l);
            value.style.flexShrink = 1;
            value.style.flexGrow = 1;
            row.Add(value);
            if (trailing != null)
            {
                trailing.style.flexShrink = 0;
                row.Add(trailing);
            }
            return row;
        }

        /// <summary>A "Copy Link to Clipboard" button for the row it sits on.
        /// It copies the raw download URL — the exact address the transfer
        /// uses — so it can be checked, fetched or archived independently.</summary>
        Button CopyButton(string url, string tooltip)
        {
            return new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = url;
                if (_status == null) return;
                _status.text = L10n.Tr("Link copied to the clipboard.");
                _status.style.color = StyleKeyword.Null;
            })
            { text = L10n.Tr("Copy Link to Clipboard"), tooltip = tooltip };
        }

        static VisualElement TextRow(string label, string text, string tooltip)
        {
            // Selectable so a cautious user can copy the commit or the path.
            var v = new TextField { value = text, isReadOnly = true, tooltip = tooltip };
            v.style.marginLeft = 0;
            v.style.backgroundColor = Color.clear;
            v.style.borderLeftWidth = v.style.borderRightWidth = 0;
            v.style.borderTopWidth = v.style.borderBottomWidth = 0;
            return Row(label, v, tooltip);
        }

        static VisualElement LinkRow(string label, string text, string url, string tooltip, VisualElement trailing = null)
        {
            var b = new Button(() => Application.OpenURL(url)) { text = text, tooltip = tooltip };
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.marginLeft = 0;
            b.style.paddingLeft = 2;
            b.style.backgroundColor = Color.clear;
            b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            b.style.borderTopWidth = b.style.borderBottomWidth = 0;
            b.style.color = new Color(0.34f, 0.61f, 0.84f); // ATE link blue
            return Row(label, b, tooltip, trailing);
        }

        void StartDownload()
        {
            _download.SetEnabled(false);
            _status.text = L10n.Tr("Downloading…");
            _status.style.color = StyleKeyword.Null;
            // Let the status paint before the (blocking) transfer starts.
            rootVisualElement.schedule.Execute(RunDownload).ExecuteLater(60);
        }

        void RunDownload()
        {
            string path = ZStory.EnsureDownloaded(_game, out string error);
            if (path == null)
            {
                _status.text = string.Format(L10n.Tr("Download failed: {0}"), error);
                _status.style.color = new Color(0.9f, 0.45f, 0.4f);
                _download.SetEnabled(true);
                return;
            }
            var ready = _onReady;
            _onReady = null;
            Close();
            ready?.Invoke(path);
        }
    }
}
#endif
