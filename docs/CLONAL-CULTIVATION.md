# Clonal Cultivation mod

Goal: allow one bud of a player-created weed product to be planted in a normal pot and produce a plant whose harvest retains that product identity, effects, appearance, and value.

## Safety and multiplayer rules

- Planting consumes exactly one bud before a plant is created.
- The host validates the pot, inventory stack, Steam owner, career, and product definition.
- Clone identities use deterministic synthetic seed IDs derived from the custom product ID.
- Clone records are stored atomically per Steam owner and career.
- A failed planting transaction restores the bud or declines to create the plant; it never performs both sides partially.
- Pots and growth continue to use the game's native network and save systems.

## Implementation stages

1. Register runtime seed definitions for discovered custom weed products using a native weed seed as the prefab template.
2. Add a contextual “Plant bud” action when a custom weed bud is equipped and an empty prepared pot is targeted.
3. Route the request through the host, consume one unit, and invoke the pot's native planting RPC with the synthetic seed ID.
4. Resolve the synthetic seed back to the custom `WeedDefinition` when `WeedPlant.GetHarvestedProduct` runs.
5. Re-register clone seeds before pot save data is loaded and synchronize registry additions in co-op.
6. Add transaction, save/reload, reconnect, and mismatched-client tests before release.
