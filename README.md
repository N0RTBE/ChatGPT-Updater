<p align="center">
  <img src="Assets/chatgpt-updater-logo.png" alt="ChatGPT Updater logo" width="128" height="128">
</p>

<h1 align="center">ChatGPT Updater</h1>

<p align="center">
  Install and update ChatGPT on Windows without opening Microsoft Store.
</p>

## Why ChatGPT Updater exists

Installing ChatGPT for the first time - or keeping an existing installation up to date - is not always straightforward when it depends on Microsoft Store. The Store may be disabled by an organization, unavailable on a managed device, removed from a customized Windows installation, or unable to complete downloads because of a broken cache, a stuck update queue, sign-in problems, or other Store client errors. As a result, some users cannot install ChatGPT at all, while others have an existing installation that no longer receives updates.

ChatGPT Updater handles both situations. When ChatGPT is not installed, it finds and downloads the latest official release, then lets the user confirm its installation through Windows. When ChatGPT is already installed, the updater compares its version with the latest release available through Microsoft Store. It downloads a newer version when one is available or launches ChatGPT immediately when the installed version is already up to date. Every package comes directly from Microsoft delivery servers and is validated for the expected identity, version, architecture, and digital signature before it is handed over to Windows for installation.

The application does not modify Microsoft Store, remove device-management restrictions, or bypass Windows security controls. It provides an alternative to the Store interface for installing and updating ChatGPT, but either operation still depends on Windows allowing the official package and on the required Microsoft services being reachable.

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

1. Download the latest `ChatGPT-Updater-vX.Y.Z-win-x64.zip` archive from the [Releases](https://github.com/N0RTBE/ChatGPT-Updater/releases) page.
2. Extract the archive.
3. Run `ChatGPT Updater.exe`.
4. If a new version is available, wait for the download and confirm the installation in the Windows system dialog.

You can also use ChatGPT Updater as your regular ChatGPT launcher. Each time it starts, it checks for a newer version first and opens ChatGPT immediately when the installed version is already up to date.

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
