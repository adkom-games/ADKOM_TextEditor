// ATE sample addon: Snake — a complete in-editor game on the AteApi game
// surface. Demonstrates: the STATEFUL addon lifecycle (API 1.2 — the game
// survives domain reloads via SaveState/RestoreState and re-finds its board
// document by StateTag), game mode, the 30 Hz-capped tick, consumable key
// events, key-state polling (hold Shift for turbo), WriteAt/ReadAt drawing,
// fg+bg color overlay, a pause mode, and the status-bar Prompt.
//
// Run it from Games → Snake. The game STARTS PAUSED — press Space to
// begin. Arrows/WASD steer, Space pauses/resumes (and restarts after a
// crash), Shift is turbo, Escape quits. After a domain reload the game
// comes back paused too.
using System;
using ADKOM.TextEditor.Scripting;
using UnityEngine;
using Random = System.Random;

[AteAddon(Name = "Snake", Category = "Games", ApiVersion = "1.2")]
public class SnakeGame : IAteAddonStateful
{
    const int W = 40, H = 20;       // playfield including the border
    const string Tag = "ate-snake"; // StateTag claiming our board document
    static readonly Color Border = new Color(0.45f, 0.45f, 0.45f);
    static readonly Color Head = new Color(0.4f, 1f, 0.4f);
    static readonly Color Body = new Color(0.1f, 0.6f, 0.1f);
    static readonly Color Food = new Color(1f, 0.35f, 0.25f);

    AteDocument _doc;
    AteTick _tick;
    readonly Random _rng = new Random();
    (int x, int y)[] _snake = new (int, int)[W * H];
    int _len, _dx, _dy, _pendingDx, _pendingDy;
    (int x, int y) _food;
    int _score;
    bool _running, _dead, _paused;
    bool _persisting; // SaveState returned state: OnUnload must keep the doc

    // ---- Lifecycle ----

    public void OnLoad()
    {
        AteApi.keyDown += OnKey;
        AteApi.documentClosed += d => { if (Equals(d, _doc)) StopGame(); };
    }

    public void OnUnload()
    {
        if (_persisting)
        {
            // The board document lives on in the session; RestoreState will
            // re-claim it by its StateTag after the reload.
            _tick?.Stop();
            _tick = null;
            _running = false;
            _doc = null;
            return;
        }
        StopGame();
    }

    public void OnFocusGained() { }
    public void OnFocusLost() { } // ticks pause and key states reset automatically

    public void Run()
    {
        if (_doc != null && _doc.IsValid) { _doc.Activate(); return; }
        _doc = AteApi.NewDocument(BuildBoardText());
        _doc.SetTitle("Snake");
        _doc.StateTag = Tag;
        _doc.GameMode = true;
        PaintBorder();
        NewGame();
        _tick = AteApi.StartTick(10, Step);
        _running = true;
    }

    // ---- State persistence (AteApi 1.2) ----

    [Serializable]
    class State
    {
        public int len, dx, dy, pdx, pdy, fx, fy, score;
        public bool dead;
        public int[] sx, sy;
    }

    public string SaveState()
    {
        _persisting = false;
        if (!_running || _doc == null || !_doc.IsValid) return null;
        var st = new State
        {
            len = _len, dx = _dx, dy = _dy, pdx = _pendingDx, pdy = _pendingDy,
            fx = _food.x, fy = _food.y, score = _score, dead = _dead,
            sx = new int[_len], sy = new int[_len]
        };
        for (int i = 0; i < _len; i++) { st.sx[i] = _snake[i].x; st.sy[i] = _snake[i].y; }
        _persisting = true;
        return JsonUtility.ToJson(st);
    }

