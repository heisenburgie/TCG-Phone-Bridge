using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Newtonsoft.Json.Linq;

namespace PhoneBridge.Preloader
{
    // BepInEx preloader patcher: runs before any plugin, so Phone Overhaul has not read its save yet.
    // When [Layout] ApplyFolderPreset = true in local.phonebridge.cfg, it writes a tidy home screen into
    // Phone Overhaul's save (backup first) and switches the option off again.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { return new string[0]; } }
        public static void Patch(AssemblyDefinition assembly) { }

        static ManualLogSource Log;

        public static void Initialize()
        {
            Log = Logger.CreateLogSource("PhoneBridge.Preloader");
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromManaged;
            try { FolderPreset.RunIfRequested(Log); }
            catch (Exception e) { Log.LogError("folder preset: " + e); }
        }

        static Assembly ResolveFromManaged(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name + ".dll";
            string file = Path.Combine(Paths.ManagedPath, name);
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        }
    }

    internal static class FolderPreset
    {
        const string Cfg = "local.phonebridge.cfg";
        const string Option = "ApplyFolderPreset";

        class Folder
        {
            public string Name; public int Slot; public string[] Apps;
            public Folder(string name, int slot, params string[] apps) { Name = name; Slot = slot; Apps = apps; }
        }

        // Home screen for a fresh start. Slots: 9 per row.
        // The home screen grid is 9 wide and 4 high (slot = row * 9 + column). Folders down the left edge,
        // single apps down the right edge, the middle stays free for the wallpaper.
        static readonly Folder[] Folders =
        {
            new Folder("Stock", 0, "api:OnePurchase", "vanilla:PhoneButtonGrp_RestockBoardGame", "vanilla:PhoneButtonGrp_ScannerOrder"),
            new Folder("Shop", 9, "vanilla:PhoneButtonGrp_Furniture", "api:shopos.ExpandShop", "api:shopos.BuyDecoration", "api:shopos.Lights"),
            new Folder("Management", 18, "api:shopos.RentBill", "vanilla:PhoneButtonGrp_Hiring", "api:shopos.CustomerReview"),
            new Folder("Events", 27, "api:shopos.GameEvent", "vanilla:PhoneButtonGrp_HostTournament"),
            new Folder("Tools", 28, "vanilla:PhoneButtonGrp_Setting", "api:shopos.Setting", "api:shopos.ModConfig", "api:Icons", "api:Notifications",
                       "vanilla:PhoneButtonGrp_CameraTakePhoto", "vanilla:PhoneButtonGrp_PhotoGallery", "api:Wallpapers", "api:BepInEX Converter"),
            new Folder("Duplicates", 35, "vanilla:PhoneButtonGrp_RentBill", "vanilla:PhoneButtonGrp_GameEvent", "vanilla:PhoneButtonGrp_PriceCheck",
                       "vanilla:PhoneButtonGrp_BuyDecoration", "vanilla:PhoneButtonGrp_ExpandShop", "vanilla:PhoneButtonGrp_CustomerReview"),
        };

        static readonly Dictionary<string, int> Singles = new Dictionary<string, int>
        {
            { "api:Pokemon Shop", 1 }, { "api:CollectionTracker", 3 }, { "vanilla:PhoneButtonGrp_Grading", 7 }, { "vanilla:PhoneButtonGrp_Restock", 8 },
            { "api:shopos.Trades", 17 }, { "api:shopos.Music", 26 }, { "api:shopos.PriceCheck", 34 },
        };

        public static void RunIfRequested(ManualLogSource log)
        {
            // applied once on its own (first start with the bridge), afterwards only when asked for in the config,
            // so a layout the player changed afterwards stays as it is
            string cfg = Path.Combine(Paths.ConfigPath, Cfg);
            string done = Path.Combine(Paths.ConfigPath, "local.phonebridge.preset_done");
            string[] lines = File.Exists(cfg) ? File.ReadAllLines(cfg) : new string[0];
            int at = Array.FindIndex(lines, l => l.Trim().StartsWith(Option + " ", StringComparison.Ordinal) || l.Trim().StartsWith(Option + "=", StringComparison.Ordinal));
            bool asked = at >= 0 && lines[at].Split('=')[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (!asked && File.Exists(done)) return;

            string save = SavePath();
            if (save == null || !File.Exists(save))
            {
                log.LogInfo("folder preset: Phone Overhaul has no save yet, the preset follows on a later start.");
                return;
            }

            string backup = save + ".before_preset_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
            File.Copy(save, backup, true);
            JObject root = JObject.Parse(File.ReadAllText(save));
            Apply(root, Names(lines));
            File.WriteAllText(save, root.ToString(Newtonsoft.Json.Formatting.Indented));

            File.WriteAllText(done, DateTime.Now.ToString("s"));
            if (at >= 0)
            {
                lines[at] = Option + " = false";
                File.WriteAllLines(cfg, lines);
            }
            log.LogInfo("folder preset written to Phone Overhaul (backup: " + Path.GetFileName(backup) + ").");
        }

        static void Apply(JObject root, Dictionary<string, string> names)
        {
            // (assigning a token that already has a parent makes Newtonsoft clone it, so only attach new ones)
            var layout = root["layout"] as JObject;
            if (layout == null) { layout = new JObject(); root["layout"] = layout; }
            var slots = layout["slots"] as JObject ?? new JObject();
            var foldersObj = root["folders"] as JObject;
            if (foldersObj == null) { foldersObj = new JObject(); root["folders"] = foldersObj; }
            var oldList = foldersObj["list"] as JArray ?? new JArray();

            // every app Phone Overhaul knows about, and the ones it removed (slot -1)
            var known = new HashSet<string>();
            var removed = new HashSet<string>();
            foreach (var p in slots.Properties())
            {
                if (p.Name.StartsWith("folder:")) continue;
                known.Add(p.Name);
                if ((int)p.Value < 0) removed.Add(p.Name);
            }
            foreach (JObject f in oldList)
                foreach (var a in (f["contents"] as JArray ?? new JArray())) known.Add((string)a);

            Func<string, bool> exists = key => key.StartsWith("vanilla:") || key.StartsWith("api:shopos.") || known.Contains(key);

            var newSlots = new JObject();
            var newList = new JArray();
            var placed = new HashSet<string>();
            var taken = new HashSet<int>();
            int id = 1;
            foreach (var folder in Folders)
            {
                var apps = folder.Apps.Where(a => exists(a) && !removed.Contains(a)).ToList();
                if (apps.Count == 0) continue;
                string fid = "folder_" + id++;
                string name;
                if (!names.TryGetValue(folder.Name, out name)) name = folder.Name;
                newList.Add(new JObject { { "id", fid }, { "name", name }, { "contents", new JArray(apps) } });
                newSlots["folder:" + fid] = folder.Slot;
                taken.Add(folder.Slot);
                foreach (var a in apps) placed.Add(a);
            }
            foreach (var s in Singles)
            {
                if (!exists(s.Key) || removed.Contains(s.Key)) continue;
                newSlots[s.Key] = s.Value;
                taken.Add(s.Value);
                placed.Add(s.Key);
            }
            // nothing gets lost: removed apps stay removed, everything else goes into free slots after the preset
            int next = 27;
            foreach (string key in known)
            {
                if (placed.Contains(key)) continue;
                if (removed.Contains(key)) { newSlots[key] = -1; continue; }
                if (key.StartsWith("api:phoneoverhaul.device.")) continue;   // the bridge's old tablet tile
                while (taken.Contains(next)) next++;
                newSlots[key] = next;
                taken.Add(next);
            }

            layout["slots"] = newSlots;
            foldersObj["list"] = newList;
            foldersObj["nextFolderId"] = id;
        }

        // folder names from the bridge's language file: %English|Translated
        static Dictionary<string, string> Names(string[] cfgLines)
        {
            var names = new Dictionary<string, string>();
            string lang = "auto";
            foreach (string l in cfgLines)
                if (l.Trim().StartsWith("Language ", StringComparison.Ordinal) || l.Trim().StartsWith("Language=", StringComparison.Ordinal))
                    lang = l.Split('=')[1].Trim().ToLowerInvariant();
            if (lang == "en" || lang == "off") return names;
            string dir = Path.Combine(Paths.PluginPath, "PhoneBridge");
            string file = null;
            if (lang == "auto") { var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "lang_*.txt") : new string[0]; if (f.Length > 0) file = f[0]; }
            else file = Path.Combine(dir, "lang_" + lang + ".txt");
            if (file == null || !File.Exists(file)) return names;
            foreach (string raw in File.ReadAllLines(file, System.Text.Encoding.UTF8))
            {
                if (raw.Length < 3 || raw[0] != '%') continue;
                int bar = raw.IndexOf('|');
                if (bar > 1) names[raw.Substring(1, bar - 1).Trim()] = raw.Substring(bar + 1).Trim();
            }
            return names;
        }

        // Unity's persistentDataPath: %USERPROFILE%\AppData\LocalLow\<company>\<product>
        static string SavePath()
        {
            string info = Path.Combine(Path.GetDirectoryName(Paths.ManagedPath), "app.info");
            if (!File.Exists(info)) return null;
            string[] ci = File.ReadAllLines(info);
            if (ci.Length < 2) return null;
            string low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
            return Path.Combine(low, ci[0].Trim(), ci[1].Trim(), "Phone - Overhaul", "PhoneOverhaul_Save.json");
        }
    }
}
