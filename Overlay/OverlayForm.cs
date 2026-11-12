using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using D4Overlay.Shared;

namespace D4WorldBossOverlay;

public class TrackedEvent
{
    public required string Label;
    public required Color Color;
    public required string[] RootKeyCandidates;
    public int ActiveWindowSeconds = 10;

    public EventAlertConfig Alerts = new();
    public DateTimeOffset? NextSpawnUtc;
    public string DisplayName = "?";
    public readonly HashSet<int> FiredAlertMinutes = new();
    /// <summary>false tant que les paliers déjà dépassés n'ont pas été marqués « tirés » sans son.</summary>
    public bool AlertsPrimed;
    public void ResetAlerts() { FiredAlertMinutes.Clear(); AlertsPrimed = false; }
    public string Text = "";
}

public class OverlayForm : Form
{
    private const string SiteUrl = "https://helltides.com/";
    private const string ApiUrl = "https://helltides.com/api/schedule";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        c.DefaultRequestHeaders.Referrer = new Uri(SiteUrl);
        return c;
    }

    private static string LogPath => Path.Combine(AppDir, "overlay.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 200_000) fi.Delete();
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static string AppDir => ConfigStore.AppDir;

    private static string FallbackJsonPath => Path.Combine(AppDir, "schedule.json");
    private static string LastDumpPath => Path.Combine(AppDir, "last.txt");
    private static string PosPath => Path.Combine(AppDir, "pos.txt");

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(600);
    // Identifiants des raccourcis globaux (RegisterHotKey). Les deux dernières ne sont actives qu'en mode déplacement.
    private const int HkMoveMode = 1;
    private const int HkRefresh = 2;
    private const int HkMute = 3;
    private const int HkOpacityUp = 4;
    private const int HkOpacityDown = 5;
    private const int OpacityStep = 5; // % par appui
    private const bool SoundEnabled = true;
    private readonly List<TrackedEvent> _events = new()
    {
        new TrackedEvent
        {
            Label = "World Boss", Color = OverlayLayout.ColorFor("World Boss"),
            RootKeyCandidates = new[] { "world_boss", "boss", "worldBoss" }
        },
        new TrackedEvent
        {
            Label = "Helltide", Color = OverlayLayout.ColorFor("Helltide"),
            RootKeyCandidates = new[] { "helltide" },
            ActiveWindowSeconds = 55 * 60
        },
        new TrackedEvent
        {
            Label = "Legion", Color = OverlayLayout.ColorFor("Legion"),
            RootKeyCandidates = new[] { "legion" }
        },
    };

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WM_HOTKEY = 0x0312;

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst,
        ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    private const int ULW_ALPHA = 2;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;

    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = (int)PollInterval.TotalMilliseconds };
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 1000 };
    private string _statusText = "init";
    private readonly OverlayRenderer _renderer = new();
    private OverlayLayout _layout = LayoutStore.LoadOrDefaults();
    private DateTime _alertsStamp, _layoutStamp, _settingsStamp;
    private bool _overlayVisible = true;
    // Sourdine rapide : volontairement NON mémorisée (on ne reste pas muet par oubli après un redémarrage).
    private bool _alertsMuted;
    private DateTime? _configAppliedAt; // accusé de réception pour le configurateur
    private HotkeySettings _hotkeys = new();
    private readonly HashSet<string> _hotkeyFailed = new();
    // Bulle d'information affichée 2 s sur l'overlay (opacité, sourdine…) et aperçu temporaire de l'opacité réelle en mode déplacement.
    private string _badgeText = "";
    private DateTime _badgeUntil = DateTime.MinValue;
    private DateTime _opacityPreviewUntil = DateTime.MinValue;
    // Détection du jeu (option « seulement en jeu » d'un event) : la liste des processus vient de settings.json.
    private string[] _gameProcesses = { "Diablo IV" };
    private bool _gameRunning;
    private DateTime _gameCheckedAt = DateTime.MinValue;
    private bool _blankPushed;
    private NotifyIcon? _tray;
    private int _configTicks;
    private bool _closeHover;
    // Le configurateur enregistre ses fichiers ; l'overlay les relit à cette cadence (F5 = tout de suite).
    private const int ConfigCheckSeconds = 10;
    private DateTime? _lastApiOk;
    private string _lastPublished = "";

    private WebView2? _browser;
    private bool _browserReady;
    private bool _pollInFlight;
    private string? _lastGoodJson;

    private bool _editMode;
    private Point _dragOffset;
    private string _source = "cadence";

    public OverlayForm()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(ConfigStore.SoundsDir);
        TryLoadCachedJson();
        LoadOrCreateAlerts();
        _alertsStamp = Stamp(ConfigStore.AlertsPath);
        _layoutStamp = Stamp(LayoutStore.LayoutPath);
        _settingsStamp = Stamp(SettingsStore.SettingsPath);
        var startupSettings = SettingsStore.LoadOrDefaults();
        _overlayVisible = startupSettings.OverlayVisible;
        _gameProcesses = startupSettings.GameProcesses;
        _hotkeys = startupSettings.Hotkeys;
        ApplyStartWithWindows(startupSettings.StartWithWindows);
        Log("layout au démarrage : " + DescribeLayout(_layout));

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = LoadSavedPosition();
        _renderer.SetBackground(_layout.BackgroundImage);
        Size = WindowSize();
        Location = FitOnScreen(Location, Size); // écran débranché, résolution changée…

        foreach (var ev in _events)
            ev.Text = $"{Loc.EventName(ev.Label)}: --:--:--";

        _browser = new WebView2
        {
            Size = new Size(1, 1),
            Location = new Point(-10, -10),
            Visible = false
        };
        Controls.Add(_browser);

        if (_lastGoodJson is null || !TryApplyJson(_lastGoodJson))
            ApplyCadenceFallback();
        RefreshCountdowns();

        _pollTimer.Tick += async (_, _) => await PollAsync();
        _tickTimer.Tick += (_, _) =>
        {
            if (++_configTicks >= ConfigCheckSeconds)
            {
                _configTicks = 0;
                CheckConfigFiles();
            }
            if (AnyEventExpired())
            {
                if (!TryApplyFromCache())
                    ApplyCadenceFallback();
            }
            RefreshCountdowns();
        };

        Load += async (_, _) =>
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SetClickThrough(true);
            RegisterGlobalHotkeys();
            InitTray();
            _tickTimer.Start();
            await InitBrowserAsync();
            _pollTimer.Start();
            await PollAsync();
        };

        FormClosing += (_, _) =>
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            UnregisterAllHotkeys();
            SaveSavedPosition(Location);
            _browser?.Dispose();
            if (_tray is not null)
            {
                _tray.Visible = false; // sinon l'icône reste affichée jusqu'au survol
                _tray.Dispose();
            }
            StatusStore.Clear();
        };

        HookDrag(this);
    }

    /// <summary>
    /// Les Label avalent la souris : sans ça le Form ne reçoit jamais
    /// MouseDown et le drag "ne marche pas". On attache le même handler
    /// au Form ET à chaque contrôle.
    /// </summary>
    /// 
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        if (IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            _pollInFlight = false;
            SetStatus("reveil — recalc");
            if (!TryApplyFromCache())
                ApplyCadenceFallback();
            _ = PollAsync();
        }));
    }

    private bool AnyEventExpired()
        => _events.Any(ev =>
            ev.NextSpawnUtc is DateTimeOffset t
            && DateTimeOffset.UtcNow >= t.AddSeconds(ev.ActiveWindowSeconds));
    private void HookDrag(Control c)
    {
        c.MouseDown += OnDragStart;
        c.MouseMove += OnDragMove;
        c.MouseUp += OnDragEnd;
    }

    private void OnDragStart(object? sender, MouseEventArgs e)
    {
        if (!_editMode || e.Button != MouseButtons.Left) return;
        var src = (Control)sender!;
        // Offset en coordonnées écran → coin du Form, pour que le drag
        // parte du vrai pixel cliqué (que ce soit un Label ou le Form).
        var screen = src.PointToScreen(e.Location);
        _dragOffset = new Point(screen.X - Location.X, screen.Y - Location.Y);
        Capture = true;
    }

    private void OnDragMove(object? sender, MouseEventArgs e)
    {
        if (!_editMode || !Capture || e.Button != MouseButtons.Left) return;
        var screen = ((Control)sender!).PointToScreen(e.Location);
        Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
    }

    private void OnDragEnd(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            SaveSavedPosition(Location);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == OverlayMessages.Reload)
        {
            // Le configurateur vient d'enregistrer : on applique tout de suite, sans attendre le contrôle périodique.
            CheckConfigFiles();
            _configAppliedAt = DateTime.UtcNow; // « je suis à jour » : c'est ce que le configurateur attend pour confirmer
            PublishStatus();
            return;
        }

        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HkMoveMode:
                    ToggleMoveMode();
                    break;
                case HkRefresh:
                    CheckConfigFiles(); // applique aussi tout de suite les réglages du configurateur
                    _ = ForceRefreshAsync();
                    break;
                case HkMute:
                    ToggleMuteByHotkey();
                    break;
                case HkOpacityUp:
                    AdjustOpacity(+OpacityStep);
                    break;
                case HkOpacityDown:
                    AdjustOpacity(-OpacityStep);
                    break;
            }
        }
        base.WndProc(ref m);
    }
    // ───────────── Raccourcis clavier globaux ─────────────

    /// <summary>Entrée / sortie du mode déplacement. Les touches d'opacité n'existent que pendant ce mode : aucune touche en plus en jeu.</summary>
    private void ToggleMoveMode()
    {
        if (!_overlayVisible)
        {
            Log("raccourci de déplacement ignoré : overlay masqué");
            return;
        }

        _editMode = !_editMode;
        SetClickThrough(!_editMode);
        if (_editMode)
        {
            RegisterOpacityHotkeys();
            SetStatus("Mode déplacement actif");
        }
        else
        {
            UnregisterOpacityHotkeys();
            // Lâché hors écran pendant le déplacement : on le ramène avant de mémoriser la position.
            Location = FitOnScreen(Location, Size);
            SaveSavedPosition(Location);
            SetStatus("Verrouillé");
        }
    }

    private bool RegisterOne(int id, string text, string name, bool allowRepeat)
    {
        if (string.IsNullOrWhiteSpace(text)) return false; // aucune touche attribuée : normal, pas une erreur

        if (!HotkeySpec.TryParse(text, out var spec))
        {
            _hotkeyFailed.Add(name);
            Log($"raccourci {name} : « {text} » n'est pas une touche valide");
            return false;
        }

        uint modifiers = spec.Modifiers | (allowRepeat ? 0u : HotkeySpec.ModNoRepeat);
        if (RegisterHotKey(Handle, id, modifiers, spec.VirtualKey)) return true;

        _hotkeyFailed.Add(name);
        Log($"raccourci {name} « {text} » indisponible (déjà utilisé par une autre application)");
        return false;
    }

    /// <summary>(Ré)enregistre les raccourcis permanents ; teste aussi les touches d'opacité pour signaler un conflit dès maintenant.</summary>
    private void RegisterGlobalHotkeys()
    {
        for (int id = HkMoveMode; id <= HkOpacityDown; id++) UnregisterHotKey(Handle, id);
        _hotkeyFailed.Clear();

        bool move = RegisterOne(HkMoveMode, _hotkeys.MoveMode, "MoveMode", false);
        bool refresh = RegisterOne(HkRefresh, _hotkeys.Refresh, "Refresh", false);
        bool mute = RegisterOne(HkMute, _hotkeys.Mute, "Mute", false);
        Log($"raccourcis : déplacement={_hotkeys.MoveMode} ({move}), rafraîchir={_hotkeys.Refresh} ({refresh}), " +
            $"sourdine={(string.IsNullOrEmpty(_hotkeys.Mute) ? "aucune" : _hotkeys.Mute)} ({mute})");

        RegisterOpacityHotkeys();
        if (!_editMode) UnregisterOpacityHotkeys(); // simple test de disponibilité : elles ne restent actives qu'en mode déplacement
        PublishStatus();
    }

    private void RegisterOpacityHotkeys()
    {
        UnregisterOpacityHotkeys();
        RegisterOne(HkOpacityUp, _hotkeys.OpacityUp, "OpacityUp", true);   // répétition permise : on peut garder la touche enfoncée
        RegisterOne(HkOpacityDown, _hotkeys.OpacityDown, "OpacityDown", true);
    }

    private void UnregisterOpacityHotkeys()
    {
        UnregisterHotKey(Handle, HkOpacityUp);
        UnregisterHotKey(Handle, HkOpacityDown);
    }

    private void UnregisterAllHotkeys()
    {
        for (int id = HkMoveMode; id <= HkOpacityDown; id++) UnregisterHotKey(Handle, id);
    }

    /// <summary>Sourdine par raccourci : pas d'interface sous les yeux, donc un petit « whoosh » et une bulle confirment la prise en compte.</summary>
    private void ToggleMuteByHotkey()
    {
        bool mute = !_alertsMuted;
        if (mute) AlertSounds.PlayFeedback(reversed: true); // dernier son avant le silence : « whoosh » à l'envers
        SetMuted(mute);
        if (!mute) AlertSounds.PlayFeedback(reversed: false); // le son est de retour : « whoosh » à l'endroit
        ShowBadge(Loc.T(mute ? "overlay.muted" : "overlay.unmuted"));
    }

    /// <summary>Opacité ± en mode déplacement : enregistrée tout de suite, avec un aperçu de l'opacité réelle pendant 2 s.</summary>
    private void AdjustOpacity(int delta)
    {
        if (!_editMode) return;

        int value = Math.Clamp(_layout.Opacity + delta, 20, 100);
        if (value != _layout.Opacity)
        {
            _layout.Opacity = value;
            try
            {
                LayoutStore.Save(_layout);
                _layoutStamp = Stamp(LayoutStore.LayoutPath); // on ne relit pas ce qu'on vient d'écrire
            }
            catch (Exception ex) { Log("layout non enregistré : " + ex.Message); }
        }

        // En mode déplacement l'overlay est opaque pour qu'on le retrouve ; pendant 2 s on montre l'opacité choisie.
        _opacityPreviewUntil = DateTime.UtcNow.AddSeconds(2);
        ShowBadge(Loc.T("overlay.opacityBadge", value));
    }

    private void ShowBadge(string text)
    {
        _badgeText = text;
        _badgeUntil = DateTime.UtcNow.AddSeconds(2);
        RenderOverlay();
    }

    private static void DrawBadge(Graphics g, Size size, string text)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = g.MeasureString(text, font);
        float w = measured.Width + 24, h = measured.Height + 12;
        var rect = new RectangleF((size.Width - w) / 2f, (size.Height - h) / 2f, w, h);
        using var back = new SolidBrush(Color.FromArgb(225, 15, 15, 20));
        g.FillRectangle(back, rect);
        using var border = new Pen(Color.FromArgb(235, 255, 255, 255), 1f);
        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawString(text, font, Brushes.White, rect.X + 12, rect.Y + 6);
    }

    private async Task ForceRefreshAsync()
    {
        _pollInFlight = false;
        SetStatus("refresh API…");
        await PollAsync(forceRefresh: true);
    }
    private void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(Handle, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        style = enabled ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
        SetWindowLong(Handle, GWL_EXSTYLE, style);
    }

    private async Task InitBrowserAsync()
    {
        try
        {
            if (_browser is null) return;
            // Dossier de données dans %AppData% : l'exe peut se trouver dans un dossier en lecture seule (Program Files).
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppDir, "webview"));
            await _browser.EnsureCoreWebView2Async(env);
            _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            _browser.CoreWebView2.WebResourceResponseReceived += async (_, e) =>
            {
                try
                {
                    if (e.Request.Uri.IndexOf("/api/schedule", StringComparison.OrdinalIgnoreCase) < 0)
                        return;
                    var stream = await e.Response.GetContentAsync();
                    if (stream is null) return;
                    using var sr = new StreamReader(stream);
                    string body = await sr.ReadToEndAsync();
                    Log($"intercept {e.Request.Uri} len={body.Length}");
                    Dump("intercept", body);
                    if (LooksLikeJson(body) && IsScheduleJson(body))
                    {
                        _lastGoodJson = body;
                        try
                        {
                            Directory.CreateDirectory(AppDir);
                            File.WriteAllText(FallbackJsonPath, body);
                        }
                        catch { }
                        BeginInvoke(new Action(() =>
                        {
                            if (TryApplyJson(body))
                            {
                                _source = "API";
                                _lastApiOk = DateTime.Now;
                                SetStatus("API OK");
                            }
                        }));
                    }
                }
                catch (Exception ex) { Log("intercept KO: " + ex.Message); }
            };

            _browserReady = true;
            Log("webview ready");
            SetStatus("WebView2 OK — auto waiting API");
        }
        catch (Exception ex)
        {
            _browserReady = false;
            Log("webview KO: " + ex);
            SetStatus("WebView2 KO: " + ShortError(ex));
        }
    }

    private async Task PollAsync(bool forceRefresh = false)
    {
        if (_pollInFlight) return;
        _pollInFlight = true;
        try
        {
            // Le réseau fait foi. Le cache disque ne sert que de secours.
            string? json = await FetchFromNetworkAsync();
            if (json is not null && TryApplyJson(json))
            {
                _lastGoodJson = json;
                SaveFetchedJson(json);
                _source = "API";
                _lastApiOk = DateTime.Now;
                Log("poll: API OK");
                SetStatus("API OK");
                return;
            }

            if (_lastGoodJson is not null && TryApplyJson(_lastGoodJson))
            {
                _source = "cache";
                Log("poll: réseau KO, cache encore valide");
                SetStatus("réseau KO — cache");
                return;
            }

            ApplyCadenceFallback();
            _source = "cadence";
            Log("poll: réseau KO, cache périmé → cadence");
            SetStatus("API ko — cadence");
        }
        catch (Exception ex)
        {
            Log("poll exception: " + ex);
            ApplyCadenceFallback();
            _source = "cadence";
            SetStatus("err " + ShortError(ex));
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    /// <summary>HTTP direct d'abord, puis WebView2 (au cas où le site filtre les clients non-navigateur).</summary>
    private async Task<string?> FetchFromNetworkAsync()
    {
        var viaHttp = await FetchViaHttpAsync();
        if (viaHttp is not null) return viaHttp;
        return await FetchViaWebViewAsync();
    }

    private static async Task<string?> FetchViaHttpAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ApiUrl);
            string body = await resp.Content.ReadAsStringAsync();
            Log($"http {(int)resp.StatusCode} len={body.Length}");
            if (!resp.IsSuccessStatusCode) return null;
            string? json = ExtractJson(body);
            return json is not null && IsScheduleJson(json) ? json : null;
        }
        catch (Exception ex)
        {
            Log("http KO: " + ex.Message);
            return null;
        }
    }

    private async Task<string?> FetchViaWebViewAsync()
    {
        if (!_browserReady || _browser?.CoreWebView2 is null)
        {
            Log("webview fetch: pas prêt");
            return null;
        }

        var wv = _browser.CoreWebView2;
        var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e) => nav.TrySetResult(e.IsSuccess);
        wv.NavigationCompleted += OnNav;
        try
        {
            wv.Navigate(ApiUrl);
            var done = await Task.WhenAny(nav.Task, Task.Delay(20000));
            if (done != nav.Task || !nav.Task.Result)
            {
                Log("webview fetch: navigation KO/timeout");
                return null;
            }

            // Si une page de vérification s'affiche d'abord, on laisse quelques secondes.
            for (int i = 0; i < 10; i++)
            {
                string wrapped = await wv.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                string body = UnwrapScriptResult(wrapped);
                string? json = ExtractJson(body);
                if (json is not null && IsScheduleJson(json))
                {
                    Log($"webview fetch OK len={json.Length}");
                    return json;
                }
                await Task.Delay(1000);
            }
            Log("webview fetch: contenu non reconnu");
            return null;
        }
        catch (Exception ex)
        {
            Log("webview fetch KO: " + ex.Message);
            return null;
        }
        finally
        {
            wv.NavigationCompleted -= OnNav;
        }
    }

    private static void SaveFetchedJson(string json)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(FallbackJsonPath, json);
        }
        catch { }
    }

    /// <summary>
    /// Relit schedule.json / last.txt / mémoire et applique si c'est un schedule.
    /// Appelé au boot, à chaque tick, et après intercept.
    /// </summary>
    private bool TryApplyFromCache()
    {
        if (!string.IsNullOrEmpty(_lastGoodJson) && TryApplyJson(_lastGoodJson))
        {
            _source = "cache";
            return true;
        }
        TryLoadCachedJson();
        if (!string.IsNullOrEmpty(_lastGoodJson) && TryApplyJson(_lastGoodJson))
        {
            _source = "cache";
            return true;
        }
        return false;
    }

    private bool TryApplyJson(string json)
    {
        try
        {
            json = ExtractJson(json) ?? json;
            if (!IsScheduleJson(json)) return false;
            using var doc = JsonDocument.Parse(json);
            var root = UnwrapRoot(doc.RootElement);
            int parsed = 0;
            var missing = new List<TrackedEvent>();
            foreach (var ev in _events)
            {
                if (!TryDeepFind(root, ev, out var name, out var spawnUtc))
                {
                    missing.Add(ev);
                    continue;
                }
                ev.DisplayName = name;
                if (ev.NextSpawnUtc != spawnUtc) ev.ResetAlerts();
                ev.NextSpawnUtc = spawnUtc;
                parsed++;
            }
            if (parsed == 0) return false;
            var now = DateTimeOffset.UtcNow;
            foreach (var ev in missing) ApplyCadenceTo(ev, now);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonElement UnwrapRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (!string.IsNullOrWhiteSpace(inner) && LooksLikeJson(inner))
            {
                using var innerDoc = JsonDocument.Parse(inner);
                return innerDoc.RootElement.Clone();
            }
        }
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrap in new[] { "data", "schedule", "result", "payload" })
            {
                if (root.TryGetProperty(wrap, out var inner)
                    && inner.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    return inner;
            }
        }
        return root;
    }

    private static bool TryDeepFind(JsonElement el, TrackedEvent ev, out string name, out DateTimeOffset spawnUtc)
    {
        if (TryFindEvent(el, ev, out name, out spawnUtc))
            return true;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
                if (TryDeepFind(p.Value, ev, out name, out spawnUtc))
                    return true;
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (TryDeepFind(item, ev, out name, out spawnUtc))
                    return true;
        }
        name = ev.Label;
        spawnUtc = default;
        return false;
    }

    private static bool TryFindEvent(JsonElement root, TrackedEvent ev, out string name, out DateTimeOffset spawnUtc)
    {
        name = ev.Label;
        spawnUtc = default;
        if (!TryGetByCandidates(root, ev.RootKeyCandidates, out var dataEl))
            return false;

        var items = FlattenItems(dataEl);
        if (items.Count == 0) return false;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset bestTime = DateTimeOffset.MaxValue;
        JsonElement? best = null;

        foreach (var item in items)
        {
            if (!TryReadStart(item, out var t)) continue;
            if (t.AddSeconds(ev.ActiveWindowSeconds) > now && t < bestTime)
            {
                bestTime = t;
                best = item;
            }
        }
        if (best is null) return false;
        spawnUtc = bestTime;
        name = ReadName(best.Value, ev.Label);
        return true;
    }

    private static bool TryGetByCandidates(JsonElement obj, string[] candidates, out JsonElement dataEl)
    {
        dataEl = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in candidates)
        {
            if (obj.TryGetProperty(key, out var el))
            {
                dataEl = el;
                return true;
            }
        }
        foreach (var p in obj.EnumerateObject())
        {
            string norm = p.Name.Replace("-", "_").Replace(" ", "_");
            foreach (var key in candidates)
            {
                if (string.Equals(norm, key, StringComparison.OrdinalIgnoreCase))
                {
                    dataEl = p.Value;
                    return true;
                }
            }
        }
        return false;
    }

    private static List<JsonElement> FlattenItems(JsonElement dataEl)
    {
        var items = new List<JsonElement>();
        if (dataEl.ValueKind == JsonValueKind.Array)
            items.AddRange(dataEl.EnumerateArray());
        else if (dataEl.ValueKind == JsonValueKind.Object)
            items.Add(dataEl);
        return items;
    }

    private static bool TryReadStart(JsonElement item, out DateTimeOffset when)
    {
        when = default;
        foreach (var key in new[] { "timestamp", "expectedTime", "startTime", "start_time", "time", "start" })
        {
            if (!item.TryGetProperty(key, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Number && UnixToDto(el.GetDouble(), out when))
                return true;
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (DateTimeOffset.TryParse(s, out var dto))
                {
                    when = dto.ToUniversalTime();
                    return true;
                }
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var n)
                    && UnixToDto(n, out when))
                    return true;
            }
        }
        return false;
    }

    private static bool UnixToDto(double n, out DateTimeOffset when)
    {
        when = default;
        if (n <= 0) return false;
        long seconds = n > 1e11 ? (long)(n / 1000.0) : (long)n;
        try { when = DateTimeOffset.FromUnixTimeSeconds(seconds); return true; }
        catch { return false; }
    }

    private static string ReadName(JsonElement item, string fallback)
    {
        // Zone ignorée : la map in-game / le site suffisent.
        // Helltide et Legion n'ont d'ailleurs pas de zone dans /api/schedule.
        foreach (var key in new[] { "boss", "name", "title" })
        {
            if (item.TryGetProperty(key, out var ne) && ne.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(ne.GetString()))
                return ne.GetString()!;
        }
        return fallback;
    }

    private void ApplyCadenceFallback()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var ev in _events)
            ApplyCadenceTo(ev, now);
    }

    private static void ApplyCadenceTo(TrackedEvent ev, DateTimeOffset now)
    {
        DateTimeOffset next = ev.Label switch
        {
            "Helltide" => NextHelltide(now),
            "World Boss" => NextWorldBoss(now),
            "Legion" => NextLegion(now),
            _ => now.AddHours(1)
        };
        if (ev.NextSpawnUtc != next) ev.ResetAlerts();
        ev.NextSpawnUtc = next;
        ev.DisplayName = ev.Label;
    }

    private static DateTimeOffset NextHelltide(DateTimeOffset now)
    {
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        if (now < hourStart.AddMinutes(55)) return hourStart;
        return hourStart.AddHours(1);
    }

    private static DateTimeOffset NextWorldBoss(DateTimeOffset now)
    {
        var anchor = new DateTimeOffset(2024, 1, 1, 8, 30, 0, TimeSpan.Zero);
        var period = TimeSpan.FromHours(3.5);
        long n = (long)Math.Floor((now - anchor).TotalSeconds / period.TotalSeconds);
        var candidate = anchor + TimeSpan.FromSeconds(n * period.TotalSeconds);
        if (candidate.AddSeconds(10) <= now) candidate += period;
        return candidate;
    }

    private static DateTimeOffset NextLegion(DateTimeOffset now)
    {
        // Cycle de 25 min, ancré sur 2026-10-26 05:40 UTC (vérifié sur l'API).
        var anchor = new DateTimeOffset(2026, 10, 26, 5, 40, 0, TimeSpan.Zero);
        var period = TimeSpan.FromMinutes(25);
        long n = (long)Math.Floor((now - anchor).TotalSeconds / period.TotalSeconds);
        var candidate = anchor + TimeSpan.FromSeconds(n * period.TotalSeconds);
        if (candidate.AddSeconds(10) <= now) candidate += period;
        return candidate;
    }

    private static string UnwrapScriptResult(string wrapped)
    {
        if (string.IsNullOrWhiteSpace(wrapped) || wrapped == "null") return "";
        try { return JsonSerializer.Deserialize<string>(wrapped) ?? wrapped; }
        catch { return wrapped; }
    }

    private static bool LooksLikeJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        int i = 0;
        while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
        return i < body.Length && (body[i] == '{' || body[i] == '[');
    }

    private void TryLoadCachedJson()
    {
        try
        {
            foreach (var path in new[] { FallbackJsonPath, LastDumpPath })
            {
                if (!File.Exists(path)) continue;
                string raw = File.ReadAllText(path);
                string? json = ExtractJson(raw);
                if (json is null || !IsScheduleJson(json)) continue;
                _lastGoodJson = json;
                return;
            }
        }
        catch { }
    }

    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        int obj = raw.IndexOf('{');
        int arr = raw.IndexOf('[');
        int i = -1;
        if (obj >= 0 && arr >= 0) i = Math.Min(obj, arr);
        else if (obj >= 0) i = obj;
        else if (arr >= 0) i = arr;
        if (i < 0) return null;
        string json = raw[i..];
        return LooksLikeJson(json) ? json : null;
    }

    private static bool IsScheduleJson(string json)
        => json.IndexOf("world_boss", StringComparison.OrdinalIgnoreCase) >= 0
        || json.IndexOf("helltide", StringComparison.OrdinalIgnoreCase) >= 0
        || json.IndexOf("\"legion\"", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void Dump(string tag, string body)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            string head = $"--- {tag} {DateTime.Now:O} len={body?.Length ?? 0} ---\n";
            File.WriteAllText(Path.Combine(AppDir, "debug.txt"), head + (body ?? ""));

            string? json = ExtractJson(body ?? "");
            if (json is null || !IsScheduleJson(json)) return;

            File.WriteAllText(LastDumpPath, head + json);
            File.WriteAllText(FallbackJsonPath, json);
        }
        catch { }
    }

    private void RefreshCountdowns()
    {
        foreach (var ev in _events)
        {
            if (!ev.Alerts.Visible) continue;
            if (ev.NextSpawnUtc is null)
            {
                ev.Text = $"{Loc.EventName(ev.Label)}: --:--:--";
                continue;
            }

            var remaining = ev.NextSpawnUtc.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                var since = -remaining;
                if (since.TotalSeconds < ev.ActiveWindowSeconds)
                {
                    var left = TimeSpan.FromSeconds(ev.ActiveWindowSeconds) - since;
                    ev.Text = $"{Loc.EventName(ev.Label)}: {Loc.T("overlay.up")} {Fmt(left)}";
                }
                else ev.Text = $"{Loc.EventName(ev.Label)}: {Loc.T("overlay.spawned")}";
                continue;
            }

            string who = ev.DisplayName != ev.Label && ev.DisplayName != "?"
                ? ev.DisplayName : Loc.EventName(ev.Label);
            ev.Text = $"{who}: {Fmt(remaining)}";

            TickAlerts(ev, remaining);
        }

        RenderOverlay();
        PublishStatus();
    }

    // ───────────── Config (alerts.json, écrit par le configurateur) ─────────────

    private void LoadOrCreateAlerts()
    {
        ConfigStore.EnsureFileExists();
        ApplyConfig(ConfigStore.LoadOrDefaults());
    }

    private void ApplyConfig(Dictionary<string, EventAlertConfig> map)
    {
        foreach (var ev in _events)
        {
            if (!map.TryGetValue(ev.Label, out var cfg)) continue;
            ev.Alerts = cfg;
            ev.ResetAlerts();
        }
    }

    private static DateTime Stamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    // Une fois par seconde : le configurateur a-t-il enregistré quelque chose ?
    // (plus fiable qu'un FileSystemWatcher avec un remplacement de fichier par renommage)
    private void CheckConfigFiles()
    {
        var a = Stamp(ConfigStore.AlertsPath);
        if (a != _alertsStamp)
        {
            _alertsStamp = a;
            ReloadConfig();
        }

        var l = Stamp(LayoutStore.LayoutPath);
        if (l != _layoutStamp)
        {
            _layoutStamp = l;
            ReloadLayout();
        }

        var st = Stamp(SettingsStore.SettingsPath);
        if (st != _settingsStamp)
        {
            _settingsStamp = st;
            var settings = SettingsStore.LoadOrDefaults();
            Loc.SetLanguage(settings.Language);
            _gameProcesses = settings.GameProcesses;
            ApplyStartWithWindows(settings.StartWithWindows);
            if (!_hotkeys.SameAs(settings.Hotkeys))
            {
                _hotkeys = settings.Hotkeys;
                RegisterGlobalHotkeys();
            }
            _gameCheckedAt = DateTime.MinValue; // on revérifie tout de suite avec la nouvelle liste
            Log("langue : " + Loc.Code);
            SetOverlayVisible(settings.OverlayVisible, persist: false);
            RefreshCountdowns();
        }
    }

    private void ReloadConfig()
    {
        if (!ConfigStore.TryLoad(out var map))
        {
            Log("config: lecture impossible, on garde la précédente");
            return;
        }
        ApplyConfig(map);
        RefreshCountdowns();
        Log("config rechargée");
    }

    private static string DescribeLayout(OverlayLayout l)
    {
        var wb = l.GetPlacement("World Boss");
        return $"{l.FontFamily} {l.FontSize}px{(l.Bold ? " gras" : "")}, espacement {l.LetterSpacing}, échelle {l.Scale} %, opacité {l.Opacity} %, image '{(l.BackgroundImage.Length == 0 ? "défaut" : l.BackgroundImage)}', World Boss=({wb.X},{wb.Y})";
    }

    private void ReloadLayout()
    {
        if (!LayoutStore.TryLoad(out var layout))
        {
            Log("layout: lecture impossible, on garde le précédent");
            return;
        }
        _layout = layout;
        FontCatalog.Rescan(); // une police a pu être ajoutée depuis le démarrage
        _renderer.SetBackground(layout.BackgroundImage);
        var windowSize = WindowSize(); // une autre image ou une autre échelle : la fenêtre change de taille
        if (Size != windowSize)
        {
            Size = windowSize;
            Location = FitOnScreen(Location, Size);
        }
        RenderOverlay();
        Log("layout rechargé : " + DescribeLayout(layout));
    }

    // ───────────── Alarmes ─────────────

    private void TickAlerts(TrackedEvent ev, TimeSpan remaining)
    {
        if (!SoundEnabled || !ev.Alerts.Visible || !ev.Alerts.Enabled) return;
        if (remaining <= TimeSpan.Zero) return;

        // Paliers franchis et pas encore tirés (le plus proche de l'event en dernier).
        var crossed = ev.Alerts.Levels
            .Where(l => l.Minutes > 0
                        && remaining <= TimeSpan.FromMinutes(l.Minutes)
                        && !ev.FiredAlertMinutes.Contains(l.Minutes))
            .OrderBy(l => l.Minutes)
            .ToList();

        if (crossed.Count == 0)
        {
            ev.AlertsPrimed = true;
            return;
        }

        foreach (var l in crossed) ev.FiredAlertMinutes.Add(l.Minutes);

        // 1er passage après le lancement / un rechargement de config : les paliers déjà
        // dépassés sont marqués sans jouer de son (sinon ils sonneraient tous d'un coup).
        if (!ev.AlertsPrimed)
        {
            ev.AlertsPrimed = true;
            return;
        }

        // Sourdine rapide : les paliers franchis sont consommés en silence, ils ne sonneront pas à la réactivation.
        if (_alertsMuted) return;

        // « Seulement quand Diablo IV est lancé » : hors jeu, les paliers franchis sont aussi consommés en silence.
        if (ev.Alerts.OnlyInGame && !IsGameRunning()) return;

        // Si plusieurs paliers sont franchis dans le même tick (réveil de veille…), un seul son : le plus proche.
        AlertSounds.Play(crossed[0].Sound, crossed[0].Volume);
    }

    // ───────────── Rendu : image de fond + texte, fenêtre à transparence par pixel ─────────────

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RenderOverlay();
    }

    /// <summary>Publie source / mode / dernière réponse API dans status.json (lu par le configurateur).</summary>
    private void PublishStatus()
    {
        bool? game = NeedsGameCheck() ? IsGameRunning() : null;
        string key = $"{_source}|{_statusText}|{_editMode}|{_overlayVisible}|{_alertsMuted}|{game}|{string.Join(",", _hotkeyFailed.OrderBy(x => x))}|{_configAppliedAt:O}|{_lastApiOk:O}";
        if (key == _lastPublished) return;
        _lastPublished = key;
        StatusStore.Write(new OverlayStatus
        {
            Pid = Environment.ProcessId,
            Source = _source,
            Message = _statusText,
            EditMode = _editMode,
            OverlayVisible = _overlayVisible,
            AlertsMuted = _alertsMuted,
            GameRunning = game,
            HotkeyFailed = _hotkeyFailed.ToArray(),
            ConfigAppliedAt = _configAppliedAt,
            LastApiSuccess = _lastApiOk
        });
    }

    // ───────────── Masquage de l'overlay + icône de notification ─────────────

    /// <summary>Affiche ou masque l'overlay. Masqué : rien n'est dessiné, mais les timers, les events et les alertes sonores continuent.</summary>
    private void SetOverlayVisible(bool visible, bool persist)
    {
        if (_overlayVisible == visible) return;
        _overlayVisible = visible;

        if (!visible && _editMode)
        {
            _editMode = false;
            SetClickThrough(true);
            UnregisterOpacityHotkeys();
        }
        if (persist)
        {
            try
            {
                var settings = SettingsStore.LoadOrDefaults();
                settings.OverlayVisible = visible;
                SettingsStore.Save(settings);
                _settingsStamp = Stamp(SettingsStore.SettingsPath);
            }
            catch (Exception ex) { Log("settings.json non enregistré : " + ex.Message); }
        }
        Log("overlay " + (visible ? "affiché" : "masqué"));
        RenderOverlay();
        PublishStatus();
    }

    // Sans fenêtre visible, il faut une porte d'entrée : icône près de l'horloge pour réafficher, configurer ou quitter.
    private void InitTray()
    {
        try
        {
            var menu = new ContextMenuStrip();
            var toggle = new ToolStripMenuItem();
            var mute = new ToolStripMenuItem();
            var settings = new ToolStripMenuItem();
            var quit = new ToolStripMenuItem();
            toggle.Click += (_, _) => SetOverlayVisible(!_overlayVisible, persist: true);
            mute.Click += (_, _) => SetMuted(!_alertsMuted);
            settings.Click += (_, _) => OpenSettingsApp();
            quit.Click += (_, _) => Close();
            menu.Items.AddRange(new ToolStripItem[] { toggle, mute, settings, new ToolStripSeparator(), quit });

            // Textes relus à chaque ouverture du menu : la langue a pu changer entre-temps.
            menu.Opening += (_, _) =>
            {
                toggle.Text = Loc.T(_overlayVisible ? "tray.hide" : "tray.show");
                mute.Text = Loc.T("tray.mute");
                mute.Checked = _alertsMuted;
                if (_tray is not null) _tray.Text = TrayTip(); // la langue a pu changer
                settings.Text = Loc.T("tray.settings");
                quit.Text = Loc.T("tray.quit");
            };

            Icon icon;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
            catch { icon = SystemIcons.Application; }

            _tray = new NotifyIcon { Icon = icon, Text = "D4 Overlay", ContextMenuStrip = menu, Visible = true };
            _tray.DoubleClick += (_, _) => SetOverlayVisible(!_overlayVisible, persist: true);
        }
        catch (Exception ex) { Log("icône de notification KO : " + ex.Message); }
    }

    /// <summary>Le jeu tourne-t-il ? Résultat gardé 5 s : lister les processus à chaque seconde serait du gaspillage.</summary>
    private bool IsGameRunning()
    {
        if (DateTime.UtcNow - _gameCheckedAt < TimeSpan.FromSeconds(5)) return _gameRunning;
        _gameCheckedAt = DateTime.UtcNow;

        bool running = false;
        try
        {
            foreach (var raw in _gameProcesses)
            {
                string name = (raw ?? "").Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                if (name.Length == 0) continue;

                var found = System.Diagnostics.Process.GetProcessesByName(name);
                bool any = found.Length > 0;
                foreach (var process in found) process.Dispose();
                if (any) { running = true; break; }
            }
        }
        catch (Exception ex) { Log("détection du jeu KO : " + ex.Message); }

        if (running != _gameRunning) Log("Diablo IV " + (running ? "détecté" : "non détecté"));
        _gameRunning = running;
        return running;
    }

    /// <summary>Au moins un event actif utilise l'option « seulement en jeu » : sinon on ne cherche pas le jeu du tout.</summary>
    private bool NeedsGameCheck() => _events.Any(e => e.Alerts.Visible && e.Alerts.Enabled && e.Alerts.OnlyInGame);

    /// <summary>
    /// Démarrage avec Windows : l'overlay connaît son propre chemin, donc il inscrit (ou retire) lui-même l'entrée, à chaque
    /// lancement et à chaque changement de réglage. Un dossier déplacé est ainsi recalé dès le prochain lancement.
    /// </summary>
    private static void ApplyStartWithWindows(bool enabled)
    {
        try { StartupRegistration.Apply(enabled, Environment.ProcessPath); }
        catch (Exception ex) { Log("démarrage avec Windows : " + ex.Message); }
    }

    /// <summary>Texte au survol de l'icône (63 caractères maximum) : indique aussi la sourdine, pour ne pas l'oublier.</summary>
    private string TrayTip() => _alertsMuted ? Loc.T("tray.mutedTip") : "D4 Overlay";

    /// <summary>Sourdine rapide (appel vocal, stream…) : coupe les sons sans toucher aux réglages ni à l'affichage.</summary>
    private void SetMuted(bool muted)
    {
        if (_alertsMuted == muted) return;
        _alertsMuted = muted;
        if (_tray is not null) _tray.Text = TrayTip();
        Log("sourdine " + (muted ? "activée" : "désactivée"));
        PublishStatus();
    }

    private void OpenSettingsApp()
    {
        try
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "D4OverlayConfig.exe");
            if (File.Exists(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            else
                MessageBox.Show(Loc.T("tray.noConfig"), "D4 Overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { Log("ouverture de la configuration : " + ex.Message); }
    }

    /// <summary>
    /// Opacité appliquée à toute la fenêtre (0-255), d'après le réglage en %. En mode déplacement (F8) l'overlay est
    /// toujours totalement opaque : à 20 % il serait quasi introuvable pour le replacer.
    /// </summary>
    private byte CurrentAlpha()
        => _editMode && DateTime.UtcNow >= _opacityPreviewUntil
            ? (byte)255
            : (byte)Math.Round(255 * Math.Clamp(_layout.Opacity, 20, 100) / 100.0);

    /// <summary>Taille de la fenêtre : l'image du cadre multipliée par l'échelle choisie (50 à 300 %).</summary>
    private Size WindowSize()
    {
        var frame = _renderer.FrameSize;
        float k = Math.Clamp(_layout.Scale, 50, 300) / 100f;
        return new Size(Math.Max(1, (int)Math.Round(frame.Width * k)), Math.Max(1, (int)Math.Round(frame.Height * k)));
    }

    // Petite croix en haut à droite, visible et cliquable uniquement en mode déplacement (F8).
    private static Rectangle CloseButtonRect(Size frame) => new(frame.Width - 32, 8, 22, 22);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_editMode) return;
        bool hover = CloseButtonRect(ClientSize).Contains(e.Location);
        if (hover == _closeHover) return;
        _closeHover = hover;
        Cursor = hover ? Cursors.Hand : Cursors.Default;
        RenderOverlay();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_closeHover) return;
        _closeHover = false;
        RenderOverlay();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_editMode && e.Button == MouseButtons.Left && CloseButtonRect(ClientSize).Contains(e.Location))
        {
            Log("fermeture demandée via la croix");
            Close();
            return; // pas de base : on ne démarre pas un déplacement
        }
        base.OnMouseDown(e);
    }

    private void RenderOverlay()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(new Action(RenderOverlay)); return; }

        // Overlay masqué : on pose une fois une image entièrement transparente, puis on ne dessine plus rien.
        if (!_overlayVisible)
        {
            if (!_blankPushed)
            {
                var blankSize = WindowSize();
                using var blank = new Bitmap(blankSize.Width, blankSize.Height, PixelFormat.Format32bppPArgb);
                PushToScreen(blank);
                _blankPushed = true;
            }
            return;
        }
        _blankPushed = false;

        var size = WindowSize();
        using var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);

            var lines = _events
                .Where(e => e.Alerts.Visible)
                .Select(e =>
                {
                    var p = _layout.GetPlacement(e.Label);
                    return new TimerLine(e.Text, _layout.GetTimerColor(e.Label), p.X, p.Y);
                })
                .ToList();
            _renderer.Draw(g, _layout, lines, _layout.Scale / 100f);

            if (!_editMode) _closeHover = false;
            bool showBadge = DateTime.UtcNow < _badgeUntil && _badgeText.Length > 0;
            if (_editMode)
            {
                // Mode déplacement (F8) : cadre pointillé bien visible + croix pour fermer l'application.
                using var pen = new Pen(Color.Cyan, 2f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, 1, 1, size.Width - 3, size.Height - 3);

                var r = CloseButtonRect(size);
                using var fill = new SolidBrush(_closeHover ? Color.FromArgb(240, 225, 40, 40) : Color.FromArgb(225, 140, 20, 20));
                g.FillRectangle(fill, r);
                using var border = new Pen(Color.White, 1f);
                g.DrawRectangle(border, r);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var cross = new Pen(Color.White, 2f);
                g.DrawLine(cross, r.Left + 6, r.Top + 6, r.Right - 6, r.Bottom - 6);
                g.DrawLine(cross, r.Right - 6, r.Top + 6, r.Left + 6, r.Bottom - 6);
            }

            if (showBadge) DrawBadge(g, size, _badgeText);
        }
        PushToScreen(frame);
    }

    private void PushToScreen(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            oldBmp = SelectObject(memDc, hBmp);

            var size = new NativeSize { Cx = bmp.Width, Cy = bmp.Height };
            var src = new NativePoint();
            var dst = new NativePoint { X = Left, Y = Top };
            var blend = new BlendFunction
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = CurrentAlpha(),
                AlphaFormat = AC_SRC_ALPHA
            };
            if (!UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA))
                Log("UpdateLayeredWindow KO, erreur " + Marshal.GetLastWin32Error());
        }
        finally
        {
            if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static string Fmt(TimeSpan t)
        => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
        _statusText = text;
        RenderOverlay();
        PublishStatus();
    }

    private static string ShortError(Exception ex)
        => ex.Message.Length > 70 ? ex.Message[..70] + "…" : ex.Message;

    /// <summary>
    /// Une position sauvegardée peut ne plus exister (écran débranché, résolution ou disposition changées) : l'overlay
    /// serait alors invisible. Si un coin de la fenêtre sort de tous les écrans, on la ramène dans l'écran le plus proche.
    /// Une fenêtre à cheval sur deux écrans, dont les quatre coins sont visibles, reste où elle est.
    /// </summary>
    private static Point FitOnScreen(Point location, Size size)
    {
        try
        {
            var screens = Screen.AllScreens;
            bool Visible(int x, int y) => screens.Any(s => s.Bounds.Contains(x, y));
            var r = new Rectangle(location, size);
            if (Visible(r.Left, r.Top) && Visible(r.Right - 1, r.Top)
                && Visible(r.Left, r.Bottom - 1) && Visible(r.Right - 1, r.Bottom - 1))
                return location;

            var area = Screen.FromRectangle(r).WorkingArea; // l'écran le plus proche
            var fitted = new Point(
                Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
                Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
            if (fitted != location) Log($"position corrigée : {location} → {fitted} (la fenêtre sortait des écrans)");
            return fitted;
        }
        catch { return location; }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            var fitted = FitOnScreen(Location, Size);
            if (fitted == Location) return;
            Location = fitted;
            SaveSavedPosition(Location);
            RenderOverlay();
        }));
    }

    private static Point LoadSavedPosition()
    {
        try
        {
            var parts = File.ReadAllText(PosPath).Split(',');
            return new Point(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        catch { return new Point(100, 100); }
    }

    private static void SaveSavedPosition(Point p)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(PosPath, $"{p.X},{p.Y}");
        }
        catch { }
    }
}