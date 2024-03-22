[![license-badge]][license-badge-url]
[![release-badge]][release-badge-url]
[![discord-badge]][discord-badge-url]

# RomM Playnite Library Plugin

This plugin allows you to import your RomM library into Playnite. It queries the RomM API for your library and then creates a Playnite library entry for each game in your RomM library. Installing a game in Playnite will download it from RomM and store it on your system.

## Setup

### Emulators

The plugin requires that you have **at least 1 emulator installed** on your system and configured in Playnite. You can use a built-in emulator or a custom one. **If no emulators are installed and configured, you won't be able to complete setup!** To set up an emulator, go to `Menu` -> `Library` -> `Configure Emulators...` -> `Add emulator...`.

### Settings

The plugin needs to be configured before it can be used. To do this, go to `Menu` -> `Library` -> `Configure Integrations...` -> `RomM`.

#### Authentication

You'll need to enter the host URL of your RomM instance, as well as a username and password. Passwords are stored in plaintext in Playnite, so it's recommended to use a separate account with the "VIEWER" role. **The host URL has the include the protocol (http/https) and should not include a trailing slash, e.g. `https://romm.example.com`.**

#### Emulator path mappings

| Field | Description | Example | Required |
|---|----|----|----|
| Emulator | A built-in (or custom) emulator | Dolphin | ✓ |
| Emulator Profile | A built-in (or custom) emulator profile | Nintendo GameCube | ✓ |
| Platform | The platform or console | Nintendo GameCube | ✓ |
| Destination Path | The path where downloaded ROMs will be stored | `C:\roms\gc` | ✓ |
| Enabled | Whether the mapping is enabled | ✓ | ✓ |

## Importing your library

Once you've set up the plugin, you can import your library by going to `Menu` -> `Library` -> `Import RomM library`. All games matching the emulator path mappings will be imported into Playnite.

Installing a game will download it from RomM and store it in the destination path. You can then launch the game from Playnite, and it will be launched using the configured emulator.

By default, compressed files will be extracted automatically into a folder matching the game's name. You can modify this behavior in the settings page.

## Support

If you have any issues with the plugin, please [open an issue](https://github.com/rommapp/playnite-plugin/issues/new) in this repository. If the issue is with RomM itself, open an issue in the [RomM repository](https://github.com/rommapp/romm/issues/new/choose).

Join us on discord, where you can ask questions, submit ideas, get help, showcase your collection, and discuss RomM with other users.

[![discord-invite]][discord-invite-url]

### Acknowledgement

This plugin is **heavily** inspired by the excellent [Playnite EmuLibrary](https://github.com/psychonic/Playnite-EmuLibrary) by @psychonic.

<!-- Badges -->

[license-badge]: https://img.shields.io/github/license/rommapp/playnite-plugin?style=for-the-badge&color=a32d2a
[license-badge-url]: LICENSE
[release-badge]: https://img.shields.io/github/v/release/rommapp/playnite-plugin?style=for-the-badge
[release-badge-url]: https://github.com/rommapp/playnite-plugin/releases
[discord-badge]: https://img.shields.io/badge/discord-7289da?style=for-the-badge
[discord-badge-url]: https://discord.gg/P5HtHnhUDH

<!-- Links -->

[discord-invite]: https://invidget.switchblade.xyz/P5HtHnhUDH
[discord-invite-url]: https://discord.gg/P5HtHnhUDH
