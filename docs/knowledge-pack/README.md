# 🧠 Battle-Tested AI Knowledge Packs

> **Curated edge-case solutions, undocument framework fixes, and performance patterns for Google Antigravity CLI (gy) and modern AI coding assistants.**

When AI coding assistants write code, they excel at standard documentation. However, they frequently fail on obscure framework bugs (e.g. Safari 7-day storage wipes, WinUI 3 HWND-airspace issues, or PowerShell exception swallowing).

These knowledge packs act as a **long-term memory shield** to stop AI agents from repeating known mistakes.

---

## 📦 Included Knowledge Packs

- **winui3-patterns-and-airspace.md**: Native ConPTY airspace fixes (Width=0, Height=0), compositor-thread opacity pulses, RTF theme-morphing, and VisualTreeHelper namespace rules.
- **csharp-dotnet10-high-performance.md**: .NET 10 SDK item inclusion rules (_archive), UTF-16 surrogate pair emojis in char literals, and async task lambda conversion.
- **javascript-safari-storage-and-unicode.md**: Apple iOS Safari 7-day ITP storage purge survival via URL tokens, Arabic/Persian unicode digit normalization, and Firestore { merge: true } data-loss prevention.
- **rowser-dom-and-network-stability.md**: Co-Browser CDP timeouts, Shadow-DOM hit-testing, and throttled Deep-DOM traversal to prevent browser freezes.
- **powershell-async-interop.md**: PowerShell 5.1 ETS .Result exception-swallowing prevention (.GetAwaiter().GetResult()) and parallel HttpClient connection pooling.

---

## 🚀 How to Use

1. **Option A (Global Agent Memory):** Copy any of these .md files into your global Antigravity knowledge directory:
   `powershell
   %USERPROFILE%\.gemini\antigravity-cli\skills\knowledge-base\custom-knowledge\
   `
2. **Option B (Project-Specific):** Add relevant sections into your project's docs/knowledge.md or .gemini/rules/.

---

## 🤝 Share Your Knowledge! (Crowdsourcing)

Has your **AGY CLI Dashboard** or Antigravity agent uncovered an obscure framework bug or edge-case solution during pair programming?

**Help fellow developers worldwide:**
1. Fork [agy-cli-dashboard-public](https://github.com/1YellowMellow1/agy-cli-dashboard-public).
2. Add your sanitized, domain-neutral pattern to examples/knowledge-pack/.
3. Submit a Pull Request!
