# Privacy

Win Mix runs on your computer. Its own code sends nothing anywhere unless you click **Check for updates**.

- **Check for updates** reads the latest release from `api.github.com`. If you choose to install it, Win Mix downloads the installer from `github.com`. GitHub's [privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) covers those requests.
- Volume, mute and app routing go to SteelSeries Sonar through Windows audio devices on your computer, not over the network.
- Settings are saved in `%LOCALAPPDATA%\Mix.Native\settings.json`. If Win Mix crashes, it saves the error in `last-error.txt` in the same folder. Neither file leaves your computer.

Win Mix bundles Microsoft's Windows App SDK runtime, which can send diagnostic and usage data to Microsoft. See the [Windows App SDK data collection notice](https://github.com/microsoft/WindowsAppSDK#data-collection) and the [Microsoft Privacy Statement](https://aka.ms/privacy).
