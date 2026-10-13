# AGY CLI Dashboard (Community Edition)

> **The High-Performance Native Windows Cockpit for Google Antigravity CLI (`agy`)**
> 
> 🌐 **Live Showcase & Product Page**: [https://1yellowmellow1.github.io/agy-cli-dashboard-public/](https://1yellowmellow1.github.io/agy-cli-dashboard-public/) (Hosted via GitHub Pages)

An open-source, ultra-responsive Windows desktop application built with **.NET 10 & WinUI 3** featuring an embedded native **ConPTY Terminal Engine** and an interactive **Split-Screen AI Co-Browser**.

---

## ⚖️ Legal Disclaimer & Trademark Notice

**AGY CLI Dashboard is an independent, community-driven open-source project and is NOT affiliated with, endorsed by, sponsored by, or associated with Google LLC, Alphabet Inc., or any of their affiliates.**

- **Google, Antigravity, Gemini**, and related marks are trademarks or registered trademarks of Google LLC.
- **Windows, WinUI**, and ConPTY are trademarks of Microsoft Corporation.
- All other trademarks, logos, and copyrights belong to their respective owners.

This software is provided "as-is" under the MIT License as an unofficial companion and dashboard interface for the Antigravity CLI.

---

## 🚀 Key Features

- **Native ConPTY Engine**: Zero Electron bloat. Direct hardware-accelerated DirectX terminal rendering with full 24-bit ANSI color and bracketed paste support.
- **Integrated Split-Screen AI Co-Browser**: Real-time Edge WebView2 browser pane with deep DOM map inspection, network diagnostics, and automated form interaction via MCP (`cobrowser_*`).
- **Multi-Agent Session Management**: Seamlessly switch between parallel agent workflows without terminal clutter.
- **Project & Rule Management**: Centralized control over global and project-specific AI instructions (`GEMINI.md`), knowledge packs, and integrated TODO notes.
- **System Health & Requirements**: Built-in verification for dependencies, tools, and CLI versions.
- **Bilingual Interface**: Instant toggle between German and English (DE / EN).

---

## 🚀 Quickstart (Clone & Run)

This project is 100% open source. To run the cockpit directly on your Windows PC:

### 1. Prerequisites
- **Windows 10** (Build 1809+) or **Windows 11** (x64)
- **Google Antigravity CLI** (`agy`) installed ([antigravity.google](https://antigravity.google))
- **.NET 10 SDK** (Windows x64) — [Download](https://dotnet.microsoft.com/download/dotnet/10.0) or via winget:
  ```powershell
  winget install Microsoft.DotNet.SDK.10
  ```
- **Microsoft Edge WebView2 Runtime** (installed by default on modern Windows)

### 2. Clone & Launch
Clone the repository and run `start.cmd`. It automatically validates prerequisites, builds in Debug mode, and launches the cockpit:

```powershell
# 1. Clone the repository
git clone https://github.com/1YellowMellow1/agy-cli-dashboard-public.git
cd agy-cli-dashboard-public

# 2. Launch with 1-click
.\start.cmd
```

Alternatively, build manually using the .NET CLI:
```powershell
dotnet build -c Debug -m:1 /p:BuildInParallel=false
```

---

## 📄 License

Licensed under the [MIT License](LICENSE).
