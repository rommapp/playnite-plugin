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
| Emulator | A built-in emulator or a custom one you defined in Playnite | Dolphin | ✓ |
| Emulator Profile | A built-in emulator profile or a custom one | Nintendo GameCube | ✓ |
| Platform | The ROM platform/console | Nintendo GameCube | ✓ |
| Destination Path | The path where the ROMs will be stored | `C:\roms\gc` | ✓ |
| Enabled | Whether the mapping is enabled | ✓ | ✓ |

## Importing your library

Once you've set up the plugin, you can import your library by going to `Menu` -> `Library` -> `Import RomM library`. All games matching the emulator path mappings will be imported into Playnite.
