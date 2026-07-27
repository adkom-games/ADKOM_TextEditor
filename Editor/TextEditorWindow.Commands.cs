#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ADKOM.TextEditor
{
    // THE COMMAND TABLE — the single source of truth for keyboard commands
    // (defect #7). Each command defines, in ONE place: its per-keymap
    // bindings, the handler, and the display string the menus show. The
    // dispatchers (OnGlobalKeyDown / OnKeyDown) and every menu label consult
    // this table, so bindings and labels can never drift apart again.
    // Commands with Run == null are display-only: their keys are handled
    // elsewhere (CodeView primitives like undo/clipboard, or the Tab-key
    // special case) and the table only supplies the menu hint.
    public partial class TextEditorWindow
    {
        enum CmdScope { Global, Editor }

        sealed class Key
        {
            public KeyCode Code;
            public bool Ctrl, Shift, Alt;
            public bool LooseShift, LooseAlt; // modifier ignored when matching
            public bool ShiftArg;             // Shift is the handler's argument, not a match criterion
            public string Display;            // menu hint; null on secondary bindings

            public bool Matches(KeyDownEvent e, bool ctrl)
                => MatchesRaw(this, e.keyCode, ctrl, e.shiftKey, e.altKey);
        }

        sealed class AteCommand
        {
            public string Id;
            public CmdScope Scope;
            public Key[] VS, VSCode, Rider;
            public Func<bool, bool> Run;      // (shift) => handled; null = display-only

            public Key[] For(KeymapLayout k) => k switch
            {
                KeymapLayout.VSCode => VSCode,
                KeymapLayout.Rider => Rider,
                _ => VS
            };
        }

        static Key K(KeyCode code, bool ctrl = false, bool shift = false, bool alt = false,
            string d = null, bool shiftArg = false, bool looseShift = false, bool looseAlt = false)
            => new Key { Code = code, Ctrl = ctrl, Shift = shift, Alt = alt,
                         Display = d, ShiftArg = shiftArg, LooseShift = looseShift, LooseAlt = looseAlt };

        List<AteCommand> _commands;

        void BuildCommands()
        {
            // Shared binding arrays (same keys in every layout).
            Key[] All(params Key[] k) => k;

            bool True(Action a) { a(); return true; }

            var find = All(K(KeyCode.F, ctrl: true, d: "Ctrl+F", shiftArg: true));
            var comment = All(K(KeyCode.Slash, ctrl: true, d: "Ctrl+/"));
            var gotoLine = All(K(KeyCode.G, ctrl: true, d: "Ctrl+G"));
            var findNext = All(K(KeyCode.F3, d: "F3", shiftArg: true));

            _commands = new List<AteCommand>
            {
                // ---- Global scope ----
                new AteCommand { Id = "find", Scope = CmdScope.Global, VS = find, VSCode = find, Rider = find,
                    Run = shift => True(() => FindReplaceWindow.Open(this, replaceFocus: false, allTabs: shift)) },
                new AteCommand { Id = "replace", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.H, ctrl: true, d: "Ctrl+H", shiftArg: true)),
                    VSCode = All(K(KeyCode.H, ctrl: true, d: "Ctrl+H", shiftArg: true)),
                    Rider = All(K(KeyCode.R, ctrl: true, d: "Ctrl+R", shiftArg: true)),
                    Run = shift => True(() => FindReplaceWindow.Open(this, replaceFocus: true, allTabs: shift)) },
                new AteCommand { Id = "goto-line", Scope = CmdScope.Global, VS = gotoLine, VSCode = gotoLine, Rider = gotoLine,
                    Run = _ => True(GotoLineCommand) },
                new AteCommand { Id = "find-next", Scope = CmdScope.Global, VS = findNext, VSCode = findNext, Rider = findNext,
                    Run = shift => FindReplaceWindow.FindAgain(this, reverse: shift) },
                new AteCommand { Id = "save", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.S, ctrl: true, d: "Ctrl+S")),
                    VSCode = All(K(KeyCode.S, ctrl: true, d: "Ctrl+S")),
                    Rider = null,
                    Run = _ => True(() => SaveFile(false)) },
                new AteCommand { Id = "save-all", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.S, ctrl: true, shift: true, d: "Ctrl+Shift+S")),
                    VSCode = null,
                    Rider = All(K(KeyCode.S, ctrl: true, d: "Ctrl+S")),
                    Run = _ => True(SaveAll) },
                new AteCommand { Id = "new-file", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.N, ctrl: true, d: "Ctrl+N", looseAlt: true)),
                    VSCode = All(K(KeyCode.N, ctrl: true, d: "Ctrl+N", looseAlt: true)),
                    Rider = null,
                    Run = _ => True(NewFile) },
                new AteCommand { Id = "open-file", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.O, ctrl: true, d: "Ctrl+O", looseAlt: true)),
                    VSCode = All(K(KeyCode.O, ctrl: true, d: "Ctrl+O", looseAlt: true)),
                    Rider = null,
                    Run = _ => True(OpenFile) },
                new AteCommand { Id = "close-tab", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.F4, ctrl: true, d: "Ctrl+F4", looseShift: true, looseAlt: true)),
                    VSCode = All(K(KeyCode.W, ctrl: true, d: "Ctrl+W", looseShift: true, looseAlt: true),
                                 K(KeyCode.F4, ctrl: true, looseShift: true, looseAlt: true)),
                    Rider = All(K(KeyCode.F4, ctrl: true, d: "Ctrl+F4", looseShift: true, looseAlt: true)),
                    Run = _ => True(() => CloseTab(_active)) },
                new AteCommand { Id = "next-tab", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.Tab, ctrl: true, d: "Ctrl+Tab")),
                    VSCode = All(K(KeyCode.PageDown, ctrl: true, d: "Ctrl+PgDn", looseShift: true, looseAlt: true),
                                 K(KeyCode.Tab, ctrl: true)),
                    Rider = All(K(KeyCode.RightArrow, alt: true, d: "Alt+Right", looseShift: true)),
                    Run = _ => True(() => StepTab(1)) },
                new AteCommand { Id = "prev-tab", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.Tab, ctrl: true, shift: true, d: "Ctrl+Shift+Tab")),
                    VSCode = All(K(KeyCode.PageUp, ctrl: true, d: "Ctrl+PgUp", looseShift: true, looseAlt: true),
                                 K(KeyCode.Tab, ctrl: true, shift: true)),
                    Rider = All(K(KeyCode.LeftArrow, alt: true, d: "Alt+Left", looseShift: true)),
                    Run = _ => True(() => StepTab(-1)) },
                new AteCommand { Id = "settings", Scope = CmdScope.Global,
                    VS = null,
                    VSCode = All(K(KeyCode.Comma, ctrl: true, d: "Ctrl+,", looseShift: true, looseAlt: true)),
                    Rider = All(K(KeyCode.S, ctrl: true, alt: true, d: "Ctrl+Alt+S", looseShift: true)),
                    Run = _ => True(OpenSettings) },

                // ---- Editor scope (require an editable document) ----
                new AteCommand { Id = "duplicate-line", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.D, ctrl: true, d: "Ctrl+D")),
                    VSCode = All(K(KeyCode.DownArrow, shift: true, alt: true, d: "Shift+Alt+Down"),
                                 K(KeyCode.UpArrow, shift: true, alt: true)),
                    Rider = All(K(KeyCode.D, ctrl: true, d: "Ctrl+D")),
                    Run = _ => True(DuplicateLine) },
                new AteCommand { Id = "delete-line", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.L, ctrl: true, d: "Ctrl+L", looseAlt: true)),
                    VSCode = All(K(KeyCode.K, ctrl: true, shift: true, d: "Ctrl+Shift+K", looseAlt: true)),
                    Rider = All(K(KeyCode.Y, ctrl: true, d: "Ctrl+Y", looseAlt: true)),
                    Run = _ => True(DeleteLine) },
                new AteCommand { Id = "move-line-up", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.UpArrow, alt: true, d: "Alt+Up")),
                    VSCode = All(K(KeyCode.UpArrow, alt: true, d: "Alt+Up")),
                    Rider = All(K(KeyCode.UpArrow, alt: true, shift: true, d: "Alt+Shift+Up")),
                    Run = _ => True(() => MoveLine(-1)) },
                new AteCommand { Id = "move-line-down", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.DownArrow, alt: true, d: "Alt+Down")),
                    VSCode = All(K(KeyCode.DownArrow, alt: true, d: "Alt+Down")),
                    Rider = All(K(KeyCode.DownArrow, alt: true, shift: true, d: "Alt+Shift+Down")),
                    Run = _ => True(() => MoveLine(1)) },
                new AteCommand { Id = "toggle-comment", Scope = CmdScope.Editor, VS = comment, VSCode = comment, Rider = comment,
                    Run = _ => True(ToggleComment) },
                new AteCommand { Id = "goto-definition", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.F12, d: "F12")),
                    VSCode = All(K(KeyCode.F12, d: "F12")),
                    Rider = All(K(KeyCode.B, ctrl: true, d: "Ctrl+B")),
                    Run = _ => True(() =>
                    {
                        _code.IndexToLineCol(_code.cursorIndex, out int nl, out int ncol);
                        NavigateToDefinition(nl, ncol);
                    }) },

                new AteCommand { Id = "insert-line-below", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.Return, ctrl: true, shift: true, d: "Ctrl+Shift+Enter"),
                             K(KeyCode.KeypadEnter, ctrl: true, shift: true)),
                    VSCode = All(K(KeyCode.Return, ctrl: true, d: "Ctrl+Enter"),
                                 K(KeyCode.KeypadEnter, ctrl: true)),
                    Rider = All(K(KeyCode.Return, shift: true, d: "Shift+Enter"),
                                K(KeyCode.KeypadEnter, shift: true)),
                    Run = _ => True(() => _code.InsertLineBelow()) },
                new AteCommand { Id = "insert-line-above", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.Return, ctrl: true, d: "Ctrl+Enter"),
                             K(KeyCode.KeypadEnter, ctrl: true)),
                    VSCode = All(K(KeyCode.Return, ctrl: true, shift: true, d: "Ctrl+Shift+Enter"),
                                 K(KeyCode.KeypadEnter, ctrl: true, shift: true)),
                    Rider = All(K(KeyCode.Return, ctrl: true, alt: true, d: "Ctrl+Alt+Enter"),
                                K(KeyCode.KeypadEnter, ctrl: true, alt: true)),
                    Run = _ => True(() => _code.InsertLineAbove()) },
                new AteCommand { Id = "join-lines", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.J, ctrl: true, d: "Ctrl+J")),
                    VSCode = All(K(KeyCode.J, ctrl: true, d: "Ctrl+J")),
                    Rider = All(K(KeyCode.J, ctrl: true, d: "Ctrl+J")),
                    Run = _ => True(() => _code.JoinLines()) },
                new AteCommand { Id = "select-line", Scope = CmdScope.Editor,
                    VS = null,
                    VSCode = All(K(KeyCode.L, ctrl: true, d: "Ctrl+L")),
                    Rider = null,
                    Run = _ => True(() => _code.SelectCurrentLine()) },

                new AteCommand { Id = "goto-bracket", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.RightBracket, ctrl: true, d: "Ctrl+]")),
                    VSCode = All(K(KeyCode.Backslash, ctrl: true, shift: true, d: "Ctrl+Shift+\\")),
                    Rider = All(K(KeyCode.M, ctrl: true, shift: true, d: "Ctrl+Shift+M")),
                    Run = _ => True(() => _code.GoToMatchingBracket()) },
                new AteCommand { Id = "block-comment", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.Slash, ctrl: true, shift: true, d: "Ctrl+Shift+/")),
                    VSCode = All(K(KeyCode.A, shift: true, alt: true, d: "Shift+Alt+A"),
                                 K(KeyCode.Slash, ctrl: true, shift: true)),
                    Rider = All(K(KeyCode.Slash, ctrl: true, shift: true, d: "Ctrl+Shift+/")),
                    Run = _ => True(ToggleBlockComment) },
                new AteCommand { Id = "expand-selection", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.RightArrow, shift: true, alt: true, d: "Shift+Alt+Right")),
                    VSCode = All(K(KeyCode.RightArrow, shift: true, alt: true, d: "Shift+Alt+Right")),
                    Rider = All(K(KeyCode.W, ctrl: true, d: "Ctrl+W")),
                    Run = _ => True(() => _code.ExpandSelection()) },
                new AteCommand { Id = "shrink-selection", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.LeftArrow, shift: true, alt: true, d: "Shift+Alt+Left")),
                    VSCode = All(K(KeyCode.LeftArrow, shift: true, alt: true, d: "Shift+Alt+Left")),
                    Rider = All(K(KeyCode.W, ctrl: true, shift: true, d: "Ctrl+Shift+W")),
                    Run = _ => True(() => _code.ShrinkSelection()) },
                new AteCommand { Id = "nav-back", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.Minus, ctrl: true, d: "Ctrl+-")),
                    VSCode = All(K(KeyCode.LeftArrow, alt: true, d: "Alt+Left")),
                    Rider = All(K(KeyCode.LeftArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Left")),
                    Run = _ => True(NavigateBack) },
                new AteCommand { Id = "nav-forward", Scope = CmdScope.Global,
                    VS = All(K(KeyCode.Minus, ctrl: true, shift: true, d: "Ctrl+Shift+-")),
                    VSCode = All(K(KeyCode.RightArrow, alt: true, d: "Alt+Right")),
                    Rider = All(K(KeyCode.RightArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Right")),
                    Run = _ => True(NavigateForward) },

                new AteCommand { Id = "add-next-occurrence", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.Period, shift: true, alt: true, d: "Shift+Alt+.")),
                    VSCode = All(K(KeyCode.D, ctrl: true, d: "Ctrl+D")),
                    Rider = All(K(KeyCode.J, alt: true, d: "Alt+J")),
                    Run = _ => True(() => _code.AddNextOccurrence()) },
                new AteCommand { Id = "select-all-occurrences", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.Semicolon, shift: true, alt: true, d: "Shift+Alt+;")),
                    VSCode = All(K(KeyCode.L, ctrl: true, shift: true, d: "Ctrl+Shift+L")),
                    Rider = All(K(KeyCode.J, ctrl: true, shift: true, alt: true, d: "Ctrl+Alt+Shift+J")),
                    Run = _ => True(() => _code.SelectAllOccurrences()) },
                new AteCommand { Id = "add-caret-above", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.UpArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Up")),
                    VSCode = All(K(KeyCode.UpArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Up")),
                    Rider = All(K(KeyCode.UpArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Up")),
                    Run = _ => True(() => _code.AddCaretOnAdjacentLine(-1)) },
                new AteCommand { Id = "add-caret-below", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.DownArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Down")),
                    VSCode = All(K(KeyCode.DownArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Down")),
                    Rider = All(K(KeyCode.DownArrow, ctrl: true, alt: true, d: "Ctrl+Alt+Down")),
                    Run = _ => True(() => _code.AddCaretOnAdjacentLine(1)) },

                new AteCommand { Id = "fold-region", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.LeftBracket, ctrl: true, shift: true, d: "Ctrl+Shift+[")),
                    VSCode = All(K(KeyCode.LeftBracket, ctrl: true, shift: true, d: "Ctrl+Shift+[")),
                    Rider = All(K(KeyCode.LeftBracket, ctrl: true, shift: true, d: "Ctrl+Shift+[")),
                    Run = _ => True(() => _code.FoldAtCaret()) },
                new AteCommand { Id = "unfold-region", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.RightBracket, ctrl: true, shift: true, d: "Ctrl+Shift+]")),
                    VSCode = All(K(KeyCode.RightBracket, ctrl: true, shift: true, d: "Ctrl+Shift+]")),
                    Rider = All(K(KeyCode.RightBracket, ctrl: true, shift: true, d: "Ctrl+Shift+]")),
                    Run = _ => True(() => _code.UnfoldAtCaret()) },

                                new AteCommand { Id = "rename-symbol", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.F2, d: "F2")),
                    VSCode = All(K(KeyCode.F2, d: "F2")),
                    Rider = All(K(KeyCode.F6, shift: true, d: "Shift+F6")),
                    Run = _ => True(RenameSymbolAtCaret) },
                new AteCommand { Id = "find-references", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.F12, shift: true, d: "Shift+F12")),
                    VSCode = All(K(KeyCode.F12, shift: true, d: "Shift+F12")),
                    Rider = All(K(KeyCode.F7, alt: true, d: "Alt+F7")),
                    Run = _ => True(FindAllReferences) },
                new AteCommand { Id = "format-document", Scope = CmdScope.Editor,
                    VS = All(K(KeyCode.F, shift: true, alt: true, d: "Shift+Alt+F")),
                    VSCode = All(K(KeyCode.F, shift: true, alt: true, d: "Shift+Alt+F")),
                    Rider = All(K(KeyCode.L, ctrl: true, alt: true, d: "Ctrl+Alt+L")),
                    Run = _ => True(FormatDocument) },

                                // ---- Display-only (dispatched by CodeView / Tab special-case;
                //      the table only supplies the menu hint) ----
                Disp("undo", "Ctrl+Z", "Ctrl+Z", "Ctrl+Z"),
                Disp("redo", "Ctrl+Y", "Ctrl+Shift+Z", "Ctrl+Shift+Z"),
                Disp("cut", "Ctrl+X", "Ctrl+X", "Ctrl+X"),
                Disp("copy", "Ctrl+C", "Ctrl+C", "Ctrl+C"),
                Disp("paste", "Ctrl+V", "Ctrl+V", "Ctrl+V"),
                Disp("select-all", "Ctrl+A", "Ctrl+A", "Ctrl+A"),
                Disp("indent", "Tab", "Tab", "Tab"),
                Disp("unindent", "Shift+Tab", "Shift+Tab", "Shift+Tab"),
                Disp("find-in-tabs", "Ctrl+Shift+F", "Ctrl+Shift+F", "Ctrl+Shift+F"),
                Disp("replace-in-tabs", "Ctrl+Shift+H", "Ctrl+Shift+H", "Ctrl+Shift+R"),
                Disp("find-previous", "Shift+F3", "Shift+F3", "Shift+F3"),
            };
        }

        static AteCommand Disp(string id, string vs, string vscode, string rider) => new AteCommand
        {
            Id = id, Scope = CmdScope.Editor,
            VS = vs != null ? new[] { K(KeyCode.None, d: vs) } : null,
            VSCode = vscode != null ? new[] { K(KeyCode.None, d: vscode) } : null,
            Rider = rider != null ? new[] { K(KeyCode.None, d: rider) } : null,
        };

        /// <summary>Menu shortcut hint for a command in the active keymap;
        /// null when the command is unbound there.</summary>
        string Dsp(string id)
        {
            if (_commands == null) BuildCommands();
            foreach (var c in _commands)
                if (c.Id == id)
                {
                    var keys = c.For(EditorConfig.Keymap);
                    return keys != null && keys.Length > 0 ? keys[0].Display : null;
                }
            return null;
        }

        static bool MatchesRaw(Key k, KeyCode code, bool ctrl, bool shift, bool alt)
        {
            if (code != k.Code || ctrl != k.Ctrl) return false;
            if (!k.LooseAlt && alt != k.Alt) return false;
            if (!k.ShiftArg && !k.LooseShift && shift != k.Shift) return false;
            return true;
        }

        /// <summary>Test seam: which command (id) would handle this key in
        /// the active keymap, WITHOUT executing it. Null = unbound.</summary>
        internal string DebugMatchCommand(KeyCode code, bool ctrl, bool shift, bool alt, bool editorScope)
        {
            if (_commands == null) BuildCommands();
            var scope = editorScope ? CmdScope.Editor : CmdScope.Global;
            foreach (var c in _commands)
            {
                if (c.Scope != scope || c.Run == null) continue;
                var keys = c.For(EditorConfig.Keymap);
                if (keys == null) continue;
                foreach (var k in keys)
                    if (k.Code != KeyCode.None && MatchesRaw(k, code, ctrl, shift, alt))
                        return c.Id;
            }
            return null;
        }

        /// <summary>Runs the first command whose binding matches the event in
        /// the active keymap. Returns true when a command handled it.</summary>
        bool DispatchCommands(KeyDownEvent e, CmdScope scope)
        {
            if (_commands == null) BuildCommands();
            bool ctrl = e.ctrlKey || e.commandKey;
            foreach (var c in _commands)
            {
                if (c.Scope != scope || c.Run == null) continue;
                var keys = c.For(EditorConfig.Keymap);
                if (keys == null) continue;
                foreach (var k in keys)
                    if (k.Code != KeyCode.None && k.Matches(e, ctrl))
                        return c.Run(e.shiftKey);
            }
            return false;
        }
    }
}
#endif
