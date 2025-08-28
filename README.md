# R-Fiddler
[![Thunderstore Badge](https://gist.githubusercontent.com/art0007i/c4871bbdb30d31e7899328754916bb81/raw/076910e4939e624f17c88bd879770d3bd2fe3f1e/available-on-thunderstore.svg)](https://thunderstore.io/c/resonite/)

A Resonite mod that provides real-time notifications for external asset requests. 
Monitor and preview assets in a session, see favicons, and customize your experience with configurable settings.

## Features

- Real-time in-game notifications for external asset requests
- Asset previews in notifications:
  - For images: Displays a preview of the image
  - For non-image assets: Shows a preview of the current world
- Display of website favicons in notifications
  - Uses a special algorithm to dynamically find favicons
- Color-coded notifications based on URI scheme (http, https, ws, wss)
- Clickable hyperlinks in notifications to easily access the asset's URL
- Customizable trusted domains to exclude from notifications
- Cooldown system to prevent notification spam
- Customizable settings for notification sound, cooldown period, and more
- Lightweight and easy to use

# Showcase

https://github.com/user-attachments/assets/dc5baec5-f7e5-4314-a660-e7521c703991

## Installation

1. Install [BepisLoader](https://github.com/ResoniteModding/BepisLoader) for Resonite.
2. Download the latest release ZIP file (e.g., `HGCommunity-R-Fiddler-2.0.0.zip`) from the [Releases](https://github.com/HGCommunity/R-Fiddler/releases) page.
3. Extract the ZIP and copy the `plugins` folder to your BepInEx folder in your Resonite installation directory:
   - **Default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\BepInEx\`
4. Start the game. If you want to verify that the mod is working you can check your BepInEx logs.

## Usage

Once installed, R-Fiddler works automatically. You'll see notifications in-game whenever an external asset request is made.

## Configuration

R-Fiddler has several configurable options:

- **Enabled**: Toggle notifications for external asset loading.
- **TrustedURI**: List of trusted URIs that won't trigger notifications.
- **Cooldown**: Set a cooldown period (in seconds) between notifications for the same domain.
- **PlaceholderURI**: Specify an image to use when no image is available.
- **NotifSound**: Enable or disable sound for notifications.
- **NotifURI**: Set the sound file for notifications.

## Compatibility

R-Fiddler is designed to be compatible with the latest version of Resonite and BepisLoader. If you encounter any issues, please report them in the Issues section of this repository.

## Contributing

Contributions to R-Fiddler are welcome! Please feel free to submit pull requests or create issues for bugs and feature requests.

## License

[MIT License](https://github.com/HGCommunity/R-Fiddler/blob/master/LICENSE)