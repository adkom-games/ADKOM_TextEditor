// ATE sample addon: Snake — a complete in-editor game on the AteApi 1.1
// game surface. Demonstrates: the full addon lifecycle, game mode, the
// 30 Hz-capped tick, consumable key events, key-state polling (hold Shift
// for turbo), WriteAt/ReadAt drawing, fg+bg color overlay, and the
// status-bar Prompt.
//
// Run it from Tools → Addons → Games → Snake. Arrows/WASD steer, Shift is
// turbo, Space restarts after a crash, Escape quits.
using System;
using ADKOM.TextEditor.Scripting;
using UnityEngine;
using Random = System.Random;

[AteAddon(Name = "Snake", Category = "Games", ApiVersion = "1.1")]
public class SnakeGame : IAteAddonLifecycle
{
    const int W = 40, H = 20;       // playfield including the border
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
    bool _running, _dead;

    // ---- Lifecycle ----

    public void OnLoad()
    {
        AteApi.keyDown += OnKey;
        AteApi.documentClosed += d => { if (Equals(d, _doc)) StopGame(); };
    }

    public void OnUnload() => StopGame();

    public void OnFocusGained() { }
    public void OnFocusLost() { } // ticks pause and key states reset automatically

    public void Run()
    {
        if (_doc != null && _doc.IsValid) { _doc.Activate(); return; }
        _doc = AteApi.NewDocument(BuildBoardText());
        _doc.GameMode = true;
        PaintBorder();
        NewGame();
        _tick = AteApi.StartTick(10, Step);
        _running = true;
    }

    // ---- Game ----

    void NewGame()
    {
        _len = 3;
        for (int i = 0; i < _len; i++) _snake[i] = (W / 2 - i, H / 2);
        _dx = _pendingDx = 1; _dy = _pendingDy = 0;
        _score = 0;
        _dead = false;
        _doc.SetText(BuildBoardText());
        _doc.ClearColors();
        PaintBorder();
        for (int i = 0; i < _len; i++) DrawCell(_snake[i], i == 0);
        PlaceFood();
        DrawStatus();
    }

    void Step()
    {
        if (!_running || _dead || _doc == null || !_doc.IsValid) return;
        int steps = AteApi.IsKeyDown(KeyCode.LeftShift) || AteApi.IsKeyDown(KeyCode.RightShift) ? 2 : 1;
        for (int s = 0; s < steps && !_dead; s++) Advance();
    }

    void Advance()
    {
        _dx = _pendingDx; _dy = _pendingDy;
        var head = (_snake[0].x + _dx, _snake[0].y + _dy);
        string at = _doc.ReadAt(head.Item2 + 1, head.Item1 + 1, 1);
        bool ate = head == _food;
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
        string msg = " GAME OVER — Space restarts, Esc quits ";
        int col = Math.Max(1, (W - msg.Length) / 2);
        _doc.WriteAt(H / 2 + 1, col + 1, msg);
        _doc.SetColor(H / 2 + 1, col + 1, col + 1 + msg.Length, Color.white, new Color(0.5f, 0.1f, 0.1f));
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

    void StopGame()
    {
        _running = false;
        _tick?.Stop();
        _tick = null;
        if (_doc != null && _doc.IsValid && _doc.GameMode)
        {
            _doc.GameMode = false;
            _doc.Close(discardChanges: true);
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
                if (_dead) { NewGame(); e.Handled = true; }
                break;
            case KeyCode.Escape:
                StopGame();
                e.Handled = true;
                break;
        }
    }

    void Turn(int dx, int dy)
    {
        if (dx == -_dx && dy == -_dy) return; // no instant reversal
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
        _doc.WriteAt(c.y + 1, c.x + 1, "█");
        _doc.SetColor(c.y + 1, c.x + 1, c.x + 2, head ? Head : Body, head ? Head : (Color?)null);
    }

    void PlaceFood()
    {
        for (int tries = 0; tries < 500; tries++)
        {
            var f = (x: 1 + _rng.Next(W - 2), y: 1 + _rng.Next(H - 2));
            if (_doc.ReadAt(f.y + 1, f.x + 1, 1) != " ") continue;
            _food = f;
            _doc.WriteAt(f.y + 1, f.x + 1, "●");
            _doc.SetColor(f.y + 1, f.x + 1, f.x + 2, Food);
            return;
        }
    }

    void DrawStatus()
    {
        int high = UnityEditor.EditorPrefs.GetInt("ATE.Snake.HighScore", 0);
        string holder = UnityEditor.EditorPrefs.GetString("ATE.Snake.HighName", "");
        string line = "Score: " + _score + "   High: " + high +
            (holder.Length > 0 ? " (" + holder + ")" : "") + new string(' ', 20);
        _doc.WriteAt(H + 1, 1, line);
    }
}
