# Companion-managed mod packages

Companion feature mods should be separate from the stable Companion bridge. A package uses the `.sicmod` extension (ZIP container) and contains:

- `manifest.json`;
- one or more MelonLoader DLLs under `Mods/`;
- optional Companion UI/module assets under `Companion/`;
- documentation and licence files.

Minimum manifest fields:

```json
{
  "id": "schedulei-companion.backpack",
  "name": "Reliable Personal Backpack",
  "version": "1.0.0",
  "publisher": "Schedule I Companion",
  "minimumCompanionVersion": "1.6.0",
  "minimumGameBuild": "",
  "multiplayerPolicy": "host-and-owner-required",
  "networkProtocol": 1,
  "files": [
    {
      "source": "Mods/ScheduleICompanion.Backpack.dll",
      "destination": "Mods/ScheduleICompanion.Backpack.dll",
      "sha256": "..."
    }
  ]
}
```

## Installation rules

- Validate every relative path; reject rooted paths and `..` traversal.
- Verify every declared SHA-256 before copying.
- Refuse duplicate package IDs with incompatible publishers.
- Back up replaced files and package state.
- When the game is running, stage DLL changes and clearly require a restart; never replace a loaded MelonLoader assembly.
- Keep enable/disable state in the Companion package registry, not by deleting save data.
- Uninstall removes only declared package files and preserves user data unless the user explicitly chooses data removal.
- Multiplayer packages display their policy and protocol version before installation.
- Automatic remote updates require a signed catalog and pinned publisher key; an arbitrary URL must never silently install a DLL.

## Backpack policy

The backpack defaults to `B` for its local menu toggle. The binding belongs to the owner-side mod configuration and remains available without the desktop Companion. Rebinding is immediate and does not participate in multiplayer state synchronization because it cannot change inventory data.

The first backpack version should use `host-and-owner-required`: the host needs the networking/authority component and each player who wants a backpack needs the owner UI/persistence component. If every player installs it, setup is automatic. An unmodded player can join but cannot open or mutate a backpack.

Before joining, the Companion can compare advertised package IDs/protocols and show:

- ready;
- host missing required package;
- owner package missing;
- version mismatch;
- restart required after staged update.
