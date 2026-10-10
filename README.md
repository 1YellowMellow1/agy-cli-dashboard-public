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
- **Universal IPC Handoff**: Open external context, emails, and tickets with one click via `pending_handoff.json`.
- **System Health & Requirements**: Built-in verification for dependencies, tools, and CLI versions.
- **Bilingual Interface**: Instant toggle between German and English (DE / EN).

---

## 📥 Quickstart for End Users (No .NET SDK required!)

1. Go to **[Releases](https://github.com/1YellowMellow1/agy-cli-dashboard-public/releases)** and download the latest `Antigravity-CLI-Dashboard-v1.0.0-win-x64.zip`.
2. Extract the ZIP archive to any directory on your PC.
3. Double-click `Antigravity CLI Dashboard.exe` (or `start.cmd`).

**Requirements for End Users:**
- Windows 10 (Build 1809+) or Windows 11
- Google Antigravity CLI (`agy`) installed ([antigravity.google](https://antigravity.google))
- Microsoft Edge WebView2 Runtime (installed by default on modern Windows)

---

## 🛠️ Developer Setup & Local Build

If you want to build or contribute to the project from source:

### 1. Prerequisites
Install the required tools via `winget` or direct download:
```powershell
# Microsoft Edge WebView2 (if not already present)
winget install Microsoft.EdgeWebView2Runtime

# Git for Windows
winget install Git.Git

# .NET 10 Windows SDK (Windows x64)
# Download from: https://dotnet.microsoft.com/download/dotnet/10.0
```

### 2. Clone & Build
```powershell
# Clone the repository
git clone https://github.com/1YellowMellow1/agy-cli-dashboard-public.git
cd agy-cli-dashboard-public

# Build and run with 1-click
.\start.cmd
```

Alternatively, compile manually with the .NET CLI:
```powershell
dotnet build -c Debug -m:1 /p:BuildInParallel=false
```

---

## 📄 License

Licensed under the [MIT License](LICENSE).