    public void RestoreState(string state)
    {
        if (_running) return;
        var st = JsonUtility.FromJson<State>(state);
        if (st == null || st.sx == null || st.sy == null || st.len < 1 || st.len > st.sx.Length) return;
        AteDocument doc = null;
        foreach (var d in AteApi.Documents)
            if (d.IsValid && d.StateTag == Tag) { doc = d; break; }
        if (doc == null) return; // the board tab is gone — start fresh via Run

        _doc = doc;
        _doc.SetTitle("Snake");
        _doc.GameMode = true;
        _len = Math.Min(st.len, _snake.Length);
        for (int i = 0; i < _len; i++) _snake[i] = (st.sx[i], st.sy[i]);
        _dx = st.dx; _dy = st.dy; _pendingDx = st.pdx; _pendingDy = st.pdy;
        _food = (st.fx, st.fy);
        _score = st.score;
        _dead = st.dead;
        RedrawAll();
        _running = true;
        _paused = !_dead; // resume PAUSED — a reload should never cost a life
        DrawStatus();
        _tick = AteApi.StartTick(10, Step);
    }

    /// <summary>Repaints the whole board from game state (the restore path —
    /// the document's text survived the reload but colors did not).</summary>
    void RedrawAll()
    {
        _doc.SetText(BuildBoardText());
        _doc.ClearColors();
        PaintBorder();
        for (int i = _len - 1; i >= 0; i--) DrawCell(_snake[i], i == 0);
        _doc.WriteAt(_food.y + 1, _food.x + 1, "o");
        _doc.SetColor(_food.y + 1, _food.x + 1, _food.x + 2, Food, Food);
        if (_dead) DrawGameOverBanner();
    }

    // ---- Game ----

    void NewGame()
    {
        _len = 3;
        for (int i = 0; i < _len; i++) _snake[i] = (W / 2 - i, H / 2);
        _dx = _pendingDx = 1; _dy = _pendingDy = 0;
        _score = 0;
        _dead = false;
        _paused = true; // every game starts paused — Space when you're ready
        _doc.SetText(BuildBoardText());
        _doc.ClearColors();
        PaintBorder();
        for (int i = 0; i < _len; i++) DrawCell(_snake[i], i == 0);
        PlaceFood();
        DrawStatus();
    }

    void Step()
    {
        if (!_running || _dead || _paused || _doc == null || !_doc.IsValid) return;
        int steps = AteApi.IsKeyDown(KeyCode.LeftShift) || AteApi.IsKeyDown(KeyCode.RightShift) ? 2 : 1;
        for (int s = 0; s < steps && !_dead; s++) Advance();
    }

    void Advance()
    {
        _dx = _pendingDx; _dy = _pendingDy;
        var head = (_snake[0].x + _dx, _snake[0].y + _dy);
        string at = _doc.ReadAt(head.Item2 + 1, head.Item1 + 1, 1);
        bool ate = head == _food;
        // Cells are strictly ASCII ('#' border, 's' snake, 'o' food) drawn
        // with foreground == background so they read as solid blocks; the
        // letters double as collision data for ReadAt. Non-ASCII glyphs
        // (█ ●) render at fallback width and bow the grid — AteWriteMode docs.
        if (at != " " && !ate) { GameOver(); return; }

        // Move: clear the tail (unless growing), shift, draw the new head.
        if (!ate)
        {
            var tail = _snake[_len - 1];
            _doc.WriteAt(tail.y + 1, tail.x + 1, " ");
            _doc.SetColor(tail.y + 1, tail.x + 1, tail.x + 2, null, null);
        }
        else _len++;
        for (int i = _len - 1; i > 0; i--) _snake[i] = _snake[i - 1];
        _snake[0] = head;
        DrawCell(_snake[1], false); // old head becomes body
        DrawCell(head, true);
        if (ate)
        {
            _score += 10;
            PlaceFood();
            DrawStatus();
        }
    }

    void GameOver()
    {
        _dead = true;
        _paused = false;
        DrawGameOverBanner();
        DrawStatus();
        if (_score > UnityEditor.EditorPrefs.GetInt("ATE.Snake.HighScore", 0))
        {
            UnityEditor.EditorPrefs.SetInt("ATE.Snake.HighScore", _score);
            AteApi.Prompt("New high score! Your name:", name =>
            {
                if (!string.IsNullOrEmpty(name))
                    UnityEditor.EditorPrefs.SetString("ATE.Snake.HighName", name);
                DrawStatus();
            });
        }
    }

