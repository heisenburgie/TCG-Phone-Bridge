using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

namespace PhoneBridge
{
    // Optional translation of the ShopOS pages shown on the phone, read from lang_<code>.txt next to the mod.
    //   =English|Text   whole text
    //   ~English|Text   piece of text, replaced anywhere
    //   ^Regex|Text     regular expression on the whole text
    //   @Key|Name       name of the phone app for a ShopOS key
    internal static class Translator
    {
        static readonly Dictionary<string, string> Exact = new Dictionary<string, string>();
        static readonly List<KeyValuePair<string, string>> Pieces = new List<KeyValuePair<string, string>>();
        static readonly List<KeyValuePair<Regex, string>> Rules = new List<KeyValuePair<Regex, string>>();
        static readonly Dictionary<string, string> Apps = new Dictionary<string, string>();
        static readonly Dictionary<int, string> Shown = new Dictionary<int, string>();

        public static bool Active { get; private set; }

        public static void Load(string dir, string language, ManualLogSource log)
        {
            string lang = (language ?? "auto").Trim().ToLowerInvariant();
            if (lang == "en" || lang == "off" || lang == "") return;
            string file = null;
            if (lang == "auto")
            {
                string[] found = Directory.GetFiles(dir, "lang_*.txt");
                if (found.Length > 0) file = found[0];
            }
            else file = Path.Combine(dir, "lang_" + lang + ".txt");
            if (file == null || !File.Exists(file))
            {
                if (lang != "auto") log.LogWarning("translation file not found: " + file);
                return;
            }

            foreach (string raw in File.ReadAllLines(file, System.Text.Encoding.UTF8))
            {
                if (raw.Length < 3 || raw[0] == '#') continue;
                int bar = raw.IndexOf('|');
                if (bar < 2) continue;
                string from = raw.Substring(1, bar - 1);
                string to = raw.Substring(bar + 1);
                if (raw[0] != '^')
                {
                    // \s = a space that must survive at the start or end of a line
                    from = from.Replace("\\s", " ");
                    to = to.Replace("\\s", " ");
                }
                switch (raw[0])
                {
                    case '=': Exact[from] = to; break;
                    case '~': Pieces.Add(new KeyValuePair<string, string>(from, to)); break;
                    case '@': Apps[from.Trim()] = to.Trim(); break;
                    case '^':
                        try { Rules.Add(new KeyValuePair<Regex, string>(new Regex(raw.Substring(0, bar), RegexOptions.Compiled), to)); }
                        catch (Exception e) { log.LogWarning("translation rule skipped (" + e.Message + "): " + raw); }
                        break;
                }
            }
            Pieces.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            Active = Exact.Count + Pieces.Count + Rules.Count > 0;
            log.LogInfo("translation " + Path.GetFileName(file) + ": " + Exact.Count + " texts, " + Pieces.Count + " pieces, " + Rules.Count + " rules.");
        }

        public static string AppName(string key)
        {
            string n;
            return Apps.TryGetValue(key, out n) ? n : null;
        }

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string t;
            if (Exact.TryGetValue(text, out t)) return t;
            string trimmed = text.Trim();
            if (trimmed != text && Exact.TryGetValue(trimmed, out t)) return text.Replace(trimmed, t);
            foreach (var r in Rules)
                if (r.Key.IsMatch(text)) return r.Key.Replace(text, r.Value);
            string s = text;
            foreach (var p in Pieces)
                if (s.IndexOf(p.Key, StringComparison.Ordinal) >= 0) s = s.Replace(p.Key, p.Value);
            // multi-line texts: translate each line on its own
            if (s == text && text.IndexOf('\n') >= 0)
            {
                string[] lines = text.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = Translate(lines[i]);
                    if (l != lines[i]) { lines[i] = l; changed = true; }
                }
                if (changed) s = string.Join("\n", lines);
            }
            return s;
        }

        // text components per page, looked up again twice a second (rows come and go)
        class Cache { public float At; public List<TextMeshProUGUI> Texts = new List<TextMeshProUGUI>(); }
        static readonly Dictionary<int, Cache> Caches = new Dictionary<int, Cache>();

        public static void Apply(Transform root)
        {
            if (!Active || root == null) return;
            Cache c;
            int key = root.GetInstanceID();
            if (!Caches.TryGetValue(key, out c)) Caches[key] = c = new Cache { At = -1f };
            if (Time.unscaledTime - c.At > 0.5f || c.At < 0f)
            {
                c.At = Time.unscaledTime;
                c.Texts.Clear();
                foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(false))
                {
                    // never touch what the player types
                    var field = t.GetComponentInParent<TMP_InputField>();
                    if (field != null && field.textComponent == t) continue;
                    c.Texts.Add(t);
                }
            }
            for (int i = 0; i < c.Texts.Count; i++)
            {
                var tmp = c.Texts[i];
                if (tmp == null) continue;

                int id = tmp.GetInstanceID();
                string now = tmp.text;
                string last;
                if (Shown.TryGetValue(id, out last) && last == now) continue;
                string tr = Translate(now);
                if (tr != now) tmp.text = tr;
                Shown[id] = tr;
            }
        }
    }
}
