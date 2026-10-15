Phone Bridge - ShopOS x Phone Overhaul  v1.0.0
TCG Card Shop Simulator  |  BepInEx 5

UNOFFICIAL bridge between QOL ShopOS and Phone - Overhaul.
Please report bugs with the bridge on the Phone Bridge mod page - NOT to the ShopOS
or Phone Overhaul authors.

The ShopOS pages (Music, Lights, Trades, Mod Config, Reputation, Rent, Events,
Price Check, ...) open inside the Phone Overhaul phone as phone apps, and a "Phone Overhaul"
app on the ShopOS tablet takes you to the phone.


REQUIREMENTS
  - BepInEx 5 (5.4.23 or newer)
  - Phone - Overhaul (tested with 2.2.8)
  - QOL ShopOS (tested with 1.1.3)


INSTALL
  1. Install the requirements above.
  2. Extract the zip into the game folder (the one with "Card Shop Simulator.exe"),
     so you get:
       BepInEx\plugins\PhoneBridge\PhoneBridge.dll
       BepInEx\patchers\PhoneBridge\PhoneBridge.Preloader.dll
  3. Start the game once, then set up the keys (below).
  Keep the folder name "PhoneBridge" - the translation file is looked up there.

  German translation (optional download): extract it the same way; it adds
  BepInEx\plugins\PhoneBridge\lang_de.txt.


KEYS  (in BepInEx\config\munch.PhoneOverhaul.cfg, section [Other Tablet Mods])
  Phone Key Opens = OtherTablet   Tab opens the ShopOS tablet
  Open Phone Key  = <a free key>  opens the Phone Overhaul phone
  On the tablet, the "Phone Overhaul" app opens the phone as well.


UNINSTALL
  Delete BepInEx\plugins\PhoneBridge and BepInEx\patchers\PhoneBridge.
  Optional: BepInEx\config\local.phonebridge.cfg


SETTINGS  (BepInEx\config\local.phonebridge.cfg, or an in-game config manager)
  ShopOSApps        Which ShopOS pages become phone apps, separated by ';'.
                    Key=Name renames one. Extra keys: Wallpaper, PhotoGallery,
                    HostTournament, Hiring, ScannerOrder. Restart needed.
  PhoneOverhaulApp  "Phone Overhaul" app on the ShopOS tablet that opens the phone. (true)
  Language          auto / en / de ... (auto = use the lang_*.txt next to the mod)
  ApplyFolderPreset On the first start with the bridge, the Phone Overhaul home
                    screen gets tidy folders once (Stock, Shop, Management, Events,
                    Tools, Duplicates). Rearrange it as you like afterwards - your
                    layout stays. Set this to true and restart to get the folders
                    again. A backup of your layout is made first, nothing is
                    removed, and the option switches itself off again.
                    Backup: %USERPROFILE%\AppData\LocalLow\<game>\Phone - Overhaul\
                            PhoneOverhaul_Save.json.before_preset_<date>.bak

  ShopOS settings are used on the phone as well: Brightness dims the phone, and
  "Apps on the home screen" / "Classic screens" show or hide the apps there.
  The bridge only reads them - it never changes ShopOS settings or ShopOS code.


COMPATIBILITY
  The bridge uses ShopOS internals that can change with any ShopOS update. Keeping
  it working is up to the bridge, not ShopOS. If something breaks after an update,
  the bridge switches the affected part off and writes it to BepInEx\LogOutput.log.


TRANSLATING
  Copy lang_de.txt to lang_<code>.txt and translate the right side of each line.
  The file explains its format at the top. Share it and it can go into the mod.


CREDITS
  QOL ShopOS by Evers665 - all ShopOS pages and app icons are drawn by ShopOS itself
  at runtime; no ShopOS files or assets are included.
  Phone - Overhaul by Munchmatoast - integrated through its public API; no Phone
  Overhaul files or assets are included.