    void DrawGameOverBanner()
    {
        string msg = " GAME OVER — Space restarts, Esc quits ";
        int col = Math.Max(1, (W - msg.Length) / 2);
        _doc.WriteAt(H / 2 + 1, col + 1, msg);
        _doc.SetColor(H / 2 + 1, col + 1, col + 1 + msg.Length, Color.white, new Color(0.5f, 0.1f, 0.1f));
    }

    void StopGame()
    {
        _running = false;
        _paused = false;
        _persisting = false;
        _tick?.Stop();
        _tick = null;
        if (_doc != null && _doc.IsValid)
        {
            _doc.StateTag = null; // a quit game never resurrects
            if (_doc.GameMode)
            {
                _doc.GameMode = false;
                _doc.Close(discardChanges: true);
            }
        }
        _doc = null;
    }

    // ---- Input (consume only what the game uses, only while it runs) ----

    void OnKey(AteKeyEvent e)
    {
        if (!_running || _doc == null || !_doc.IsValid) return;
        var active = AteApi.ActiveDocument;
        if (active == null || !active.Equals(_doc)) return; // game tab not front
        switch (e.Key)
        {
            case KeyCode.UpArrow: case KeyCode.W: Turn(0, -1); e.Handled = true; break;
            case KeyCode.DownArrow: case KeyCode.S: Turn(0, 1); e.Handled = true; break;
            case KeyCode.LeftArrow: case KeyCode.A: Turn(-1, 0); e.Handled = true; break;
            case KeyCode.RightArrow: case KeyCode.D: Turn(1, 0); e.Handled = true; break;
            case KeyCode.Space:
                // Context-sensitive: restart when dead, pause/resume otherwise.
                if (_dead) NewGame();
                else { _paused = !_paused; DrawStatus(); }
                e.Handled = true;
                break;
            case KeyCode.Escape:
                StopGame();
                e.Handled = true;
                break;
        }
    }

    void Turn(int dx, int dy)
    {
        if (_paused || (dx == -_dx && dy == -_dy)) return; // paused / no instant reversal
        _pendingDx = dx; _pendingDy = dy;
    }

    // ---- Drawing ----

    static string BuildBoardText()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
                sb.Append(y == 0 || y == H - 1 || x == 0 || x == W - 1 ? '#' : ' ');
            sb.Append('\n');
        }
        sb.Append("Score: 0");
        return sb.ToString();
    }

    void PaintBorder()
    {
        _doc.SetColor(1, 1, W + 1, Border);
        _doc.SetColor(H, 1, W + 1, Border);
        for (int y = 2; y < H; y++)
        {
            _doc.SetColor(y, 1, 2, Border);
            _doc.SetColor(y, W, W + 1, Border);
        }
    }

    void DrawCell((int x, int y) c, bool head)
    {
        var col = head ? Head : Body;
        _doc.WriteAt(c.y + 1, c.x + 1, "s");
        _doc.SetColor(c.y + 1, c.x + 1, c.x + 2, col, col); // fg==bg: solid block
    }

    void PlaceFood()
    {
        for (int tries = 0; tries < 500; tries++)
        {
            var f = (x: 1 + _rng.Next(W - 2), y: 1 + _rng.Next(H - 2));
            if (_doc.ReadAt(f.y + 1, f.x + 1, 1) != " ") continue;
            _food = f;
            _doc.WriteAt(f.y + 1, f.x + 1, "o");
            _doc.SetColor(f.y + 1, f.x + 1, f.x + 2, Food, Food); // solid block
            return;
        }
    }

    void DrawStatus()
    {
        int high = UnityEditor.EditorPrefs.GetInt("ATE.Snake.HighScore", 0);
        string holder = UnityEditor.EditorPrefs.GetString("ATE.Snake.HighName", "");
        string line = "Score: " + _score + "   High: " + high +
            (holder.Length > 0 ? " (" + holder + ")" : "") +
            (_paused ? "   || PAUSED (Space resumes)" : "") + new string(' ', 28);
        _doc.WriteAt(H + 1, 1, line);
    }
}
