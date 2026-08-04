# Synced Personal Backpack — multiplayer design

## Non-negotiable guarantees

1. Each Steam player owns one independent backpack per save/career.
2. During a multiplayer session, the host is authoritative for every item mutation.
3. A client never removes an item locally while waiting for the host.
4. Every transfer is idempotent: retrying the same request cannot duplicate or delete an item.
5. Failed transfers restore both source and destination exactly.
6. Disconnecting, reconnecting, changing scenes, or restarting a session cannot silently replace a newer backpack with an older snapshot.
7. Unknown or incompatible item data is quarantined for recovery, never discarded.

## Identity and ownership

- Backpack key: `career/save identity + stable Steam ID`.
- Never key ownership by Unity instance ID, FishNet connection ID, player-list index, display name, or object path; all are transient.
- Only the owning player can request moves into or out of their backpack.
- Only the host commits those requests while multiplayer is active.
- Other clients receive only visual state if needed; they do not receive private contents.

## Transaction protocol

Each request contains:

- unique request GUID;
- player Steam ID;
- expected backpack revision;
- operation (`deposit`, `withdraw`, `move`, `split`, `merge`);
- source container and slot;
- destination backpack slot or game container slot;
- item fingerprint, quantity, and quality/state metadata.

Host processing:

1. Reject incompatible mod/protocol versions.
2. Resolve the sender from the network connection; never trust a Steam ID supplied in the payload.
3. Acquire the per-player backpack transaction lock.
4. Return the cached result immediately if this request GUID was already processed.
5. Validate expected revision, ownership, source fingerprint, quantity, capacity, and destination rules.
6. Snapshot both affected slots.
7. Apply the mutation once on the host.
8. If either side fails, restore both snapshots and return a structured failure.
9. Increment the revision, append the journal entry, save atomically, and broadcast the committed snapshot to the owner.
10. Cache the request result so a network retry is harmless.

The client UI shows a pending state but leaves the source item untouched until the committed host snapshot arrives.

## Persistence and recovery

Initial version uses two mirrored copies:

- host mirror for the active career/session;
- owner's local Companion data, suitable for Steam Cloud or ordinary backup tools.

Both copies use the same monotonically increasing revision, content hash, and transaction tail. On reconnect, the highest valid common revision wins. Divergent revisions create a visible recovery choice and preserve both files.

Files are written as `new -> flush -> atomic replace`; the previous good file is retained. A compact transaction journal records the last operations so interrupted writes and disappearance reports are diagnosable.

An optional hosted sync service can later replace the owner's local mirror. It should use authenticated Steam identity, optimistic concurrency (`expected revision`/ETag), encrypted transport, rate limits, and immutable recovery history. It is not required for reliable same-group multiplayer.

### Windows VPS deployment

The supported Windows Server 2025 deployment is in `VPS-Backpack-Database-Setup-Windows`.
It runs PostgreSQL 18, Python 3.13/FastAPI, and Caddy natively without Docker, WSL, or Hyper-V.
PostgreSQL and the API bind only to `127.0.0.1`; Caddy is the sole public endpoint on HTTPS.
Player API tokens are independently issued and revocable per SteamID64. The hosted copy remains
a recovery mirror and does not replace host authority for live multiplayer inventory mutations.

## Session handshake

- Verification is session-local: the host is trusted from its local setup and grants access to
  compatible co-op players in the current Steam lobby. No external verification server is required.
- A client's grant lasts only for the current session and is accepted only when it is sent by the
  actual Steam lobby owner; another client cannot grant itself or spoof a host snapshot.
- Host advertises protocol and schema versions.
- Owner replies with supported versions and its latest valid revision/hash.
- Host compares its mirror and requests the full snapshot only when required.
- Backpack controls stay locked until reconciliation completes.
- A missing client mod does not corrupt data: backpack controls remain unavailable and the session may continue.

## Menu and key binding

- The default keyboard binding is `B`; pressing it toggles the backpack menu.
- The binding is stored in the backpack mod's local per-player configuration, so the desktop Companion is not required for it to work.
- Players can rebind it from the Companion Options screen or from an in-game backpack settings control; a change is applied immediately.
- The input handler ignores the binding while a text field, chat, console, or key-capture control has focus.
- The menu can always consume its own binding to close, including when normal gameplay input is locked.
- A detected conflict with a game or mod binding produces a warning but never silently changes the player's choice.
- Keyboard and controller bindings are stored separately. Controller support may be added without changing the keyboard binding.
- Input handling only requests a UI state change. It cannot mutate backpack contents, revisions, or transaction state.

## Test matrix before release

- deposit/withdraw/split/merge with empty, partial, and full destinations;
- double-clicks and deliberate duplicate request packets;
- disconnect before request, during commit, and after commit before acknowledgement;
- host and owner crash during each save stage;
- reconnect with older, newer, corrupt, and divergent mirrors;
- scene changes, death/respawn, arrest, vehicle entry, and underground transitions;
- two players transferring simultaneously to their own backpacks;
- incompatible mod versions and unmodded clients;
- modded/custom item definitions disappearing between game versions;
- 100+ repeated transfers with item-count conservation checks.

The release gate is item conservation: for every committed transaction, the total quantity across the source, backpack, and destination must remain constant unless the operation explicitly consumes or creates an item.
