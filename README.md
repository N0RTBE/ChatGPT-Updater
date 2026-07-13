<p align="center">
  <img src="Assets/chatgpt-updater-logo.png" alt="ChatGPT Updater logo" width="128" height="128">
</p>

<h1 align="center">ChatGPT Updater</h1>

<p align="center">
  Install and update ChatGPT on Windows without opening Microsoft Store.
</p>

## Why ChatGPT Updater exists

Access to Microsoft Store is restricted by organizational policies in some corporate environments. As a result, an existing ChatGPT installation may stop receiving updates, while installing the app through the usual Store interface may not be possible.

ChatGPT Updater was created for these situations. It downloads the official ChatGPT package from Microsoft servers, verifies it, and opens the standard Windows installer. The application does not disable or bypass Windows security policies—the installation will only proceed if it is permitted by your organization.

## Features

- Checks the installed and available ChatGPT versions.
- Offers to install ChatGPT when the app is not already installed.
- Downloads the official MSIX package directly from Microsoft servers.
- Verifies the package identity, version, and digital signature before installation.
- Resumes interrupted downloads.
- Automatically removes outdated packages and abandoned downloads.
- Launches ChatGPT immediately when no update is required.
- Supports Windows light and dark themes, Mica, and the system accent color.
- Automatically follows the Windows display language.

## Usage

1. Download `ChatGPT Updater.exe` from the [Releases](https://github.com/N0RTBE/ChatGPT-Updater/releases) page.
2. Run the application.
3. If a new version is available, wait for the download and confirm the installation in the Windows system dialog.

ChatGPT Updater requires Windows 10 or Windows 11 on an x64 system.

> Use this application only in accordance with your organization's rules and policies.

## Building from source

.NET SDK 10 is required:

```console
dotnet publish ChatGPTUpdater.csproj --configuration Release --runtime win-x64 --self-contained true --output dist
```

The standalone executable will be created at `dist/ChatGPT Updater.exe`.

## Third-party components

The application interface is built with [WPF-UI](https://github.com/lepoco/wpfui), distributed under the MIT License. Its license notice is available in [`LICENSES/WPF-UI-MIT.txt`](LICENSES/WPF-UI-MIT.txt).

## License

ChatGPT Updater is distributed under the [MIT License](LICENSE).

## Disclaimer

ChatGPT Updater is an independent, unofficial project. It is not affiliated with, endorsed by, or supported by OpenAI or Microsoft. ChatGPT, OpenAI, Microsoft, and Microsoft Store are trademarks of their respective owners.
