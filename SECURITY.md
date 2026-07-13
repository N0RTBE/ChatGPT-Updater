# Security Policy

## Supported Versions

Security updates are provided only for the latest publicly available release of ChatGPT Updater.

| Version         | Supported |
|-----------------|-----------|
| Latest release  | ✅        |
| Older releases  | ❌        |

Users are encouraged to update to the latest release before reporting a security issue.

## Reporting a Vulnerability

Please do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.

Use GitHub's private vulnerability reporting feature:

1. Open the **Security** tab of this repository.
2. Select **Report a vulnerability**.
3. Provide a clear description of the issue and steps to reproduce it.
4. Include the affected version and potential impact.
5. Attach relevant logs or screenshots after removing personal or sensitive information.

Please do not include access tokens, signed download URLs, personal information, or other secrets in your report.

You can expect an initial response within 7 days. If the vulnerability is confirmed, updates will be provided as the issue is investigated and resolved. If the report is declined, an explanation will be provided when possible.

Please allow reasonable time for a fix to be developed and released before publicly disclosing the vulnerability.

## Scope

Security reports related to the following areas are especially relevant:

- Download and validation of application packages
- Package identity, version, architecture, and digital signature verification
- Handling of downloaded or temporary files
- Unexpected code execution or privilege escalation
- Update integrity and transport security

Vulnerabilities in ChatGPT, Microsoft Store, Windows, or other third-party services should be reported directly to their respective maintainers.

## Disclaimer

ChatGPT Updater is an independent, unofficial project and is not affiliated with, endorsed by, or supported by OpenAI or Microsoft.
