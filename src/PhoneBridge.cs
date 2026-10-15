using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PhoneOverhaul.API;
using UnityEngine;
using UnityEngine.UI;

namespace PhoneBridge
{
    // Lets Phone - Overhaul and QOL ShopOS run side by side:
    // - ShopOS's own pages (Music, Lights, Trades, Mod Config, Reviews, Rent, ...) open inside Phone Overhaul
    //   as normal phone apps. ShopOS draws them itself; the bridge only hands it the phone's page container.
    // - ShopOS is a device for Phone Overhaul ("Phone Key Opens = OtherTablet" hands it the Tab key), and a
    //   "Phone Overhaul" app on the tablet leads to the phone (official ShopOS API).
    // Agreed with the ShopOS author: no ShopOS method is patched and no ShopOS setting is changed.
    [BepInPlugin(Guid, "Phone Bridge - ShopOS x Phone Overhaul", "1.0.0")]
    [BepInDependency("munch.PhoneOverhaul")]
    [BepInDependency("com.qolmods.shopos")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.phonebridge";
        const string DeviceId = "shopos";

        const string DefaultApps =
            "Music; Lights; Trades; ModConfig; CustomerReview; RentBill; GameEvent; PriceCheck; BuyDecoration; ExpandShop; Setting";

        static readonly Dictionary<string, string> DefaultNames = new Dictionary<string, string>
        {
            { "Music", "Music" }, { "Lights", "Lights" }, { "Trades", "Trades" }, { "ModConfig", "Mod Config" },
            { "CustomerReview", "Reputation" }, { "RentBill", "Rent" }, { "GameEvent", "Events" },
            { "PriceCheck", "Price Check" }, { "BuyDecoration", "Decoration" }, { "ExpandShop", "Expansion" },
            { "Setting", "ShopOS" }, { "Wallpaper", "Wallpaper" }, { "PhotoGallery", "Gallery" },
            { "HostTournament", "Tournament" }, { "Hiring", "Hiring" }, { "ScannerOrder", "Scanner" },
        };

        internal static Plugin Inst;
        internal static BepInEx.Logging.ManualLogSource Log;
        static ConfigEntry<bool> _poApp;
        static ConfigEntry<string> _apps;
        static ConfigEntry<string> _language;

        // ShopOS internals (all internal types, reached through reflection)
        static Type _tablet, _osApps, _osApp, _osPage, _osDesktop;
        static FieldInfo _phase, _canvas, _desktopField, _musicPage;
        static MethodInfo _close, _report, _openOnPage, _buildTablet, _current, _pageFor, _openMusic, _closePage;
        static PropertyInfo _isOpen;
        static FieldInfo _appKey, _appPage, _pgBuild, _pgShow, _pgHide, _pgTick, _pgRoot;

        // pending "open the whole tablet" request from the phone
        static float _nextSweep, _nextSettings;
        static readonly List<string> _embeddedKeys = new List<string>();
        static bool _pending;
        static float _pendingSince, _closedSince;
        static bool _exitSent;

        static readonly Color Navy = new Color32(18, 24, 44, 255);
        static readonly Vector2 Design = new Vector2(1800f, 980f);

        class Embedded
        {
            public string Key;
            public object Page;
            public RectTransform Holder;
            public PhoneAppPage Phone;
            public bool Open;
        }
        static readonly List<Embedded> _embedded = new List<Embedded>();

        string _dir;
        string IconDir { get { return Path.Combine(_dir, "icons"); } }

        void Awake()
        {
            Inst = this;
            Log = Logger;
            _dir = Path.GetDirectoryName(Info.Location);
            _apps = Config.Bind("General", "ShopOSApps", DefaultApps,
                "ShopOS pages that open inside Phone Overhaul as their own phone app, separated by ';'. " +
                "Write Key=Name to give one another name. Other keys: Wallpaper, PhotoGallery, HostTournament, Hiring, ScannerOrder. " +
                "Empty = none. Takes effect after a game restart.");
            _poApp = Config.Bind("General", "PhoneOverhaulApp", true,
                "A 'Phone Overhaul' app on the ShopOS tablet that opens the Phone Overhaul tablet. Takes effect after a game restart.");
            _language = Config.Bind("General", "Language", "auto",
                "Translation for the ShopOS pages on the phone. auto = use the lang_*.txt file next to the mod if there is one, " +
                "en = always English, de (or any code) = use lang_<code>.txt. Takes effect after a game restart.");
            Translator.Load(_dir, _language.Value, Logger);
            // read by PhoneBridge.Preloader before Phone Overhaul starts; bound here so it shows in the config
            Config.Bind("Layout", "ApplyFolderPreset", false,
                "The Phone Overhaul home screen gets tidy folders (Stock, Shop, Management, Events, Tools, Duplicates) once on the first " +
                "start with the bridge; your own changes afterwards are kept. Set to true and restart the game to get the folders again. " +
                "A backup of the old layout is made, apps not in the preset are kept. Switches itself off afterwards.");

            _tablet = AccessTools.TypeByName("ShopOS.Tablet");
            _osApps = AccessTools.TypeByName("ShopOS.OsApps");
            _osApp = AccessTools.TypeByName("ShopOS.OsApp");
            _osPage = AccessTools.TypeByName("ShopOS.OsPage");
            _osDesktop = AccessTools.TypeByName("ShopOS.OsDesktop");
            if (_tablet == null) { Logger.LogError("ShopOS.Tablet not found - bridge off."); return; }

            _phase = AccessTools.Field(_tablet, "_phase");
            _canvas = AccessTools.Field(_tablet, "_canvas");
            _desktopField = AccessTools.Field(_tablet, "_desktop");
            _close = AccessTools.Method(_tablet, "CloseTablet");
            _report = AccessTools.Method(_tablet, "Report");
            _openOnPage = AccessTools.Method(_tablet, "OpenOnPage");
            _buildTablet = AccessTools.Method(_tablet, "Build");
            _isOpen = AccessTools.Property(_tablet, "IsOpen");
            if (_osApps != null) _current = AccessTools.Method(_osApps, "Current");
            if (_osDesktop != null)
            {
                _pageFor = AccessTools.Method(_osDesktop, "PageFor");
                _openMusic = AccessTools.Method(_osDesktop, "OpenMusic");
                _closePage = AccessTools.Method(_osDesktop, "ClosePage");
                _musicPage = AccessTools.Field(_osDesktop, "_musicPage");
            }
            if (_osApp != null) { _appKey = AccessTools.Field(_osApp, "Key"); _appPage = AccessTools.Field(_osApp, "Page"); }
            if (_osPage != null)
            {
                _pgBuild = AccessTools.Field(_osPage, "Build");
                _pgShow = AccessTools.Field(_osPage, "Show");
                _pgHide = AccessTools.Field(_osPage, "Hide");
                _pgTick = AccessTools.Field(_osPage, "Tick");
                _pgRoot = AccessTools.Field(_osPage, "Root");
            }

            if (_phase == null)
            {
                Logger.LogError("ShopOS internals changed (phase missing) - bridge off.");
                _tablet = null;
            }
        }

        void Start()
        {
            if (_tablet == null) return;
            try { ExtractShopOSArt(); } catch (Exception e) { Logger.LogWarning("icons: " + e.Message); }
            try { RegisterEmbeddedApps(); } catch (Exception e) { Logger.LogError("phone apps: " + e); }
            // Phone Overhaul shows the device tile only with its "Show Tablet App" setting
            try { RegisterTabletOnPhone(); } catch (Exception e) { Logger.LogError("phone device: " + e); }
            if (_poApp.Value)
                try { RegisterAppOnTablet(); } catch (Exception e) { Logger.LogError("tablet app: " + e); }
        }

        static int Phase()
        {
            try { return Convert.ToInt32(_phase.GetValue(null)); } catch { return 0; }
        }

        static bool TabletOpen()
        {
            try { return _isOpen != null ? (bool)_isOpen.GetValue(null, null) : Phase() != 0; } catch { return false; }
        }

        // =====================================================================
        // ShopOS pages inside Phone Overhaul
        // =====================================================================

        // ShopOS ships its app art as embedded PNGs (icon_music.png, shopos_logo.png, ...).
        // Copy them out once so Phone Overhaul can load them by path.
        void ExtractShopOSArt()
        {
            Directory.CreateDirectory(IconDir);
            Assembly so = _tablet.Assembly;
            foreach (string res in so.GetManifestResourceNames())
            {
                if (!res.EndsWith(".png") || !(res.StartsWith("icon_") || res == "shopos_logo.png")) continue;
                string file = Path.Combine(IconDir, res);
                using (Stream s = so.GetManifestResourceStream(res))
                {
                    if (s == null) continue;
                    if (File.Exists(file) && new FileInfo(file).Length == s.Length) continue;
                    using (FileStream f = File.Create(file)) s.CopyTo(f);
                }
            }
        }

        string ShopOSIcon(string appKey)
        {
            string name = appKey;
            try
            {
                var map = AccessTools.Field(_osApps, "IconNames").GetValue(null) as Dictionary<string, string>;
                string n;
                if (map != null && map.TryGetValue(appKey, out n)) name = n;
            }
            catch { }
            string file = Path.Combine(IconDir, "icon_" + name + ".png");
            return File.Exists(file) ? file : null;
        }

        void RegisterEmbeddedApps()
        {
            var reg = PhoneOverhaulAPI.Registry;
            if (reg == null) { Logger.LogError("Phone Overhaul registry not ready."); return; }
            if (_osPage == null || _pgBuild == null || _pgRoot == null || _current == null || _pageFor == null)
            {
                Logger.LogError("ShopOS page internals changed - embedded apps off.");
                return;
            }
            foreach (string part in (_apps.Value ?? "").Split(';'))
            {
                string entry = part.Trim();
                if (entry.Length == 0) continue;
                int eq = entry.IndexOf('=');
                string key = (eq < 0 ? entry : entry.Substring(0, eq)).Trim();
                if (key.Length == 0) continue;
                string name = eq >= 0 ? entry.Substring(eq + 1).Trim() : null;
                if (string.IsNullOrEmpty(name)) name = Translator.AppName(key);
                if (string.IsNullOrEmpty(name) && !DefaultNames.TryGetValue(key, out name)) name = key;

                var e = new Embedded { Key = key };
                _embedded.Add(e);
                _embeddedKeys.Add(key);
                var spec = new ScreenAppSpec
                {
                    App = new AppSpec { AppId = "shopos." + key, DisplayName = name, Icon = ShopOSIcon(key) },
                    Title = name,
                    OnOpen = p => OpenEmbedded(e, p),
                    OnClose = () => CloseEmbedded(e),
                };
                var h = reg.RegisterScreenApp(spec);
                Logger.LogInfo("phone app '" + name + "' (ShopOS " + key + ") registered: " + h);
            }
        }

        // ShopOS pages expect its tablet (fonts, desktop, page size) to exist. Build it hidden.
        static object Desktop()
        {
            object d = _desktopField != null ? _desktopField.GetValue(null) : null;
            if (d != null) return d;
            if (_buildTablet == null) return null;
            _buildTablet.Invoke(null, null);
            if (Phase() == 0 && _canvas != null)
            {
                var c = _canvas.GetValue(null) as GameObject;
                if (c != null) c.SetActive(false);
            }
            return _desktopField != null ? _desktopField.GetValue(null) : null;
        }

        // Always a NEW ShopOS page object: a page whose widgets were destroyed keeps stale references
        // (chips, rows, toggles) and throws on Show.
        static object NewPage(string key)
        {
            object desk = Desktop();
            if (key == "Music")
            {
                if (desk == null || _musicPage == null || _openMusic == null) return null;
                ResetMusic(desk);
                // let ShopOS make its music page the normal way, then put it back
                _openMusic.Invoke(desk, null);
                if (_closePage != null) _closePage.Invoke(desk, null);
                return _musicPage.GetValue(desk);
            }
            var apps = _current.Invoke(null, null) as System.Collections.IEnumerable;
            if (apps == null) return null;
            foreach (object app in apps)
            {
                if ((string)_appKey.GetValue(app) != key) continue;
                var make = _appPage.GetValue(app) as Delegate;
                if (make == null) return null;   // no ShopOS page for this app
                return make.DynamicInvoke();
            }
            return null;
        }

        // The music page lives on the ShopOS desktop, which keeps its queue and speaker rows in lists
        // that are not cleared when the page is built again.
        static void ResetMusic(object desk)
        {
            object old = _musicPage.GetValue(desk);
            _musicPage.SetValue(desk, null);
            foreach (string f in new[] { "_queueRows", "_speakRows" })
            {
                var list = FieldValue(desk, f) as System.Collections.IList;
                if (list != null) list.Clear();
            }
            var built = FieldValue(desk, "_builtPages") as System.Collections.IList;
            if (built != null && old != null) built.Remove(old);
            SetInt(desk, "_musicShown", -1);
            SetInt(desk, "_queueCountShown", -1);
            SetInt(desk, "_queueIndexShown", -2);
        }

        static void SetOpenPage(object page, bool open)
        {
            try
            {
                object desk = _desktopField != null ? _desktopField.GetValue(null) : null;
                var f = desk != null ? AccessTools.Field(_osDesktop, "_openPage") : null;
                if (f == null) return;
                if (open) f.SetValue(desk, page);
                else if (f.GetValue(desk) == page) f.SetValue(desk, null);
            }
            catch (Exception ex) { Inst.Logger.LogWarning("music page state: " + ex.Message); }
        }

        static object FieldValue(object o, string field)
        {
            var f = AccessTools.Field(o.GetType(), field);
            return f != null ? f.GetValue(o) : null;
        }

        static void SetInt(object o, string field, int v)
        {
            var f = AccessTools.Field(o.GetType(), field);
            if (f != null && f.FieldType == typeof(int)) f.SetValue(o, v);
        }

        // Pages are parked here while their phone app is closed, because Phone Overhaul destroys the app's content.
        static Transform _park;

        static Transform Park()
        {
            if (_park == null)
            {
                var go = new GameObject("PhoneBridge_Parked");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.SetActive(false);
                _park = go.transform;
            }
            return _park;
        }

        static bool Alive(Embedded e)
        {
            if (e.Page == null || e.Holder == null) return false;
            var root = _pgRoot.GetValue(e.Page) as RectTransform;
            return root != null && root.IsChildOf(e.Holder);
        }

        static void OpenEmbedded(Embedded e, PhoneAppPage p)
        {
            e.Phone = p;
            // "Classic screens" in the ShopOS settings: this app opens the game's own screen instead
            if (Settings.IsClassic(e.Key) && OpenClassic(e.Key, p)) return;
            try
            {
                bool fresh = !Alive(e);
                if (fresh)
                {
                    if (e.Holder != null) UnityEngine.Object.Destroy(e.Holder.gameObject);
                    e.Holder = null;
                    e.Page = NewPage(e.Key);
                    if (e.Page == null)
                    {
                        PhoneUi.Text(p.Content, "Missing", "ShopOS has no page for '" + e.Key + "'.", 28f, Color.white, TMPro.TextAlignmentOptions.Center);
                        return;
                    }
                    var go = new GameObject("ShopOS_" + e.Key, typeof(RectTransform), typeof(Image));
                    e.Holder = (RectTransform)go.transform;
                    go.GetComponent<Image>().color = Navy;
                }

                e.Holder.SetParent(p.Content, false);
                e.Holder.anchorMin = e.Holder.anchorMax = new Vector2(0.5f, 1f);
                e.Holder.pivot = new Vector2(0.5f, 1f);
                e.Holder.sizeDelta = Design;
                e.Holder.localScale = Vector3.one;
                e.Holder.gameObject.SetActive(true);

                var root = _pgRoot.GetValue(e.Page) as RectTransform;
                if (root == null)
                {
                    root = new GameObject("Page_" + e.Key, typeof(RectTransform)).GetComponent<RectTransform>();
                    Stretch(root, e.Holder);
                    _pgRoot.SetValue(e.Page, root);
                    var build = _pgBuild.GetValue(e.Page) as Action<RectTransform>;
                    if (build != null) build(root);
                }
                else
                {
                    Stretch(root, e.Holder);   // music: built on the hidden ShopOS desktop, moved here
                }
                root.gameObject.SetActive(true);

                // ShopOS only refreshes its music page while it counts as the open page
                if (e.Key == "Music") SetOpenPage(e.Page, true);

                var show = _pgShow.GetValue(e.Page) as Action;
                if (show != null) show();
                e.Open = true;
                Fit(e);
                FixMasks(e.Holder);
                Translator.Apply(e.Holder);
                LogCanvasChainOnce(e.Holder);
            }
            catch (Exception ex)
            {
                Inst.Logger.LogError("open ShopOS " + e.Key + ": " + ex);
                e.Page = null;   // build a fresh one next time
            }
        }

        // Presses the game's own phone button for a ShopOS app (ShopOS knows which one: OsApp.Button).
        static bool OpenClassic(string key, PhoneAppPage p)
        {
            try
            {
                var apps = _current.Invoke(null, null) as System.Collections.IEnumerable;
                var press = AccessTools.Method(_osApps, "Press");
                var button = AccessTools.Field(_osApp, "Button");
                if (apps == null || press == null || button == null) return false;
                foreach (object app in apps)
                {
                    if ((string)_appKey.GetValue(app) != key) continue;
                    string b = button.GetValue(app) as string;
                    if (string.IsNullOrEmpty(b)) return false;   // no classic screen for this one
                    PhoneUi.Text(p.Content, "Classic", Translator.Translate("Opening the game's own screen..."), 28f, Color.white, TMPro.TextAlignmentOptions.Center);
                    press.Invoke(null, new object[] { b });
                    return true;
                }
            }
            catch (Exception ex) { Log.LogWarning("classic screen " + key + ": " + ex.Message); }
            return false;
        }

        static void CloseEmbedded(Embedded e)
        {
            if (!e.Open) return;
            e.Open = false;
            try
            {
                var hide = _pgHide.GetValue(e.Page) as Action;
                if (hide != null) hide();
            }
            catch (Exception ex) { Inst.Logger.LogWarning("close ShopOS " + e.Key + ": " + ex.Message); }
            if (e.Key == "Music") SetOpenPage(e.Page, false);
            // take the page out before Phone Overhaul destroys the app's content
            try { if (e.Holder != null) e.Holder.SetParent(Park(), false); }
            catch (Exception ex) { Inst.Logger.LogWarning("park ShopOS " + e.Key + ": " + ex.Message); }
        }

        // ShopOS clips its lists, search fields and pages with RectMask2D. RectMask2D clips in the space of the
        // topmost canvas, and inside Phone Overhaul that space does not line up with the page (rows come out cut
        // diagonally or vanish). A stencil Mask clips on the drawn shape itself, so swap it in.
        static readonly HashSet<int> _fixedMasks = new HashSet<int>();

        static void FixMasks(Transform root)
        {
            if (root == null) return;
            foreach (var rm in root.GetComponentsInChildren<RectMask2D>(true))
            {
                if (!_fixedMasks.Add(rm.GetInstanceID()) && !rm.enabled) continue;
                rm.enabled = false;
                var go = rm.gameObject;
                bool hidden = false;
                var g = go.GetComponent<Graphic>();
                if (g == null)
                {
                    var img = go.AddComponent<Image>();
                    img.color = Color.white;
                    img.raycastTarget = false;
                    hidden = true;
                }
                else if (g.color.a < 0.02f)
                {
                    // a fully transparent mask graphic writes no stencil and hides every child;
                    // make it opaque but do not draw it
                    var c = g.color; c.a = 1f; g.color = c;
                    hidden = true;
                }
                var m = go.GetComponent<Mask>();
                if (m == null) m = go.AddComponent<Mask>();
                m.showMaskGraphic = !hidden;
            }
        }

        static bool _chainLogged;

        static void LogCanvasChainOnce(Transform t)
        {
            if (_chainLogged || t == null) return;
            _chainLogged = true;
            var sb = new System.Text.StringBuilder("page parents (for support):");
            for (Transform p = t; p != null; p = p.parent)
            {
                var c = p.GetComponent<Canvas>();
                sb.Append("\n  ").Append(p.name)
                  .Append(" rot=").Append(p.localEulerAngles)
                  .Append(" scale=").Append(p.localScale);
                if (c != null) sb.Append(" CANVAS ").Append(c.renderMode).Append(c.isRootCanvas ? " root" : " nested");
                if (p.GetComponent<RectMask2D>() != null) sb.Append(" RectMask2D");
                if (p.GetComponent<Mask>() != null) sb.Append(" Mask");
            }
            Inst.Logger.LogInfo(sb.ToString());
        }

        static void Stretch(RectTransform rt, Transform parent)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        // Scale the ShopOS page (laid out at tablet size) to fit the phone's page area.
        static void Fit(Embedded e)
        {
            if (e.Holder == null || e.Phone == null || e.Phone.Content == null) return;
            var view = e.Phone.Content.parent as RectTransform;
            if (view == null) view = e.Phone.Content;
            Vector2 size = view.rect.size;
            if (size.x < 10f || size.y < 10f) return;
            float s = Mathf.Min(size.x / Design.x, size.y / Design.y);
            e.Holder.localScale = new Vector3(s, s, 1f);
            e.Holder.anchoredPosition = new Vector2(0f, -(size.y - Design.y * s) * 0.5f);
            e.Phone.SetContentHeight(size.y);
        }

        // =====================================================================
        // Optional: whole ShopOS tablet as a phone app
        // =====================================================================

        void RegisterTabletOnPhone()
        {
            var reg = PhoneOverhaulAPI.Registry;
            if (reg == null) return;
            string icon = Path.Combine(IconDir, "shopos_logo.png");
            var spec = new DeviceSpec
            {
                DeviceId = DeviceId,
                PluginGuid = Guid,
                DisplayName = "ShopOS",
                Icon = File.Exists(icon) ? icon : null,
                IsOpen = TabletOpen,
                Open = RequestTablet,
                Close = CloseTablet,
            };
            Logger.LogInfo("phone device 'ShopOS' registered: " + reg.RegisterDevice(spec));
        }

        void RegisterAppOnTablet()
        {
            Sprite icon = LoadSprite("tablet_icon.png");
            // app id stays "phone" so the tile keeps its place on the ShopOS home screen
            bool ok = ShopOS.ShopOSApi.RegisterApp(Guid, "phone", "Phone Overhaul", icon, new Action(OpenPhone));
            Logger.LogInfo("tablet app 'Phone Overhaul' registered: " + ok);
        }

        // ShopOS closes the tablet before this runs.
        static void OpenPhone()
        {
            try
            {
                var reg = PhoneOverhaulAPI.Registry;
                if (reg != null && reg.OpenPhone()) return;
            }
            catch (Exception e) { Inst.Logger.LogWarning("OpenPhone via API failed: " + e.Message); }
            try { PhoneManager.EnterPhoneMode(); } catch (Exception e) { Inst.Logger.LogWarning("EnterPhoneMode: " + e.Message); }
        }

        static readonly FieldInfo PhoneInst = AccessTools.Field(typeof(PhoneManager), "m_Instance");
        static readonly FieldInfo PhoneModeField = AccessTools.Field(typeof(PhoneManager), "m_IsPhoneMode");
        static readonly FieldInfo SaveLoaded = AccessTools.Field(typeof(GameInstance), "m_FinishedSavefileLoading");

        static bool IsPhoneMode()
        {
            try
            {
                var pm = PhoneInst.GetValue(null);
                return pm != null && (bool)PhoneModeField.GetValue(pm);
            }
            catch { return false; }
        }

        static bool RequestTablet()
        {
            try { if (!(bool)SaveLoaded.GetValue(null)) return false; } catch { }
            _pending = true;
            _pendingSince = Time.unscaledTime;
            _closedSince = -1f;
            _exitSent = false;
            return true;
        }

        static void CloseTablet()
        {
            _pending = false;
            try { if (_close != null) _close.Invoke(null, null); } catch { }
        }

        void Update()
        {
            float now = Time.unscaledTime;

            // keep embedded pages sized and ticking; ShopOS rebuilds rows, so masks and texts are checked again
            bool sweep = now >= _nextSweep;
            if (sweep) _nextSweep = now + 0.15f;
            if (now >= _nextSettings)
            {
                _nextSettings = now + 0.5f;
                try { Settings.Tick(_embeddedKeys); } catch (Exception ex) { Logger.LogWarning("settings tick: " + ex.Message); }
            }
            for (int i = 0; i < _embedded.Count; i++)
            {
                var e = _embedded[i];
                if (!e.Open) continue;
                if (e.Holder == null || e.Phone == null || !e.Phone.IsOpen) { CloseEmbedded(e); continue; }
                Fit(e);
                try
                {
                    var tick = _pgTick.GetValue(e.Page) as Action<float>;
                    if (tick != null) tick(now);
                }
                catch (Exception ex) { Logger.LogWarning("ShopOS " + e.Key + " tick: " + ex.Message); }
                if (sweep)
                {
                    try { FixMasks(e.Holder); }
                    catch (Exception ex) { Logger.LogWarning("ShopOS " + e.Key + " sweep: " + ex.Message); }
                }
            }

            if (!_pending) return;
            // the phone has to be fully put away before ShopOS can take the player
            if (now - _pendingSince > 4f) { _pending = false; Logger.LogWarning("tablet open timed out (phone did not close)."); return; }
            if (IsPhoneMode())
            {
                _closedSince = -1f;
                if (!_exitSent && now - _pendingSince > 0.1f)
                {
                    _exitSent = true;
                    try { PhoneManager.SetCanClosePhone(true); PhoneManager.ExitPhoneMode(); } catch (Exception e) { Logger.LogWarning("ExitPhoneMode: " + e.Message); }
                }
                return;
            }
            if (_closedSince < 0f) { _closedSince = now; return; }
            if (now - _closedSince < 0.25f) return;

            _pending = false;
            if (Phase() != 0) return;
            try
            {
                if (_openOnPage != null) { _openOnPage.Invoke(null, new object[] { null, false }); return; }
                var player = CSingleton<InteractionPlayerController>.Instance;
                if (player == null) return;
                if (_report != null) _report.Invoke(null, null);
                AccessTools.Method(_tablet, "Open").Invoke(null, new object[] { player });
            }
            catch (Exception e) { Logger.LogError("open tablet: " + e); }
        }

        // Translate after ShopOS has written its texts for this frame, so no English frame shows (no flicker).
        void LateUpdate()
        {
            if (!Translator.Active) return;
            for (int i = 0; i < _embedded.Count; i++)
            {
                var e = _embedded[i];
                if (!e.Open || e.Holder == null) continue;
                try { Translator.Apply(e.Holder); }
                catch (Exception ex) { Logger.LogWarning("translate " + e.Key + ": " + ex.Message); }
            }
        }

        // icon embedded in PhoneBridge.dll (build.ps1 adds it as a resource)
        static Sprite LoadSprite(string resource)
        {
            byte[] data;
            using (Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null) return null;
                data = new byte[s.Length];
                int read = 0, n;
                while (read < data.Length && (n = s.Read(data, read, data.Length - read)) > 0) read += n;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(tex, data)) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
        }
    }
}
