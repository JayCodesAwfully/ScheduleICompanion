# Companion mod roadmap

## Recommended first foundation

Build a small `Companion Multiplayer Core` mod before feature mods. It owns version handshakes, stable player identity, host-authoritative request/response messages, idempotency, atomic persistence, diagnostics, and install/update metadata. Backpack and later mods reuse this rather than inventing separate networking.

## Prioritised ideas

1. **Reliable Personal Backpack** — individual storage with the transaction and recovery guarantees in `BACKPACK-MULTIPLAYER-DESIGN.md`.
2. **Crew Pings and Shared Waypoints** — temporary map markers with labels, colours, expiry, and host moderation.
3. **Shared Order Board** — assign deals to players, show required products, readiness, destination, and delivery window without changing core contract state.
4. **Vehicle Keys and Recovery** — shared access rules, last driver, impound/recovery marker, and optional crew ownership.
5. **Crew Stash Ledger** — audit deposits/withdrawals from agreed storage entities and identify shortages without replacing the game's inventory authority.
6. **Loadout Templates** — host-validated moves from owned storage into a saved personal loadout, with an exact missing-items report and no item spawning.
7. **Production Alerts** — multiplayer notifications for ready plants, stalled stations, completed deliveries, unpaid employees, and laundering completion.
8. **Transaction Recovery Inspector** — a diagnostic view for backpack/stash journals that can restore quarantined items deliberately instead of silently spawning replacements.

Avoid beginning with mechanics that alter NPC AI, contracts, economy, or world ownership. Those have a larger compatibility surface and are harder to make host-authoritative across game patches.
