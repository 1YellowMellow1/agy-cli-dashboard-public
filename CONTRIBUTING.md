# Contributing to AGY CLI Dashboard

Thank you for your interest in contributing to **AGY CLI Dashboard**!

## Getting Started

1. Fork and clone the repository:
   ```powershell
   git clone https://github.com/1YellowMellow1/agy-cli-dashboard-public.git
   cd agy-cli-dashboard-public
   ```
2. Prerequisites:
   - Windows 10 (1809+) or Windows 11
   - .NET 10 Windows SDK
   - Microsoft Edge WebView2 Runtime
   - Google Antigravity CLI (`agy`)

3. Build and run locally:
   ```powershell
   .\start.cmd
   ```

## Development Guidelines

- **Zero Electron Bloat**: All UI code uses native WinUI 3 (Windows App SDK) and DirectX hardware acceleration.
- **Localization**: When adding new UI elements, register translation keys in `Assets/Locales/de.json` and `Assets/Locales/en.json`.
- **Resource Integrity**: Run `powershell scripts/Validate-XamlResources.ps1` before committing to verify all StaticResources and ThemeResources.

## Submitting Pull Requests

1. Create a feature branch: `git checkout -b feat/your-feature-name`.
2. Ensure clean compilation: `dotnet build -c Debug`.
3. Submit your pull request against `main`.
