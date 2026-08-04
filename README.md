# Schedule I Companion v1.6.0 — Dashboard Intelligence and Managed Mods

The dashboard now brings live game information together beside the map: game time, quests,
messages, orders, notifications, and configurable map POIs. Each section and POI category can be
shown or hidden in Settings. The dark Schedule I-inspired interface and live runtime refresh remain.

## Dashboard additions

- In-game clock with 12-hour and 24-hour display modes
- Property, laundering-business, contract, owned-vehicle, dead-drop, recruited-dealer, and tracked-objective POIs
- Current quest objectives and recent text-message previews
- Orders moved onto the Dashboard as an optional panel
- Opt-in debugging tools for freezing time, clearing trash manually or every 5–60 seconds,
  showing FPS, and setting clear weather

DevTools are disabled by default and must be enabled in Settings. Their actions use Schedule I's
built-in console command system.

## Shareable install

Extract `dist\ScheduleICompanion-v1.7.3.zip`, keep its `Payload` folder beside
`ScheduleICompanion-Setup.exe`, and run the setup application. It detects Steam libraries,
can install the official pinned MelonLoader v0.7.3 x64 build, verifies that download before
extracting it, installs the self-contained Companion, creates backups, and supports repair or
uninstall. Close Schedule I before using setup.

The release ZIP is produced with `BUILD-RELEASE.bat` on a development machine that has the
Schedule I IL2CPP assemblies available for compilation.

## Managed mods

The **Mods** tab installs and removes SHA-256 verified mod DLLs while preserving their player
data. It uses the bundled catalogue by default, or an optional HTTPS catalogue hosted on your
own VPS. Close Schedule I before changing enabled mods.

The first managed mod is **Reliable Personal Backpack 0.2.0**. It provides twelve private
slots, opens with `B`, transfers whole stacks, and uses host-authorised revisioned transactions
in multiplayer. The host verifies the local session and compatible co-op players inherit that
verification for that session; no external server is required. Both the host and each backpack
owner need the same compatible mod version.

The **Best Mixes** tab ranks the ten highest-price recipes currently discovered and unlocked
in the active save, using live in-game prices and excluding locked ingredients.

The setup application enables Personal Backpack by default. Another player can unpack the release,
run the setup EXE, and let it detect the game, install MelonLoader when needed, install the
Companion, and place the Backpack mod correctly. Backpack can later be switched on or off from the
Companion's Mods tab without removing player data.

Two optional hosted-backup kits are included. `VPS-Backpack-Database-Setup-Windows` is the
native Windows Server 2025 installer and is the correct choice for a Windows VPS. The older
`VPS-Backpack-Database-Setup` kit targets Ubuntu/Debian through Windows BAT launchers. Read the
selected kit's `README-FIRST.txt` before setup.

## Developer install

Close Schedule I, then run `BUILD-AND-INSTALL.bat` as administrator.

## Live refresh development

Install this revision once with Schedule I closed. That installs a small stable MelonLoader
bootstrap plus the reloadable game runtime.

After that, `BUILD-AND-INSTALL.bat` can run while the game is open. It rebuilds and restarts
the desktop companion, replaces `ScheduleICompanion.Runtime.dll`, and leaves the loaded
bootstrap untouched. Use **Refresh live** in the companion to reload the runtime, rescan the
current scene, and reload maps/settings without restarting Schedule I. Starting the newly built
companion also requests a runtime refresh automatically when it reconnects.

Changes to `ScheduleICompanion.Mod` itself still require running the installer with the game
closed and then starting the game again. Most game-facing development should live in
`ScheduleICompanion.Runtime`/`GameProbe.cs` so it remains reloadable.

## Included portal pairs

- North-central: surface `-51.3, 1.0, 91.5` → tunnel `-51.5, -6.5, 91.5`
- North-east: surface `-8.5, -3.1, 114.9` → tunnel `-9.0, -4.0, 78.1`
- West: surface `-82.2, -3.2, 68.5` → tunnel `-43.4, -4.3, 54.5`
- South-central: surface `26.1, -3.2, 11.3` → tunnel `34.0, -4.3, 13.1`
- East: surface `100.5, -0.1, -4.5` → tunnel `105.4, -4.2, -2.1`

Portal switching uses proximity, local elevation midpoint, movement direction, and the global threshold as a fallback.
