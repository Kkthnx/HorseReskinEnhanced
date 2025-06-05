# HorseReskinEnhanced

A Stardew Valley mod that allows players to reskin their horses with custom 224x128 textures. Supports multiplayer with proper host authority and syncing.

## Features
- Easily change your horse's appearance using a menu.
- Supports custom 224x128 textures (see `assets/` folder for examples).
- Multiplayer compatible: only the host can update the skin map, farmhands send requests.
- Configurable enable/disable option.
- Localized (see `i18n/` folder).

## Installation
1. Install [SMAPI](https://smapi.io/).
2. Download and extract this mod into your `Stardew Valley/Mods` folder.
3. (Optional) Add your own horse textures to the `assets/` folder (224x128 PNG).

## Usage
- In-game, open the horse reskin menu (see controls or mod menu).
- Select a skin and apply it to your horse.
- Only the host can change horse skins in multiplayer; farmhands must request changes.

## Configuration
- Edit `config.json` (created after first launch) to enable or disable the mod:
  ```json
  {
    "Enabled": true
  }
  ```

## Credits
- Mod by Kkthnx
- Inspired by the original MultiplayerHorseReskin mod
- Uses [SMAPI](https://smapi.io/)

## License
MIT License (see LICENSE file)