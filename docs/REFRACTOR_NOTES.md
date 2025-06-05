# Refactor Notes: HorseReskinEnhanced

## Purpose
This file tracks all changes made during the refactor to align with the original MultiplayerHorseReskin mod's multiplayer sync logic, config, and authority model, while keeping/improving our menu and performance.

---

## Stage 1: Initial Refactor
- **Config**: Remove all options except a single `Enabled` boolean.
- **Host Authority**: Only the host can update the horse skin map. Farmhands send requests to the host.
- **Ownership Enforcement**: Only allow reskinning if the player is at their own stable/horse.
- **Sync Events**: On connect/disconnect/save/load, host sends/broadcasts the full skin map.
- **Message Types**: Use `HorseReskinMessage` (single change) and `SkinUpdateMessage` (full sync).
- **Menu**: Keep current menu unless a bug or clear improvement is found.

---

## Completed Steps
- Removed all legacy config options (ShowNotifications, PlaySounds, SinglePlayerUpdateRate, MultiplayerUpdateRate, ShowPreviewAnimation, SuccessMessageColor, RememberLastSkin, AutoApplyLastSkin) and their related logic.
- Removed GMCM config menu registration for these options.
- All logic now checks only `Config.Enabled` for enable/disable.
- Refactored multiplayer sync logic: only the host can update the skin map, farmhands send requests to the host, and the host validates and broadcasts changes.
- All periodic update logic is now simplified to run every tick (can be optimized later).
- Menu logic was left as-is per requirements.

---

## Planned Next Steps
- Test the mod for correct multiplayer syncing and authority.
- Optimize periodic update logic if needed (e.g., add a hardcoded tick interval).
- Document any menu improvements or bugfixes if made.

---

## Notes
- All changes are made to match or improve upon the original mod's multiplayer experience and reliability.
- This file will be updated with each major refactor step. 