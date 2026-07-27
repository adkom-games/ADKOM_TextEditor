#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ADKOM.TextEditor
{
    /// <summary>
    /// GitHub Copilot integration: manages the official Copilot Language
    /// Server (@github/copilot-language-server, a Node process speaking LSP
    /// over stdio), the device-flow sign-in, document sync, and inline
    /// completion requests. The user brings their own Copilot subscription.
    /// Everything is asynchronous and non-modal; the editor never blocks.
    /// </summary>
    public static class CopilotService
    {
        public enum State { Off, Installing, Starting, NotSignedIn, SigningIn, Ready, Error }

        public static State Status { get; private set; } = State.Off;
        public static string StatusDetail { get; private set; } = string.Empty;
        public static string PendingUserCode { get; private set; }

        /// <summary>Raised on the main thread whenever Status changes.</summary>
        public static event Action onStatusChanged;

        static Process _proc;
        static Thread _readThread;
        static SynchronizationContext _mainCtx;
        static int _nextId = 1;
        static readonly ConcurrentDictionary<int, Action<JToken, JToken>> _pending =
            new ConcurrentDictionary<int, Action<JToken, JToken>>();
        static readonly object _writeLock = new object();
        static string _openUri;
        static int _docVersion;

        static string ServerDir => Path.Combine(
            Path.GetDirectoryName(Application.dataPath), "Library", "ADKOMTextEditor", "copilot");
        static string ServerJs => Path.Combine(ServerDir,
            "node_modules", "@github", "copilot-language-server", "dist", "language-server.js");

        static void SetStatus(State s, string detail = "")
        {
            // The server re-announces its status liberally; only real
            // transitions reach listeners (was spamming the console).
            if (s == Status && (detail ?? string.Empty) == StatusDetail) return;
            Status = s;
            StatusDetail = detail ?? string.Empty;
            Post(() => onStatusChanged?.Invoke());
        }

        static void Post(Action a)
        {
            if (_mainCtx != null) _mainCtx.Post(_ => a(), null);
            else a();
        }

        // ---------- Lifecycle ----------

        [InitializeOnLoadMethod]
        static void HookDomainReload() =>
            AssemblyReloadEvents.beforeAssemblyReload += Stop; // never orphan Node

        public static void Start()
        {
            if (_proc != null && !_proc.HasExited) return;
            _mainCtx = SynchronizationContext.Current;
            if (!File.Exists(ServerJs)) { InstallThenStart(); return; }
            Launch();
        }

        public static void Stop()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    Notify("exit", new JObject());
                    if (!_proc.WaitForExit(1000)) _proc.Kill();
                }
            }
            catch (Exception) { }
            _proc = null;
            _openUri = null;
            SetStatus(State.Off);
        }

        /// <summary>npm-installs the server into Library/ (per project, never
        /// shipped) on a worker thread, then launches it.</summary>
        static void InstallThenStart()
        {
            SetStatus(State.Installing, L10n.Tr("Installing the Copilot Language Server (npm)…"));
            var t = new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(ServerDir);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "npm.cmd",
                        Arguments = "install @github/copilot-language-server --no-audit --no-fund",
                        WorkingDirectory = ServerDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.StandardOutput.ReadToEnd();
                        string err = p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        if (p.ExitCode != 0 || !File.Exists(ServerJs))
                        {
                            SetStatus(State.Error, L10n.Tr("npm install failed: ") + err);
                            return;
                        }
                    }
                    Post(Launch);
                }
                catch (Exception ex)
                {
                    SetStatus(State.Error,
                        L10n.Tr("Could not run npm — is Node.js installed? ") + ex.Message);
                }
            }) { IsBackground = true, Name = "ATE Copilot npm install" };
            t.Start();
        }

        static void Launch()
        {
            try
            {
                SetStatus(State.Starting, L10n.Tr("Starting the Copilot Language Server…"));
                _proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "\"" + ServerJs + "\" --stdio",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                    }
                };
                _proc.Start();
                _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ATE Copilot LSP reader" };
                _readThread.Start();

                var init = new JObject
                {
                    ["processId"] = Process.GetCurrentProcess().Id,
                    ["clientInfo"] = new JObject { ["name"] = "ADKOM Text Editor", ["version"] = UpdateChecker.CurrentVersion() },
                    ["capabilities"] = new JObject
                    {
                        ["workspace"] = new JObject { ["workspaceFolders"] = false },
                        ["textDocument"] = new JObject { ["inlineCompletion"] = new JObject() }
                    },
                    ["initializationOptions"] = new JObject
                    {
                        ["editorInfo"] = new JObject { ["name"] = "ADKOMTextEditor", ["version"] = UpdateChecker.CurrentVersion() },
                        ["editorPluginInfo"] = new JObject { ["name"] = "adkom-ate-copilot", ["version"] = UpdateChecker.CurrentVersion() }
                    }
                };
                Request("initialize", init, (res, err) =>
                {
                    if (err != null) { SetStatus(State.Error, err.ToString()); return; }
                    Notify("initialized", new JObject());
                    CheckAuth();
                });
            }
            catch (Exception ex)
            {
                SetStatus(State.Error, L10n.Tr("Could not start Node.js: ") + ex.Message);
            }
        }

        // ---------- Auth ----------

        static void CheckAuth()
        {
            Request("checkStatus", new JObject(), (res, err) =>
            {
                string st = res?["status"]?.ToString();
                if (st == "OK" || st == "MaybeOk")
                    SetStatus(State.Ready, res?["user"]?.ToString() ?? "");
                else
                    SetStatus(State.NotSignedIn);
            });
        }

        /// <summary>Device-flow sign-in: surfaces the user code, opens the
        /// browser, and completes when GitHub confirms.</summary>
        public static void SignIn()
        {
            if (Status == State.Off) { Start(); }
            Request("signIn", new JObject(), (res, err) =>
            {
                if (err != null) { SetStatus(State.Error, err.ToString()); return; }
                AteConsole.Log("[Copilot] signIn response: " +
                    (res?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"));
                // Cached credentials elsewhere can complete instantly.
                string status = res?["status"]?.ToString();
                if (status == "AlreadySignedIn" || status == "OK")
                { CheckAuth(); return; }
                PendingUserCode = res?["userCode"]?.ToString();
                string uri = res?["verificationUri"]?.ToString();
                SetStatus(State.SigningIn, PendingUserCode ?? "");
                Post(() =>
                {
                    EditorGUIUtility.systemCopyBuffer = PendingUserCode ?? "";
                    if (!string.IsNullOrEmpty(uri)) Application.OpenURL(uri);
                    StartSignInWatchdog();
                });
                var cmd = res?["command"];
                if (cmd != null)
                {
                    Request("workspace/executeCommand", new JObject
                    {
                        ["command"] = cmd["command"],
                        ["arguments"] = cmd["arguments"] ?? new JArray()
                    }, (res2, err2) =>
                    {
                        AteConsole.Log("[Copilot] sign-in finish: " +
                            (err2 != null ? "error " + err2.ToString()
                             : res2?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"));
                        PendingUserCode = null;
                        CheckAuth(); // authoritative either way
                    });
                }
            });
        }

        // The finish command SHOULD resolve when GitHub confirms, but a missed
        // or mis-shaped response must never strand the user in "SigningIn"
        // (field report 2026-07-27): poll checkStatus while signing in.
        static double _watchdogUntil;

        static void StartSignInWatchdog()
        {
            _watchdogUntil = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update -= WatchdogTick;
            EditorApplication.update += WatchdogTick;
        }

        static double _nextWatchdogPoll;

        static void WatchdogTick()
        {
            if (Status != State.SigningIn || EditorApplication.timeSinceStartup > _watchdogUntil)
            {
                EditorApplication.update -= WatchdogTick;
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextWatchdogPoll) return;
            _nextWatchdogPoll = EditorApplication.timeSinceStartup + 3.0;
            Request("checkStatus", new JObject(), (res, err) =>
            {
                string st = res?["status"]?.ToString();
                if (st == "OK" || st == "MaybeOk")
                {
                    PendingUserCode = null;
                    SetStatus(State.Ready, res?["user"]?.ToString() ?? "");
                }
            });
        }

        public static void SignOut() =>
            Request("signOut", new JObject(), (res, err) => CheckAuth());

        // ---------- Documents & completions ----------

        static string UriFor(string path) =>
            new Uri(path).AbsoluteUri;

        static string LanguageIdFor(string path)
        {
            switch (Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".cs": return "csharp";
                case ".md": return "markdown";
                case ".json": return "json";
                case ".xml": case ".uxml": return "xml";
                case ".yaml": case ".yml": return "yaml";
                case ".shader": case ".hlsl": case ".cginc": return "shaderlab";
                default: return "plaintext";
            }
        }

        /// <summary>Full-text document sync (open or change).</summary>
        public static void SyncDocument(string path, string text)
        {
            if (Status != State.Ready || string.IsNullOrEmpty(path)) return;
            string uri = UriFor(path);
            if (_openUri != uri)
            {
                if (_openUri != null)
                    Notify("textDocument/didClose", new JObject
                    { ["textDocument"] = new JObject { ["uri"] = _openUri } });
                _openUri = uri;
                _docVersion = 1;
                Notify("textDocument/didOpen", new JObject
                {
                    ["textDocument"] = new JObject
                    {
                        ["uri"] = uri,
                        ["languageId"] = LanguageIdFor(path),
                        ["version"] = _docVersion,
                        ["text"] = text
                    }
                });
            }
            else
            {
                _docVersion++;
                Notify("textDocument/didChange", new JObject
                {
                    ["textDocument"] = new JObject { ["uri"] = uri, ["version"] = _docVersion },
                    ["contentChanges"] = new JArray(new JObject { ["text"] = text })
                });
            }
        }

        public struct Suggestion
        {
            public string Text;
            public int StartLine, StartChar, EndLine, EndChar; // replace range
            public bool HasRange;
        }

        /// <summary>Requests an inline completion at (line, character); the
        /// callback runs on the MAIN thread with the suggestion (whose Text
        /// REPLACES the given range — Copilot rewrites text around the caret,
        /// e.g. a "()" the user already typed) or null.</summary>
        public static void RequestCompletion(string path, int line, int character,
            Action<List<Suggestion>> onResult)
        {
            if (Status != State.Ready || _openUri == null) { onResult?.Invoke(null); return; }
            Request("textDocument/inlineCompletion", new JObject
            {
                ["textDocument"] = new JObject { ["uri"] = _openUri, ["version"] = _docVersion },
                ["position"] = new JObject { ["line"] = line, ["character"] = character },
                ["context"] = new JObject { ["triggerKind"] = 2 },
                ["formattingOptions"] = new JObject
                { ["tabSize"] = EditorConfig.TabSize, ["insertSpaces"] = true }
            }, (res, err) =>
            {
                var result = new List<Suggestion>();
                try
                {
                    var items = res?["items"] as JArray;
                    if (items != null)
                        foreach (var it in items)
                        {
                            string text = it?["insertText"]?.ToString();
                            if (string.IsNullOrEmpty(text)) continue;
                            var sg = new Suggestion { Text = text };
                            var range = it["range"];
                            if (range != null)
                            {
                                sg.HasRange = true;
                                sg.StartLine = (int)(range["start"]?["line"] ?? line);
                                sg.StartChar = (int)(range["start"]?["character"] ?? character);
                                sg.EndLine = (int)(range["end"]?["line"] ?? line);
                                sg.EndChar = (int)(range["end"]?["character"] ?? character);
                            }
                            else
                            {
                                sg.StartLine = sg.EndLine = line;
                                sg.StartChar = sg.EndChar = character;
                            }
                            result.Add(sg);
                        }
                }
                catch (Exception) { }
                Post(() => onResult?.Invoke(result.Count > 0 ? result : null));
            });
        }

        // ---------- JSON-RPC plumbing ----------

        static void Request(string method, JObject @params, Action<JToken, JToken> cb)
        {
            if (_proc == null || _proc.HasExited) { cb?.Invoke(null, JToken.FromObject("server not running")); return; }
            int id = Interlocked.Increment(ref _nextId);
            if (cb != null) _pending[id] = cb;
            Write(new JObject
            { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params });
        }

        static void Notify(string method, JObject @params) =>
            Write(new JObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params });

        static void Respond(JToken id, JToken result) =>
            Write(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

        static void Write(JObject msg)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None));
                byte[] head = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
                lock (_writeLock)
                {
                    var s = _proc.StandardInput.BaseStream;
                    s.Write(head, 0, head.Length);
                    s.Write(body, 0, body.Length);
                    s.Flush();
                }
            }
            catch (Exception ex)
            {
                SetStatus(State.Error, ex.Message);
            }
        }

        static void ReadLoop()
        {
            var stream = _proc.StandardOutput.BaseStream;
            var headerBuf = new StringBuilder();
            try
            {
                while (!_proc.HasExited)
                {
                    // Headers: read until \r\n\r\n.
                    headerBuf.Clear();
                    int b, contentLength = -1;
                    while ((b = stream.ReadByte()) != -1)
                    {
                        headerBuf.Append((char)b);
                        int n = headerBuf.Length;
                        if (n >= 4 && headerBuf[n - 4] == '\r' && headerBuf[n - 3] == '\n' &&
                            headerBuf[n - 2] == '\r' && headerBuf[n - 1] == '\n')
                            break;
                    }
                    if (b == -1) break;
                    foreach (var line in headerBuf.ToString().Split('\n'))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(), out contentLength);
                    if (contentLength <= 0) continue;
                    var body = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int r = stream.Read(body, read, contentLength - read);
                        if (r <= 0) { read = -1; break; }
                        read += r;
                    }
                    if (read < 0) break;
                    HandleMessage(JObject.Parse(Encoding.UTF8.GetString(body)));
                }
            }
            catch (Exception) { /* process torn down */ }
            if (Status != State.Off) SetStatus(State.Off, "server exited");
        }

        static void HandleMessage(JObject msg)
        {
            var id = msg["id"];
            string method = msg["method"]?.ToString();
            if (method == null && id != null)
            {
                // Response to one of our requests.
                if (int.TryParse(id.ToString(), out int rid) && _pending.TryRemove(rid, out var cb))
                    cb(msg["result"], msg["error"]);
                return;
            }
            switch (method)
            {
                case "didChangeStatus":
                {
                    string kind = msg["params"]?["kind"]?.ToString();
                    string message = msg["params"]?["message"]?.ToString() ?? "";
                    if (kind == "Normal") SetStatus(State.Ready, message);
                    else if (kind == "Error") SetStatus(State.Error, message);
                    else if (kind == "Inactive" && Status == State.Ready) StatusDetail = message;
                    break;
                }
                case "window/logMessage":
                case "featureFlagsNotification":
                case "statusNotification":
                case "$/progress":
                    break; // informational
                default:
                    // Server-to-client REQUESTS get an empty-but-valid reply so
                    // the server never hangs (configuration, showMessage, ...).
                    if (id != null)
                        Respond(id, method == "workspace/configuration" ? (JToken)new JArray() : JValue.CreateNull());
                    break;
            }
        }
    }
}
#endif
