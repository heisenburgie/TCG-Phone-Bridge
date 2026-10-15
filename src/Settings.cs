using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PhoneOverhaul.API;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PhoneBridge
{
    // Makes the ShopOS settings mean something on the Phone Overhaul tablet (the ShopOS settings are only read,
    // never written):
    // - Brightness dims the phone screen.
    // - "Apps on the home screen" and "Classic screens" show or hide the ShopOS apps on the phone.
    internal static class Settings
    {
        static ConfigEntry<float> _brightness;
        static ConfigEntry<string> _hidden, _classic;
        static bool _looked;

        static void Look()
        {
            if (_looked) return;
            _looked = true;
            var plugin = AccessTools.TypeByName("ShopOS.Plugin");
            if (plugin == null) return;
            _brightness = Get(plugin, "Brightness", null) as ConfigEntry<float>;
            _hidden = Get(plugin, "HiddenApps", null) as ConfigEntry<string>;
            _classic = Get(plugin, "ClassicScreens", null) as ConfigEntry<string>;
        }

        static object Get(Type t, string name, object o)
        {
            var f = AccessTools.Field(t, name);
            return f != null ? f.GetValue(o) : null;
        }

        // ---------- every half second while playing ----------

        static Transform _screen;
        static Image _dim;
        static string _visKey;

        public static void Tick(IList<string> embeddedKeys)
        {
            Look();
            Brightness();
            Visibility(embeddedKeys);
            RichText();
        }

        static Transform Screen()
        {
            if (_screen == null)
            {
                var go = GameObject.Find("UI_PhoneScreen_Grp");
                if (go != null) _screen = go.transform;
            }
            return _screen;
        }

        static void Brightness()
        {
            if (_brightness == null) return;
            float alpha = Mathf.Clamp01(1f - _brightness.Value);
            if (alpha < 0.01f)
            {
                if (_dim != null) _dim.gameObject.SetActive(false);
                return;
            }
            var screen = Screen();
            if (screen == null) return;
            if (_dim == null)
            {
                var go = new GameObject("PhoneBridge_Dim", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(screen, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                _dim = go.GetComponent<Image>();
                _dim.raycastTarget = false;
            }
            _dim.gameObject.SetActive(true);
            _dim.color = new Color(0f, 0f, 0f, alpha);
            if (_dim.transform.GetSiblingIndex() != _dim.transform.parent.childCount - 1) _dim.transform.SetAsLastSibling();
        }

        // ShopOS app keys that are the game's own phone buttons (PhoneButtonGrp_<key> on the phone)
        static readonly string[] VanillaKeys =
        {
            "Restock", "RestockBoardGame", "Furniture", "BuyDecoration", "ExpandShop", "PriceCheck", "Grading", "HostTournament",
            "Hiring", "ScannerOrder", "CustomerReview", "RentBill", "GameEvent", "CameraTakePhoto", "PhotoGallery",
        };
        // ShopOS app keys of other mods -> their Phone Overhaul app id
        static readonly Dictionary<string, string> ModApps = new Dictionary<string, string>
        {
            { "tracker", "CollectionTracker" }, { "epl:Pokemon Shop", "Pokemon Shop" }, { "PurchaseOverhaul", "OnePurchase" },
        };

        static readonly HashSet<string> _weHid = new HashSet<string>();
        static readonly Dictionary<string, GameObject> _tiles = new Dictionary<string, GameObject>();

        public static bool IsClassic(string key)
        {
            Look();
            if (_classic == null || string.IsNullOrEmpty(_classic.Value)) return false;
            foreach (string k in _classic.Value.Split('|')) if (k.Trim() == key) return true;
            return false;
        }

        // "Apps on the home screen" in the ShopOS settings: on = shown on the Phone Overhaul tablet, off = gone.
        static void Visibility(IList<string> keys)
        {
            var off = new HashSet<string>(StringComparer.Ordinal);
            if (_hidden != null && _hidden.Value != null)
                foreach (string k in _hidden.Value.Split('|')) if (k.Trim().Length > 0) off.Add(k.Trim());
            off.Remove("Setting");   // never hide the page that brings apps back

            // the game's own buttons: Phone Overhaul drops a button from the home screen while it is inactive
            var screen = Screen();
            if (screen != null)
            {
                foreach (string key in VanillaKeys)
                {
                    var tile = Tile(screen, "PhoneButtonGrp_" + key);
                    if (tile == null) continue;
                    if (off.Contains(key))
                    {
                        if (tile.activeSelf) { tile.SetActive(false); _weHid.Add(key); }
                    }
                    else if (_weHid.Remove(key) && !tile.activeSelf) tile.SetActive(true);
                }
            }

            // apps registered through the Phone Overhaul API (ShopOS pages and other mods)
            string state = string.Join("|", new List<string>(off).ToArray());
            if (state == _visKey) return;
            var reg = PhoneOverhaulAPI.Registry;
            if (reg == null) return;
            _visKey = state;
            foreach (string key in keys)
            {
                if (key == "Setting") continue;
                try { reg.SetEnabled("shopos." + key, !off.Contains(key)); }
                catch (Exception e) { Plugin.Log.LogWarning("app visibility " + key + ": " + e.Message); }
            }
            foreach (var m in ModApps)
            {
                try { reg.SetEnabled(m.Value, !off.Contains(m.Key)); }
                catch (Exception e) { Plugin.Log.LogWarning("app visibility " + m.Key + ": " + e.Message); }
            }
        }

        static GameObject Tile(Transform screen, string name)
        {
            GameObject go;
            if (_tiles.TryGetValue(name, out go) && go != null) return go;
            foreach (var t in screen.GetComponentsInChildren<Transform>(true))
                if (t.name == name) { _tiles[name] = t.gameObject; return t.gameObject; }
            return null;
        }

        // ShopOS adds a reputation line with colour tags to the game's review screen; inside Phone Overhaul
        // that label has rich text off, so the tags show as text.
        static void RichText()
        {
            var screen = Screen();
            if (screen == null || !screen.gameObject.activeInHierarchy) return;
            foreach (var t in screen.GetComponentsInChildren<TextMeshProUGUI>(false))
                if (!t.richText && t.text != null && t.text.IndexOf("<color=", StringComparison.Ordinal) >= 0) t.richText = true;
        }
    }
}
