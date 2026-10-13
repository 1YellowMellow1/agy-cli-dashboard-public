// AGY CLI Dashboard - Internationalization (i18n) Engine
// Supports English (Default) and German with persistent state and dynamic live switching

const AgyI18n = (() => {
  const STORAGE_KEY = 'agy_lang';
  let currentLang = 'en';
  const listeners = [];

  const translations = {
    en: {
      // Document metadata
      page_title: "AGY CLI Dashboard • Native Windows Cockpit for AI-Assisted Development",
      page_meta_desc: "The native Windows 11 desktop cockpit for Google Antigravity CLI. Featuring split-screen AI Co-Browser, multi-LLM orchestration, Git history control, and token telemetry.",

      // Navigation
      nav_features: "Features",
      nav_toolbar: "Action Bar",
      nav_governance: "Governance",
      nav_architecture: "Architecture",
      nav_knowledge: "Knowledge Pack",
      nav_quickstart: "Quickstart",
      nav_faq: "FAQ",
      nav_source: "Source Code",

      // Hero
      hero_pill: "WINDOWS 11 NATIVE • WINUI 3 &amp; .NET 10 • DE / EN BILINGUAL • 1-CLICK VOICE MODE",
      hero_title: "The Native Desktop Cockpit for<br><span class=\"title-sub\">AI-Assisted Software Development</span>",
      hero_desc: "Supercharge the Antigravity CLI, orchestrate subagents, inspect live DOM structures in the split-screen Co-Browser, and keep total clarity over Git history and token telemetry – all in a blazing-fast, native Windows app.",
      hero_companion: "<strong>Companion GUI Cockpit:</strong> Augments your installed Google Antigravity CLI (<code>agy</code>) with native WinUI 3 control – your terminal remains the heart of your workflow.",
      hero_btn_repo: "Source Code on GitHub",
      hero_btn_quickstart: "Quickstart (Clone &amp; Run)",
      hero_copy: "Copy",
      hero_copied: "Copied",
      hero_copy_title: "Copy command to clipboard: Copies 'git clone ...' directly into your terminal",

      // Mockup Title & Tabs
      mockup_window_title: "Antigravity CLI Dashboard — Community Edition",
      tab_cobrowser: "Split-Screen Co-Browser",
      tab_models: "Models &amp; Token Meter",
      tab_git: "Git Control &amp; History",
      tab_voice: "Voice Input &amp; Prompts",

      tab_cobrowser_title: "Co-Browser Split-Screen View: Web browser with AI automation and DOM inspection right next to the terminal",
      tab_models_title: "Model Selection &amp; Token Meter: Multi-provider choices (Gemini, Claude, GPT, DeepSeek) and live cost metrics",
      tab_git_title: "Git Control &amp; History: Version control, branch tracking, commit diffs, and automatic checkpoints",
      tab_voice_title: "Voice Input &amp; Prompts: 1-click microphone for hands-free dictation and reusable engineering prompts",

      // Mockup Tab 1: Co-Browser
      mockup_agent_session: "Antigravity Agent Session",
      mockup_badge_active: "ACTIVE",
      mockup_log_user: "user: Analyze the login structure and extract input fields.",
      mockup_log_agent: "agent: Launching AI Co-Browser and capturing DOM elements...",
      mockup_log_subtle: "DOM Map: 12 interactive nodes captured (reduces image token transfer by &gt;95%).",
      mockup_browser_title: "Chromium Co-Browser",
      mockup_browser_status: "Status: Ready • Human takeover available anytime",
      mockup_btn_login: "Sign In",

      // Mockup Tab 2: Models
      mockup_models_heading: "Model Management &amp; Live Cost Telemetry",
      mockup_models_desc: "Seamlessly switch between frontier models with transparent latency and token volume metrics.",
      mockup_lbl_active_model: "Active Model",
      mockup_lbl_model_stats: "Latency: 28ms • Context: 1,000,000 Tokens",
      mockup_lbl_session_burn: "Session Consumption",
      mockup_lbl_session_cost: "Total chat trajectory: &lt; $0.01",

      // Mockup Tab 3: Git
      mockup_git_heading: "Git Control &amp; Version History",
      mockup_git_desc: "Real-time branch tracking, dirty-state counter, commit timeline with timestamps, and auto cloud backup.",
      mockup_git_branch_status: "Branch: main • Status: 2 uncommitted changes",
      mockup_git_commit_sample: "[18dd9d0] docs(web): add interactive cockpit toolbar guide (10.10.2026 14:43)",
      mockup_git_backup_status: "Auto-Cloud Backup active (every 30 min) • 1-Click Hard Reset ready",

      // Mockup Tab 4: Voice
      mockup_voice_heading: "1-Click Voice Input &amp; Prompt Library",
      mockup_voice_desc: "Activate the built-in Antigravity CLI dictation mode with a single click, or access specialized roles and system prompts.",
      mockup_voice_pill_title: "VOICE DICTATION (/voice)",
      mockup_voice_pill_desc: "1-Click Start &amp; Stop • Transcription typed directly at cursor",
      mockup_tag_arch: "System: Code Architect",
      mockup_tag_sec: "Role: Security Auditor",
      mockup_tag_diff: "Template: Bugfix &amp; Diff",

      // Features Section
      feat_section_tag: "ARCHITECTURE &amp; CAPABILITIES",
      feat_section_title: "Engineered for High-Velocity Workflows",
      feat_section_desc: "Consolidated tooling for demanding software engineering tasks without disruptive context switches.",
      feat_1_title: "Integrated AI Co-Browser",
      feat_1_desc: "Side-by-side split-screen web inspection. Full Chrome DevTools Protocol (CDP) control, semantic DOM maps with numeric selectors, and instant human takeover.",
      feat_2_title: "Multi-LLM &amp; Token Level Meter",
      feat_2_desc: "Support for leading model families (Gemini 3.8 Flash, Claude Sonnet 5.5, GPT-6.1 Sol Pro). Reactive level meter warns of context overflow, complete with transparent latency and cost tracking.",
      feat_3_title: "Subagent Swarms &amp; Audits",
      feat_3_desc: "Structured delegation of parallel research and code review tasks. Automated four-eyes checks verify modifications before merging.",
      feat_4_title: "Git Control &amp; File Context",
      feat_4_desc: "Live branch tracking, dirty-state counters, and commit timeline right in the sidebar. Drag &amp; drop project files directly into the prompt; persistent date-ordered folder navigation.",
      feat_5_title: "1-Click Voice Input",
      feat_5_desc: "Native audio transcription typed directly at the cursor. Click the microphone button to start CLI dictation (/voice), speak your instructions, and click again to submit cleanly.",
      feat_6_title: "Native Performance &amp; i18n (EN/DE)",
      feat_6_desc: "Crafted with .NET 10 and WinUI 3 (no heavy Electron overhead). Blazing-fast ConPTY terminal, minimal RAM footprint, and full bilingual user interface (English &amp; German).",

      // Toolbar Guide Section
      tb_section_tag: "COCKPIT CONTROLS &amp; SHORTCUTS",
      tb_section_title: "The Action Bar in Detail",
      tb_section_desc: "Click any icon to discover its function, practical engineering purpose, and corresponding Numpad shortcut.",
      tb_btn_model_label: "Model",

      // Governance Section
      gov_section_tag: "GOVERNANCE &amp; SELF-HEALING",
      gov_section_title: "Why Your AI Agent Ships Better Code With the Dashboard",
      gov_section_desc: "Plain terminal agents suffer from session amnesia, operate without deterministic guardrails, and fail silently when tools crash. The Cockpit closes these vulnerabilities through active governance, persistent memory, and live diagnostics.",
      
      gov_card1_badge: "SINGLE SOURCE OF TRUTH",
      gov_card1_title: "Modular Rule Governance &amp; Guardrails",
      gov_card1_text: "Clean separation into global and project-specific rules (<code>rule_control.json</code> / <code>project_rules.json</code>). The Cockpit automatically injects binding coding standards, language directives, and safety guardrails into the context window before every prompt.",
      gov_card1_location: "Sidebar: Rule Governance &amp; Knowledge",
      gov_card1_impact: "Up to 90% fewer hallucinations",

      gov_card2_badge: "PERSISTENCE OVER AMNESIA",
      gov_card2_title: "Persistent Memory &amp; Audit Protocol",
      gov_card2_text: "Through <code>docs/knowledge.md</code>, the agent never forgets discovered framework traps or workarounds. Every agent decision is documented chronologically in <code>docs/protocol.md</code> with clickable links to the source transcript. Backed by automatic Git safety commits prior to code edits and 1-click dead-end cleanup.",
      gov_card2_location: "Cockpit Center: Git, Protocol &amp; Knowledge",
      gov_card2_impact: "Zero context loss on chat resets",

      gov_card3_badge: "AUTOMATED DIAGNOSTICS",
      gov_card3_title: "1-Click System Check &amp; Toolchain Validation",
      gov_card3_text: "Validates your entire engineering toolchain with one click: .NET 10 Windows SDK, WebView2 Evergreen Runtime, ConPTY terminal, local Antigravity CLI installation, and environment paths. Includes timestamp of the last successful run.",
      gov_card3_location: "Status Bar: Monitor Icon",
      gov_card3_impact: "Zero guesswork when tool calls fail",

      gov_card4_badge: "OVERCOMING KNOWLEDGE CUTOFF",
      gov_card4_title: "Knowledge Updates &amp; Model Sync",
      gov_card4_text: "The Knowledge Update macro teaches the AI freshly released CLI features, flags, and MCP servers autonomously. Simultaneously, the dashboard reads <code>agy models</code> live, aligns real context boundaries, and updates the token meter scale on the fly.",
      gov_card4_location: "Action Bar: Antenna &amp; Chip",
      gov_card4_impact: "Always up-to-date LLMs &amp; limits",

      // 3-Way Architectural Comparison
      comp_section_tag: "ARCHITECTURE &amp; COMPARISON",
      comp_section_title: "Which Antigravity Tool for Which Purpose?",
      comp_section_desc: "The official Antigravity technology stack exists in three main flavors. Compare strengths and tradeoffs at a glance.",

      comp_cli_tag: "TERMINAL NATIVE",
      comp_cli_title: "Google Antigravity CLI",
      comp_cli_sub: "Official, minimalist command-line interface (<code>agy</code>) for the Windows console or Linux/Mac terminals.",
      comp_cli_pro1: "Ultra-minimalist resource footprint in terminal (&lt; 35 MB RAM)",
      comp_cli_pro2: "Ideal for CI/CD pipelines and headless script automation",
      comp_cli_pro3: "Full access to official slash commands and MCP tools",
      comp_cli_con1: "Completely blind: No AI Co-Browser for web apps, DOM maps, or UI tests",
      comp_cli_con2: "Session amnesia: No cognitive long-term memory for learned lessons",
      comp_cli_con3: "No automatic safety guardrails ('go' approval gate / auto-reflection)",
      comp_cli_con4: "No automated ignore triad to protect against token waste",

      comp_ide_tag: "STANDALONE IDE",
      comp_ide_title: "Antigravity Desktop (IDE)",
      comp_ide_sub: "Standalone, cloud-bound development environment based on modern web/Electron technologies.",
      comp_ide_pro1: "Full graphical IDE experience with integrated code editor",
      comp_ide_pro2: "Integrated cloud synchronization and workspace management",
      comp_ide_pro3: "Beginner-friendly chat interface for visual developers",
      comp_ide_con1: "Noticeably higher RAM footprint (Electron runtime ~500 MB – 1.2 GB RAM)",
      comp_ide_con2: "No interactive Co-Browser with CDP control and accessibility tree",
      comp_ide_con3: "No programmatic 'go' safety gates or 21 one-click toolbar macros",
      comp_ide_con4: "Strict editor replacement rather than a lightweight companion for your setup",

      comp_dash_tag: "NATIVE WINDOWS COCKPIT (COMMUNITY)",
      comp_dash_title: "AGY CLI Dashboard",
      comp_dash_sub: "Native WinUI 3 &amp; .NET 10 cockpit designed as a visual companion on top of your local Antigravity CLI.",
      comp_dash_pro1: "<strong>True Native Windows 11</strong>: WinUI 3 &amp; .NET 10 (&lt; 100 MB RAM, cold start &lt; 0.8s, ConPTY DirectX)",
      comp_dash_pro2: "<strong>Integrated AI Co-Browser</strong>: Split-screen with CDP (Port 9222), Accessibility Tree &amp; Human Takeover",
      comp_dash_pro3: "<strong>Cognitive Long-Term Memory</strong>: Automated 2-tier knowledge base eliminates session amnesia",
      comp_dash_pro4: "<strong>Strict Safety Guardrails</strong>: Approval gate ('go'), deployment gate ('deploy') &amp; Auto-Reflection",
      comp_dash_pro5: "<strong>Context Hygiene</strong>: Automated ignore triad saves up to 80% context tokens",
      comp_dash_pro6: "<strong>21 One-Click Quick Actions</strong>: Toolbar macros for audits, Git safety, cleanups &amp; DE/EN toggle",
      comp_dash_note: "Requires the installed Antigravity CLI (<code>agy</code>) on the host system",

      // Feature Matrix
      matrix_heading: "Detailed Feature Matrix",
      matrix_desc: "Comprehensive feature and architecture comparison across all 28 core engineering capabilities.",
      matrix_legend_yes: "Natively Supported",
      matrix_legend_partial: "Limited / Config Required",
      matrix_legend_no: "Not Available",
      matrix_th_feature: "Feature / Capability",
      matrix_th_cli: "Google Antigravity CLI",
      matrix_th_ide: "Antigravity Desktop (IDE)",
      matrix_th_dash: "AGY CLI Dashboard",

      matrix_btn_expand: "Expand all 28 criteria &amp; detailed comparisons",
      matrix_btn_collapse: "Collapse to core highlights",
      matrix_count_expand: "+21 Criteria",
      matrix_count_collapse: "Compact",

      matrix_cat_1: "1. Architecture, UI Technology &amp; Performance",
      matrix_cat_2: "2. Web Automation &amp; AI Co-Browser",
      matrix_cat_3: "3. AI Models &amp; Reasoning Control",
      matrix_cat_4: "4. Code Quality Suite &amp; 1-Click Macros",
      matrix_cat_5: "5. Usability, Voice &amp; Ergonomics",
      matrix_cat_6: "6. Git, Diffs &amp; Localization",
      matrix_cat_7: "7. Cognition, Governance &amp; Guardrails",

      m_f1: "Technological Foundation",
      m_f2: "Memory Footprint (RAM)",
      m_f3: "Cold Start Time",
      m_f4: "Role in Developer Setup",
      m_f5: "Split-Screen Co-Browser",
      m_f6: "CDP &amp; WebSocket Server",
      m_f7: "Interactive DOM Maps",
      m_f8: "Human Takeover Support",
      m_f9: "Google Gemini Frontier Models",
      m_f10: "Multi-LLM Hot-Switching",
      m_f11: "Live Latency &amp; Level Meter",
      m_f12: "DeepThink / Reasoning Budget",
      m_f13: "Auto-Reflection Pre-Audit",
      m_f14: "E2E &amp; Test Automation",
      m_f15: "Dead-Code &amp; Cleanup Scan",
      m_f16: "Global Root-Cause Analysis",
      m_f17: "Dead-End &amp; Workaround Cleanup",
      m_f18: "Zero-Bug &amp; Simplify Audit",
      m_f19: "1-Click Voice Input",
      m_f20: "Keyboard Cockpit (Numpad 0–9)",
      m_f21: "Drag &amp; Drop File Context",
      m_f22: "Subagent Swarm Orchestration",
      m_f23: "Git Control &amp; Version History",
      m_f24: "Git Safety Commit Pre-Edit",
      m_f25: "Bilingual Localization (EN / DE)",
      m_f26: "Cognitive Long-Term Memory",
      m_f27: "Strict Safety Guardrails",
      m_f28: "Context Hygiene (Ignore Triad)",

      // Quickstart
      qs_section_tag: "OPEN SOURCE",
      qs_section_title: "Quickstart in Three Steps",
      qs_section_desc: "The Community Edition runs 100% open source directly from code – transparent, auditable, and without installation baggage.",
      qs_step1_badge: "[STEP 01]",
      qs_step1_title: "Clone",
      qs_step1_desc: "Clone the repository to your machine using Git (Prerequisites: .NET 10 SDK &amp; Antigravity CLI <code>agy</code> in PATH).",
      qs_step2_badge: "[STEP 02]",
      qs_step2_title: "Launch",
      qs_step2_desc: "Navigate to the folder and run <code>start.cmd</code>. The script validates prerequisites (.NET 10, WebView2, agy) and launches the app automatically.",
      qs_step3_badge: "[STEP 03]",
      qs_step3_title: "Build",
      qs_step3_desc: "Open your project workspace and delegate tasks to the agent. The dashboard coordinates execution inside the native ConPTY terminal.",
      qs_step3_snippet: "Ready for input",

      // FAQ
      faq_section_tag: "QUESTIONS &amp; ANSWERS",
      faq_section_title: "Frequently Asked Questions",
      faq_q1: "Does the Dashboard require the Google Antigravity CLI?",
      faq_a1: "Yes. The dashboard acts as a visual control cockpit for the official Antigravity CLI. If the CLI tool is not yet found in PATH, the status bar alerts you on startup with a direct link to the System Check.",
      faq_q2: "Which Windows versions are supported?",
      faq_a2: "Supported on 64-bit versions of Windows 10 (Build 19041 and newer) and Windows 11. The application leverages the official Windows App SDK (WinUI 3) runtime.",
      faq_q3: "Under what license is the project published?",
      faq_a3: "The project is completely open source and published under the permissive MIT License. The source code is freely available on GitHub.",
      faq_q4: "How is the AI Co-Browser technically implemented?",
      faq_a4: "The integration is powered by Microsoft Edge WebView2. Communication between the agent and browser runs over an isolated WebSocket endpoint via the Chrome DevTools Protocol (CDP).",

      // Knowledge Pack
      kp_section_tag: "CURATED AI KNOWLEDGE",
      kp_section_title: "Battle-Tested AI Engineering Knowledge Pack",
      kp_section_desc: "5 modular, leak-audited knowledge files extracted from hundreds of hours of autonomous coding sessions. Drop them into your agent's custom-knowledge directory to instantly eliminate hallucinations and recurring framework pitfalls.",
      kp_c1_title: "WinUI 3 &amp; XAML Desktop Core",
      kp_c1_desc: "DispatcherQueue thread safety, unmanaged ConPTY pseudoconsole hosting, ContentDialog lifecycle, and WinUI 3 XAML theme styling.",
      kp_c1_tag: "winui3-patterns-and-airspace.md",
      kp_c2_title: "C# .NET 10 &amp; Win32 Interop",
      kp_c2_desc: "AOT compatibility, high-DPI PerMonitorV2 scaling, memory-safe P/Invoke bindings, and clean async task orchestration.",
      kp_c2_tag: "csharp-dotnet10-high-performance.md",
      kp_c3_title: "JavaScript &amp; TypeScript Web Core",
      kp_c3_desc: "Modern reactive DOM manipulation, CDP WebSocket bridges, zero-dependency client state, and robust DOM map generation.",
      kp_c3_tag: "javascript-safari-storage-and-unicode.md",
      kp_c4_title: "Co-Browser &amp; Automation Stability",
      kp_c4_desc: "Deep-DOM throttling, CDP protocol timeouts, Shadow-DOM traversal, and resilient error recovery patterns.",
      kp_c4_tag: "browser-dom-and-network-stability.md",
      kp_c5_title: "PowerShell &amp; System Interop",
      kp_c5_desc: "Process tree isolation, Job Object memory quotas, ConPTY clean shutdowns, and low-latency UI rendering.",
      kp_c5_tag: "powershell-async-interop.md",
      kp_cta_title: "Share Your AI Knowledge — Let's Build the Open Collective",
      kp_cta_desc: "Did your agent discover valuable edge cases or framework quirks? Cleanse private project details from your markdown notes and open a Pull Request. Every contribution makes the community's agents smarter.",
      kp_cta_btn_view: "Browse Knowledge Pack",
      kp_cta_btn_download: "Download Knowledge Pack (.zip)",
      kp_cta_btn_contribute: "Contribute on GitHub",
      kp_guide_title: "How to Use the Knowledge Pack in 3 Simple Steps",
      kp_step1_badge: "STEP 1 • DOWNLOAD",
      kp_step1_desc: "Download the ZIP package above, or click the 1-Click Starter-Pack button directly inside the app under 'Knowledge'.",
      kp_step2_badge: "STEP 2 • IMPORT",
      kp_step2_desc: "Open the 'Knowledge' tab in your Dashboard and click the Import icon (arrow down) under 'Custom Knowledge' to load the .md files.",
      kp_step3_badge: "STEP 3 • EXPORT & SHARE",
      kp_step3_desc: "Click the Export icon (cloud) to backup your own accumulated knowledge or submit valuable insights to the community via GitHub PR.",

      // CTA Banner & Footer
      cta_title: "Ready for Transparent AI Engineering?",
      cta_desc: "Clone the repository on GitHub or contribute to the open-source community.",
      cta_btn_repo: "View Repository on GitHub",
      cta_btn_qs: "Go to Quickstart",
      footer_brand: "AGY CLI Dashboard • Community Edition",
      footer_tech: "Open Source under MIT License • WinUI 3 &amp; .NET 10",
      footer_disclaimer: "Notice: This project is an independent open-source community tool and is not affiliated with Google LLC. Antigravity CLI and Gemini are trademarks or registered trademarks of Google LLC. All other trademarks belong to their respective owners."
    },

    de: {
      // Document metadata
      page_title: "AGY CLI Dashboard • Desktop-Cockpit für KI-gestützte Entwicklung",
      page_meta_desc: "Das native Windows 11 Desktop-Cockpit für die Antigravity CLI. Mit integriertem Split-Screen KI-Co-Browser, Multi-LLM Steuerung, Git-Historien-Zentrale und Token-Pegelanzeige.",

      // Navigation
      nav_features: "Funktionen",
      nav_toolbar: "Aktionsleiste",
      nav_governance: "Governance",
      nav_architecture: "Architektur",
      nav_knowledge: "Wissens-Paket",
      nav_quickstart: "Schnellstart",
      nav_faq: "FAQ",
      nav_source: "Quellcode",

      // Hero
      hero_pill: "WINDOWS 11 NATIVE • WINUI 3 &amp; .NET 10 • DE / EN BILINGUAL • 1-KLICK DIKTATMODUS",
      hero_title: "Das native Desktop-Cockpit für<br><span class=\"title-sub\">KI-gestützte Software-Entwicklung</span>",
      hero_desc: "Steuere die Antigravity CLI, koordiniere Subagenten, prüfe DOM-Strukturen im Split-Screen Co-Browser und behalte den vollen Überblick über Git-Historie und Token-Verbrauch – in einer performanten, nativen Windows-App.",
      hero_companion: "<strong>Begleitendes GUI-Cockpit:</strong> Ergänzt deine installierte Google Antigravity CLI (<code>agy</code>) um native WinUI 3 Steuerung – dein Terminal bleibt das Herzstück.",
      hero_btn_repo: "Quellcode auf GitHub",
      hero_btn_quickstart: "Schnellstart (Clone &amp; Run)",
      hero_copy: "Kopieren",
      hero_copied: "Kopiert",
      hero_copy_title: "Befehl in Zwischenablage kopieren: Kopiert 'git clone ...' direkt in dein Windows-Terminal",

      // Mockup Title & Tabs
      mockup_window_title: "Antigravity CLI Dashboard — Community Edition",
      tab_cobrowser: "Split-Screen Co-Browser",
      tab_models: "Modelle &amp; Token-Meter",
      tab_git: "Git-Zentrale &amp; Historie",
      tab_voice: "Spracheingabe &amp; Prompts",

      tab_cobrowser_title: "Co-Browser Split-Screen Ansicht: Zeigt den integrierten Web-Browser mit KI-Automatisierung und DOM-Inspektion direkt neben dem Terminal",
      tab_models_title: "Modell-Auswahl &amp; Token-Meter Ansicht: Zeigt die freie Provider-Wahl (Gemini, Claude, GPT, DeepSeek) und die transparente Token-Kostenüberwachung",
      tab_git_title: "Git-Zentrale &amp; Historie Ansicht: Zeigt Versionskontrolle, Branch-Verwaltung, Commit-Diffs und automatische Sicherheits-Checkpoints",
      tab_voice_title: "Spracheingabe &amp; Prompts Ansicht: Zeigt das 1-Klick-Mikrofon für freihändiges Diktieren und vorgefertigte Entwickler-Prompts",

      // Mockup Tab 1: Co-Browser
      mockup_agent_session: "Antigravity Agent Session",
      mockup_badge_active: "AKTIV",
      mockup_log_user: "user: Analysiere die Login-Struktur und extrahiere Eingabefelder.",
      mockup_log_agent: "agent: Starte KI-Co-Browser und erfasse DOM-Elemente...",
      mockup_log_subtle: "DOM-Map: 12 interaktive Knoten erfasst (reduziert Bild-Token-Transfer um &gt;95%).",
      mockup_browser_title: "Chromium Co-Browser",
      mockup_browser_status: "Status: Bereit • Menschlicher Eingriff (Takeover) jederzeit möglich",
      mockup_btn_login: "Anmelden",

      // Mockup Tab 2: Models
      mockup_models_heading: "Modellverwaltung &amp; Live-Kostenerfassung",
      mockup_models_desc: "Direkter Wechsel zwischen Spitzenmodellen bei transparenter Erfassung von Latenz und Token-Volumen.",
      mockup_lbl_active_model: "Aktives Modell",
      mockup_lbl_model_stats: "Latenz: 28ms • Kontext: 1.000.000 Tokens",
      mockup_lbl_session_burn: "Session-Verbrauch",
      mockup_lbl_session_cost: "Gesamter Chat-Verlauf: &lt; 0,01 $",

      // Mockup Tab 3: Git
      mockup_git_heading: "Git-Zentrale &amp; Versionshistorie",
      mockup_git_desc: "Echtzeit-Tracking von Branches und ungesicherten Änderungen, Commit-Historie mit Zeitstempeln und Auto-Cloud-Backup.",
      mockup_git_branch_status: "Branch: main • Status: 2 uncommitted changes",
      mockup_git_commit_sample: "[18dd9d0] docs(web): add interactive cockpit toolbar guide (10.10.2026 14:43)",
      mockup_git_backup_status: "Auto-Cloud-Backup aktiv (alle 30 Min.) • 1-Klick Hard-Reset bereit",

      // Mockup Tab 4: Voice
      mockup_voice_heading: "1-Klick Spracheingabe &amp; Prompt-Bibliothek",
      mockup_voice_desc: "Aktiviere den integrierten Diktatmodus der Antigravity CLI mit einem einzigen Klick oder greife auf vorgefertigte Rollen- und System-Prompts zu.",
      mockup_voice_pill_title: "DIKTATMODUS (/voice)",
      mockup_voice_pill_desc: "1-Klick Start &amp; Stopp • Transkription direkt am Cursor",
      mockup_tag_arch: "System: Code-Architekt",
      mockup_tag_sec: "Rolle: Security-Auditor",
      mockup_tag_diff: "Template: Bugfix &amp; Diff",

      // Features Section
      feat_section_tag: "ARCHITEKTUR &amp; FUNKTIONEN",
      feat_section_title: "Konzipiert für produktive Workflows",
      feat_section_desc: "Konsolidierte Werkzeuge für anspruchsvolle Entwicklungsaufgaben ohne störende Kontextwechsel.",
      feat_1_title: "Integrierter KI-Co-Browser",
      feat_1_desc: "Parallele Webansicht im Split-Screen. Vollständige Steuerung über das Chrome DevTools Protocol (CDP), semantische DOM-Karten mit numerischen Selektoren und direkter Human-Takeover.",
      feat_2_title: "Multi-LLM &amp; Token-Pegelanzeige",
      feat_2_desc: "Unterstützung für führende Modellfamilien (Gemini 3.8 Flash, Claude Sonnet 5.5, GPT-6.1 Sol Pro). Reaktive Pegelanzeige warnt vor Kontext-Overflows, inklusive transparenter Latenz- und Kostenmessung.",
      feat_3_title: "Subagenten-Schwärme &amp; Audits",
      feat_3_desc: "Strukturierte Delegation paralleler Recherche- und Review-Aufgaben. Automatisierte Vier-Augen-Prüfungen sichern Codeänderungen vor dem Zusammenführen ab.",
      feat_4_title: "Git-Zentrale &amp; Dateikontext",
      feat_4_desc: "Live-Branch-Tracking, Dirty-State-Zähler und Commit-Historie in der Seitenleiste. Ziehe Projektdateien per Drag &amp; Drop direkt als Kontext in den Chat; persistente Ordnersortierung nach Datum.",
      feat_5_title: "1-Klick Spracheingabe",
      feat_5_desc: "Integrierte Audio-Transkription direkt am Cursor. Klicke auf die Mikrofon-Taste, um das CLI-Diktat (/voice) zu starten, sprich deinen Prompt ein und beende die Aufnahme sauber per Klick.",
      feat_6_title: "Native Performance &amp; i18n (DE/EN)",
      feat_6_desc: "Entwickelt mit .NET 10 und WinUI 3 (kein schwerfälliges Electron). Schnelles ConPTY-Terminal, minimaler RAM-Bedarf und vollständige zweisprachige Benutzeroberfläche (Deutsch &amp; Englisch).",

      // Toolbar Guide Section
      tb_section_tag: "COCKPIT-STEUERUNG &amp; TASTATUR-SHORTCUTS",
      tb_section_title: "Die Aktionsleiste im Detail",
      tb_section_desc: "Klicke auf ein beliebiges Symbol, um Funktion, Verwendungszweck und den zugehörigen Numpad-Shortcut kennenzulernen.",
      tb_btn_model_label: "Modell",

      // Governance Section
      gov_section_tag: "GOVERNANCE &amp; SELBSTHEILUNG",
      gov_section_title: "Warum deine KI mit dem Dashboard bessere Antworten liefert",
      gov_section_desc: "Reine Terminal-Agenten leiden unter Amnesie, agieren ohne verbindliche Leitplanken und brechen bei Tool-Fehlern stumm ab. Das Cockpit schließt diese Schwachstellen durch eine aktive Governance-, Wissens- und Diagnose-Ebene.",
      
      gov_card1_badge: "SINGLE SOURCE OF TRUTH",
      gov_card1_title: "Modulare Regel-Steuerung &amp; Leitplanken",
      gov_card1_text: "Saubere Trennung in globale und projektspezifische Regeln (<code>rule_control.json</code> / <code>project_rules.json</code>). Das Cockpit injiziert verbindliche Coding-Standards, Sprachvorgaben und Sicherheitsregeln vor jedem Prompt automatisch in das Kontextfenster.",
      gov_card1_location: "Sidebar: Regel-Steuerung &amp; Wissen",
      gov_card1_impact: "Bis zu 90% weniger Halluzinationen",

      gov_card2_badge: "PERSISTENZ STATT AMNESIE",
      gov_card2_title: "Persistentes Gedächtnis &amp; Protokoll",
      gov_card2_text: "Über <code>docs/knowledge.md</code> vergisst die KI nie wieder entdeckte Framework-Fallen oder Workarounds. Jede Agentenentscheidung wird in <code>docs/protocol.md</code> mit Klick-Verbindung zum Quell-Transkript dokumentiert. Geschützt durch automatische Git-Sicherungs-Commits vor jedem Code-Eingriff und 1-Klick Irrweg-Bereinigung.",
      gov_card2_location: "Cockpit-Mitte: Git, Protokoll &amp; Wissen",
      gov_card2_impact: "Null Kontextverlust bei Chat-Resets",

      gov_card3_badge: "AUTOMATISIERTE DIAGNOSE",
      gov_card3_title: "1-Klick System-Check &amp; Toolchain-Validierung",
      gov_card3_text: "Validiert die gesamte Entwicklungs-Toolchain auf Knopfdruck: .NET 10 Windows SDK, WebView2 Evergreen Runtime, ConPTY-Terminal, lokale Antigravity CLI Installation und Umgebungspfade. Inklusive Zeitstempel der letzten erfolgreichen Ausführung.",
      gov_card3_location: "Statusleiste: Monitor-Icon",
      gov_card3_impact: "Kein Rätselraten bei Tool-Abbrüchen",

      gov_card4_badge: "KNOWLEDGE CUTOFF ÜBERWUNDEN",
      gov_card4_title: "Wissens-Update &amp; Modell-Synchronisation",
      gov_card4_text: "Das Wissens-Update-Makro bringt der KI neu erschienene CLI-Features, Flags und MCP-Server selbstständig bei. Parallel liest das Dashboard <code>agy models</code> live aus, gleicht reale Grenzwerte ab und aktualisiert die Token-Meter-Skala im laufenden Betrieb.",
      gov_card4_location: "Aktionsleiste: Funkantenne &amp; Chip",
      gov_card4_impact: "Stets aktuelle LLMs &amp; Limits",

      // 3-Way Architectural Comparison
      comp_section_tag: "ARCHITEKTUR &amp; VERGLEICH",
      comp_section_title: "Welches Antigravity-Werkzeug für welchen Zweck?",
      comp_section_desc: "Die offizielle Antigravity-Technologie existiert in drei primären Ausprägungen. Finde auf einen Blick heraus, wo die Stärken und Grenzen liegen.",

      comp_cli_tag: "TERMINAL-NATIV",
      comp_cli_title: "Google Antigravity CLI",
      comp_cli_sub: "Offizielles, minimalistisches Command-Line-Interface (<code>agy</code>) für die Windows-Konsole oder Linux/Mac-Terminals.",
      comp_cli_pro1: "Ultra-minimalistischer Ressourcen-Fußabdruck im Terminal (&lt; 35 MB RAM)",
      comp_cli_pro2: "Hervorragend geeignet für CI/CD-Pipelines und Headless-Skriptautomatisierung",
      comp_cli_pro3: "Voller Zugriff auf offizielle Slash-Commands und MCP-Werkzeuge",
      comp_cli_con1: "Vollständig blind: Kein KI-Co-Browser für Webseiten, DOM-Maps oder UI-Tests",
      comp_cli_con2: "Session-Amnesie: Kein kognitives Langzeitgedächtnis für gelernte Lektionen",
      comp_cli_con3: "Keine automatischen Schutzschienen ('go'-Freigabesperre / Auto-Reflexion)",
      comp_cli_con4: "Kein automatisierter Ignore-Dreiklang zum Schutz vor Token-Verschwendung",

      comp_ide_tag: "STANDALONE-IDE",
      comp_ide_title: "Antigravity Desktop (IDE)",
      comp_ide_sub: "Eigenständige, cloud-gebundene Entwicklungsumgebung basierend auf modernen Web-/Electron-Technologien.",
      comp_ide_pro1: "Vollständige grafische IDE-Erfahrung mit integriertem Code-Editor",
      comp_ide_pro2: "Integrierte Cloud-Synchronisation und Workspace-Verwaltung",
      comp_ide_pro3: "Einsteigerfreundliches Chat-Interface für visuelle Entwickler",
      comp_ide_con1: "Spürbar höherer Arbeitsspeicher-Bedarf (Electron-Laufzeitumgebung ca. 500 MB – 1,2 GB RAM)",
      comp_ide_con2: "Kein interaktiver Co-Browser mit CDP-Steuerung und barrierefreiem Accessibility-Tree",
      comp_ide_con3: "Keine programmatischen 'go'-Schutzgitter oder 21 One-Click Toolbar-Makros",
      comp_ide_con4: "Strikter Editor-Ersatz statt leichtgewichtiger Begleiter für bestehende Setups",

      comp_dash_tag: "NATIVES WINDOWS-COCKPIT (COMMUNITY)",
      comp_dash_title: "AGY CLI Dashboard",
      comp_dash_sub: "Natives WinUI 3 &amp; .NET 10 Cockpit als visueller Begleiter, der auf deiner lokalen Antigravity CLI aufsetzt.",
      comp_dash_pro1: "<strong>Echtes natives Windows 11</strong>: WinUI 3 &amp; .NET 10 (&lt; 100 MB RAM, Kaltstart &lt; 0,8 s, ConPTY DirectX)",
      comp_dash_pro2: "<strong>Integrierter KI-Co-Browser</strong>: Split-Screen mit CDP (Port 9222), Accessibility-Tree &amp; Human-Takeover",
      comp_dash_pro3: "<strong>Kognitives Langzeitgedächtnis</strong>: Automatische 2-Ebenen Knowledge-Base gegen Session-Amnesie",
      comp_dash_pro4: "<strong>Strikte Schutz-Leitplanken</strong>: Freigabe-Pflicht ('go'), Deployment-Gate ('deploy') &amp; Auto-Reflexion",
      comp_dash_pro5: "<strong>Kontext-Hygiene</strong>: Automatisierter Ignore-Dreiklang spart bis zu 80 % Kontext-Tokens",
      comp_dash_pro6: "<strong>21 One-Click Quick-Actions</strong>: Toolbar-Makros für Audits, Git-Sicherungen, Cleanups &amp; DE/EN-Wechsel",
      comp_dash_note: "Erfordert die installierte Antigravity CLI (<code>agy</code>) auf dem Host-System",

      // Feature Matrix
      matrix_heading: "Detaillierte Feature-Matrix",
      matrix_desc: "Vollständiger Funktions- und Architekturvergleich über alle 28 Kernmerkmale.",
      matrix_legend_yes: "Nativ unterstützt",
      matrix_legend_partial: "Eingeschränkt / Config",
      matrix_legend_no: "Nicht vorhanden",
      matrix_th_feature: "Funktion / Kriterium",
      matrix_th_cli: "Google Antigravity CLI",
      matrix_th_ide: "Antigravity Desktop (IDE)",
      matrix_th_dash: "AGY CLI Dashboard",

      matrix_btn_expand: "Alle 28 Kriterien &amp; Detailvergleiche ausklappen",
      matrix_btn_collapse: "Auf Kern-Highlights minimieren",
      matrix_count_expand: "+21 Kriterien",
      matrix_count_collapse: "Kompakt",

      matrix_cat_1: "1. Architektur, UI-Technologie &amp; Performance",
      matrix_cat_2: "2. Web-Automation &amp; KI-Co-Browser",
      matrix_cat_3: "3. KI-Modelle &amp; Reasoning-Steuerung",
      matrix_cat_4: "4. Code-Qualitäts-Suite &amp; 1-Klick-Makros",
      matrix_cat_5: "5. Bedienung, Voice &amp; Ergonomie",
      matrix_cat_6: "6. Git, Diffs &amp; Lokalisierung",
      matrix_cat_7: "7. Kognition, Governance &amp; Schutz-Leitplanken",

      m_f1: "Technologische Basis",
      m_f2: "Arbeitsspeicher (RAM)",
      m_f3: "Kaltstartzeit",
      m_f4: "Rolle im Setup",
      m_f5: "Split-Screen Co-Browser",
      m_f6: "CDP- &amp; WebSocket-Server",
      m_f7: "Interaktive DOM-Maps",
      m_f8: "Human Takeover (Eingriff)",
      m_f9: "Google Gemini Spitzenmodelle",
      m_f10: "Multi-LLM Hot-Switching",
      m_f11: "Live-Latenz- &amp; Pegelanzeige",
      m_f12: "DeepThink / Reasoning-Budget",
      m_f13: "Auto-Reflexion vor Abschluss",
      m_f14: "E2E- &amp; Test-Automation",
      m_f15: "Dead-Code- &amp; Bereinigungs-Scan",
      m_f16: "Globale Fehlersuche (Root Cause)",
      m_f17: "Irrweg- &amp; Workaround-Cleanup",
      m_f18: "Zero-Bug &amp; Simplify-Audit",
      m_f19: "1-Klick Spracheingabe (Voice)",
      m_f20: "Tastatur-Cockpit (Numpad 0–9)",
      m_f21: "Drag &amp; Drop Dateikontext",
      m_f22: "Subagenten-Schwärme",
      m_f23: "Git-Zentrale &amp; Versionshistorie",
      m_f24: "Git-Sicherungs-Commit vor Arbeit",
      m_f25: "Zweisprachig (DE / EN)",
      m_f26: "Kognitives Langzeitgedächtnis",
      m_f27: "Strikte Schutz-Leitplanken",
      m_f28: "Kontext-Hygiene (Ignore-Dreiklang)",

      // Quickstart
      qs_section_tag: "OPEN SOURCE",
      qs_section_title: "Schnellstart in drei Schritten",
      qs_section_desc: "Die Community Edition läuft als reines Open-Source-Projekt direkt aus dem Quellcode – transparent, auditierbar und ohne Installations-Ballast.",
      qs_step1_badge: "[SCHRITT 01]",
      qs_step1_title: "Klonen",
      qs_step1_desc: "Das Repository mit Git auf deinen Rechner klonen (Voraussetzung: .NET 10 SDK &amp; Antigravity CLI <code>agy</code> im PATH).",
      qs_step2_badge: "[SCHRITT 02]",
      qs_step2_title: "Starten",
      qs_step2_desc: "In den Ordner wechseln und <code>start.cmd</code> ausführen. Das Skript prüft Voraussetzungen (.NET 10, WebView2, agy) und startet die App automatisch.",
      qs_step3_badge: "[SCHRITT 03]",
      qs_step3_title: "Loslegen",
      qs_step3_desc: "Projektordner öffnen und Aufgaben an den Agenten übergeben. Das Dashboard koordiniert die Ausführung im nativen ConPTY-Terminal.",
      qs_step3_snippet: "Bereit zur Eingabe",

      // FAQ
      faq_section_tag: "FRAGEN &amp; ANTWORTEN",
      faq_section_title: "Häufig gestellte Fragen",
      faq_q1: "Benötigt das Dashboard die Google Antigravity CLI?",
      faq_a1: "Ja. Das Dashboard fungiert als visuelle Steuerungsebene für die offizielle Antigravity CLI. Ist das CLI-Werkzeug noch nicht im PATH vorhanden, signalisiert die Statusanzeige dies beim Start und verlinkt direkt auf den System-Check.",
      faq_q2: "Welche Windows-Versionen werden unterstützt?",
      faq_a2: "Unterstützt werden 64-Bit-Versionen von Windows 10 (ab Build 19041) sowie Windows 11. Die Anwendung nutzt die Windows App SDK (WinUI 3) Laufzeitumgebung.",
      faq_q3: "Unter welcher Lizenz steht das Projekt?",
      faq_a3: "Das Projekt ist vollständig Open Source und unter der MIT-Lizenz veröffentlicht. Der Quellcode ist auf GitHub frei zugänglich.",
      faq_q4: "Wie ist der Co-Browser technisch realisiert?",
      faq_a4: "Die Integration basiert auf Microsoft Edge WebView2. Die Kommunikation zwischen Agent und Browser erfolgt über einen isolierten WebSocket-Endpunkt via Chrome DevTools Protocol (CDP).",

      // Knowledge Pack
      kp_section_tag: "KURATIERTES KI-WISSEN",
      kp_section_title: "Praxiserprobtes KI-Engineering Wissenspaket",
      kp_section_desc: "5 modulare, leak-geprüfte Wissensdateien aus hunderten Stunden autonomer Coding-Sessions. Kopiere sie in das custom-knowledge Verzeichnis deines Agenten, um Halluzinationen und gängige API-Fehler sofort zu eliminieren.",
      kp_c1_title: "WinUI 3 &amp; XAML Desktop-Kern",
      kp_c1_desc: "DispatcherQueue Thread-Sicherheit, ConPTY Terminal-Hosting, ContentDialog Lebenszyklus und WinUI 3 XAML Theme-Styling.",
      kp_c1_tag: "winui3-patterns-and-airspace.md",
      kp_c2_title: "C# .NET 10 &amp; Win32 Interop",
      kp_c2_desc: "AOT-Kompatibilität, High-DPI Skalierung, Unmanaged ConPTY Pseudoconsole-Interop und speichersichere Async-Muster.",
      kp_c2_tag: "csharp-dotnet10-high-performance.md",
      kp_c3_title: "JavaScript &amp; TypeScript Web-Kern",
      kp_c3_desc: "Moderne reactive DOM-Manipulation, CDP-WebSocket-Bridges, schlanke Client-Zustände und robuste DOM-Map Generierung.",
      kp_c3_tag: "javascript-safari-storage-and-unicode.md",
      kp_c4_title: "Co-Browser &amp; Automatisierungs-Stabilität",
      kp_c4_desc: "Deep-DOM Drosselung, CDP Protokoll-Timeouts, Shadow-DOM Traversierung und defensive Fehlererholung.",
      kp_c4_tag: "browser-dom-and-network-stability.md",
      kp_c5_title: "PowerShell &amp; System-Interop",
      kp_c5_desc: "Prozessbaum-Isolation, Job Object Speichergrenzen, saubere ConPTY Beendigung und latenzarmes UI-Rendering.",
      kp_c5_tag: "powershell-async-interop.md",
      kp_cta_title: "Teile dein KI-Wissen — Baue an der Open-Source Wissensbasis mit",
      kp_cta_desc: "Hat dein Agent wertvolle Randfälle oder Framework-Eigenheiten gelöst? Bereinige deine Notizen um private Projektdaten und öffne einen Pull Request. Jeder Beitrag macht die Agenten der Community intelligenter.",
      kp_cta_btn_view: "Wissenspaket ansehen",
      kp_cta_btn_download: "Knowledge Pack herunterladen (.zip)",
      kp_cta_btn_contribute: "Auf GitHub beitragen",
      kp_guide_title: "So nutzt du das Knowledge Pack in 3 einfachen Schritten",
      kp_step1_badge: "SCHRITT 1 • DOWNLOAD",
      kp_step1_desc: "Lade das ZIP-Paket oben herunter oder nutze in der App unter 'Wissen' direkt den 1-Klick Starter-Pack Button.",
      kp_step2_badge: "SCHRITT 2 • IMPORTIEREN",
      kp_step2_desc: "Öffne im Dashboard den Tab 'Wissen' und klicke bei 'Benutzerdefiniertes Wissen' auf das Import-Icon (Pfeil nach unten), um die .md-Dateien zu laden.",
      kp_step3_badge: "SCHRITT 3 • EXPORT & TEILEN",
      kp_step3_desc: "Klicke auf das Export-Icon (Wolke), um dein eigenes gesammeltes Wissen als Backup zu sichern oder per Pull Request auf GitHub beizutragen.",

      // CTA Banner & Footer
      cta_title: "Bereit für transparente KI-Entwicklung?",
      cta_desc: "Klone das Repository auf GitHub oder beteilige dich an der Open-Source-Community.",
      cta_btn_repo: "Repository auf GitHub ansehen",
      cta_btn_qs: "Zum Schnellstart",
      footer_brand: "AGY CLI Dashboard • Community Edition",
      footer_tech: "Open Source unter MIT-Lizenz • WinUI 3 &amp; .NET 10",
      footer_disclaimer: "Hinweis: Dieses Projekt ist ein unabhängiges Open-Source-Community-Werkzeug und steht in keiner geschäftlichen Verbindung zu Google LLC. Antigravity CLI und Gemini sind Marken oder eingetragene Marken von Google LLC. Alle weiteren Marken sind Eigentum ihrer jeweiligen Inhaber."
    }
  };

  const toolbarData = {
    en: {
      context: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>',
        name: "Attach File Context",
        type: "FILE DIALOG & DROP",
        shortcut: "Click / Drag & Drop",
        desc: "Opens the native Windows file picker to attach source code files, logs, PDFs, or screenshots directly as prompt reference context. Also supports direct drag & drop."
      },
      model: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#818cf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20v2"/><path d="M12 2v2"/><path d="M17 20v2"/><path d="M17 2v2"/><path d="M2 12h2"/><path d="M2 17h2"/><path d="M2 7h2"/><path d="M20 12h2"/><path d="M20 17h2"/><path d="M20 7h2"/><path d="M7 20v2"/><path d="M7 2v2"/><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="8" y="8" width="8" height="8" rx="1"/></svg>',
        name: "Model Selection & Provider Switch",
        type: "SELECTION MODAL",
        shortcut: "Ctrl+M or Numpad 0",
        desc: "Opens the quick switcher for AI models. Hot-swap between Google Gemini 3.8 Flash, Anthropic Claude Sonnet 5.5, OpenAI GPT-6.1 Sol Pro, and DeepSeek V4.1 – complete with real measured latency benchmarks."
      },
      deepthink: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f59e0b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13,2 L3,14 h9 L11,22 L21,10 h-9 L13,2 z"/></svg>',
        name: "DeepThink Reasoning Mode",
        type: "LOGIC TOGGLE",
        shortcut: "Numpad 1",
        desc: "Toggles extended thinking budget (Chain-of-Thought / Reasoning) for complex architecture planning, compiler debugging, and non-trivial algorithmic engineering."
      },
      docs: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22,19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V5 A2,2 0 0 1 4,3 h5 L11,6 h9 A2,2 0 0 1 22,8 z M2,9 h20"/></svg>',
        name: "Inject Project Documentation",
        type: "PROMPT MACRO",
        shortcut: "Numpad 2",
        desc: "Automatically reads current project documentation (docs/knowledge.md, docs/protocol.md, project_rules.json) and injects it as structured system context into the next prompt."
      },
      subagent: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#a78bfa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 v4 M8,6 h8 M4,10 A2,2 0 0 1 6,8 h12 A2,2 0 0 1 20,10 v8 A2,2 0 0 1 18,20 H6 A2,2 0 0 1 4,18 v-8 z M9,13 h0.01 M15,13 h0.01"/></svg>',
        name: "Coordinate Subagent Swarms",
        type: "SWARM MODAL",
        shortcut: "Numpad 3",
        desc: "Opens the Subagent Manager to spawn specialized agents (e.g. neutral code auditor, security reviewer, parallel researchers). Distributes heavy tasks without cluttering the primary context window."
      },
      cobrowser: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 A10,10 0 1 0 22,12 A10,10 0 0 0 12,2 z M2,12 h20 M12,2 A15.3,15.3 0 0 1 16,12 A15.3,15.3 0 0 1 12,22 A15.3,15.3 0 0 1 8,12 A15.3,15.3 0 0 1 12,2 z"/></svg>',
        name: "AI Co-Browser Split-Screen",
        type: "SPLIT-SCREEN TOGGLE",
        shortcut: "Numpad 5",
        desc: "Opens or collapses the embedded Chromium WebView2 browser alongside the terminal. Enables visual DOM inspection, interactive node tagging, and seamless human takeover."
      },
      reflexion: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#60a5fa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1,12 S5,4 12,4 S23,12 23,12 S19,20 12,20 S1,12 1,12 z M12,15 A3,3 0 1 0 12,9 A3,3 0 0 0 12,15 z"/></svg>',
        name: "Auto-Reflection (Pre-Commit Audit)",
        type: "QUALITY MACRO",
        shortcut: "Numpad 4",
        desc: "Instructs the agent to perform an uncompromising self-audit on all modified files prior to finalizing work. Protects against syntax slips, type regressions, and dead references."
      },
      e2e: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#34d399" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 A10,10 0 1 0 22,12 A10,10 0 0 0 12,2 z M10,8 L16,12 L10,16 z"/></svg>',
        name: "E2E & Integration Test Run",
        type: "QUALITY MACRO",
        shortcut: "Numpad 7",
        desc: "Injects a standardized test directive: Launches interactive UI tests inside the AI Co-Browser for web apps, or runs automated suites (dotnet test, npm test, pytest) for backend services."
      },
      deadcode: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f87171" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12.5,17 L12,16 L11.5,17 Z M15,22 a1,1 0 0,0 1,-1 v-1 a2,2 0 0,0 1.56,-3.25 a8,8 0 1,0 -11.12,0 A2,2 0 0,0 8,20 v1 a1,1 0 0,0 1,1 Z M14,12 a1,1 0 1,0 2,0 a1,1 0 1,0 -2,0 M8,12 a1,1 0 1,0 2,0 a1,1 0 1,0 -2,0"/></svg>',
        name: "Dead Code & Cleanup Scan",
        type: "QUALITY MACRO",
        shortcut: "Numpad 8",
        desc: "Directs the agent to scan specifically for unused variables, orphaned methods, unreferenced helper files, and redundant imports to keep the codebase lean."
      },
      errorsearch: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#fbbf24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M11.5 14.5a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M14 17l-1.8-1.8"/></svg>',
        name: "Global Root-Cause Analysis",
        type: "DIAGNOSTIC MACRO",
        shortcut: "Click",
        desc: "Injects an exhaustive diagnostic directive for cryptic compiler faults, stack traces, or silent runtime exceptions, analyzing the call chain from invocation to kernel."
      },
      irrweg: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f97316" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3,19 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M15,5 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M9,19 h8.5 c0.4,0 0.9,-0.1 1.3,-0.2 M5.2,5.2 A3.5,3.53 0 0 0 6.5,12 H12 M2,2 l20,20 M21,15.3 a3.5,3.5 0 0 0 -3.3,-3.3 M15,5 h-4.3"/></svg>',
        name: "Dead-End & Workaround Cleanup",
        type: "CLEANUP MACRO",
        shortcut: "Numpad 9",
        desc: "Instructs the agent to eradicate temporary hacks, stale workarounds, and bloated artifacts from previous debugging attempts once the true root cause is resolved."
      },
      simplify: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4,14 h6 v6 M20,10 h-6 V4 M14,10 l7,-7 M3,21 l7,-7"/></svg>',
        name: "Code Simplify & De-bloat",
        type: "REFACTORING MACRO",
        shortcut: "Click",
        desc: "Commands the agent to radically streamline convoluted nesting, redundant branching, and unnecessary boilerplate code without altering functional semantics."
      },
      zerobug: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#4ade80" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,20 v-9 M14,7 a4,4 0 0 1 4,4 v3 a6,6 0 0 1 -12,0 v-3 a4,4 0 0 1 4,-4 z M14.12,3.88 L16,2 M21,21 a4,4 0 0 0 -3.81,-4 M21,5 a4,4 0 0 1 -3.55,3.97 M22,13 h-4 M3,21 a4,4 0 0 1 3.81,-4 M3,5 a4,4 0 0 0 3.55,3.97 M6,13 H2 M8,2 l1.88,1.88 M9,7.13 V6 a3,3 0 1 1 6,0 v1.13"/></svg>',
        name: "Zero-Bug Quality Offensive",
        type: "AUDIT MACRO",
        shortcut: "Click",
        desc: "Enforces strict defensive programming: non-nullable guarantees, strong type validation, complete exception guards, and boundary checks for production-critical components."
      },
      robustness: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#a855f7" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10,12 a4,4 0 0,0 8,0 a8,8 0 1,0 -16,0 c0,3.584 1.571,6.801 4.063,9 M14,12 a4,4 0 0,0 -8,0 a8,8 0 1,0 16,0 a11.97,11.97 0 0,0 -4,-8.944"/></svg>',
        name: "Chaos & Real-User Stress Test",
        type: "CHAOS AUDIT",
        shortcut: "Click",
        desc: "Spawns a swarm of agent personas simulating impatient end-users, malformed payloads, flaky mobile networks, and rapid UI rage clicks live inside the Co-Browser."
      },
      customprompt: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#c084fc" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 L15.09,8.26 L22,9.27 L17,14.14 L18.18,21.02 L12,17.77 L5.82,21.02 L7,14.14 L2,9.27 L8.91,8.26 Z"/></svg>',
        name: "Custom System Prompts & Roles",
        type: "TEMPLATES & ROLES",
        shortcut: "Click",
        desc: "Opens your curated library of prompt blueprints, engineering personas (e.g. Senior Architect, Penetration Tester, Tech Writer), and custom directives for instant reuse."
      },
      gitstatus: {
        icon: '<svg viewBox="0 0 78 78" width="22" height="22"><path fill="#f43f5e" transform="translate(10 10) rotate(-45 29 29)" d="M5,58c-2.76142,0 -5,-2.23858 -5,-5v-48c0,-2.76142 2.23858,-5 5,-5h33v12.54404c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,0.73514 0.13221,1.43941 0.37415,2.09031l-15.28384,15.28384c-0.6509,-0.24194 -1.35517,-0.37415 -2.09031,-0.37415c-3.31371,0 -6,2.68629 -6,6c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-0.73514 -0.13221,-1.43941 -0.37415,-2.09031l14.87415,-14.87415l0,11.50851c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.08808c2.06553,-0.94801 3.5,-3.03446 3.5,-5.45596c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.54404h10c2.76142,0 5,2.23858 5,5v48c0,2.76142 -2.23858,5 -5,5z"/></svg>',
        name: "Git Repository Live Status",
        type: "GIT TRACKER",
        shortcut: "Click",
        desc: "Displays active branch and uncommitted file count in real time. Click opens the Git Control Center for commit logs, diff views, and rollback checkpoints."
      },
      protocol: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8"/></svg>',
        name: "Project Protocol (docs/protocol.md)",
        type: "DOCUMENTATION",
        shortcut: "Click",
        desc: "Opens the chronological engineering session log. Tracks all code and architectural changes in Markdown with direct transcript references."
      },
      knowledge: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#818cf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77"/></svg>',
        name: "Knowledge Base (docs/knowledge.md)",
        type: "KNOWLEDGE BASE",
        shortcut: "Click",
        desc: "Maintains domain knowledge, architecture boundaries, framework edge cases, and best practices that the agent proactively reviews before each task."
      },
      shield: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#60a5fa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>',
        name: "Auto-Reflection Global Shield",
        type: "SAFETY TOGGLE",
        shortcut: "Click",
        desc: "Enables or disables the dashboard's active security shield. When active, critical file writes strictly enforce a verification gate before changes commit."
      },
      undo: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#94a3b8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/></svg>',
        name: "Prompt History & Undo",
        type: "HISTORY",
        shortcut: "Click",
        desc: "Cycles through recent command prompts and lets you quickly roll back or edit previously dispatched instructions."
      },
      voice: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19v3"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><rect x="9" y="2" width="6" height="13" rx="3"/></svg>',
        name: "1-Click Voice Dictation Mode",
        type: "VOICE DICTATION",
        shortcut: "Click (Start / Stop)",
        desc: "Starts or stops official Antigravity CLI dictation (/voice). Click the microphone, speak your prompt, and click again – transcribed text appears at the cursor."
      },
      send: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z"/><path d="m21.854 2.147-10.94 10.939"/></svg>',
        name: "Submit Prompt to Agent",
        type: "ACTION",
        shortcut: "Enter (or Ctrl+Enter)",
        desc: "Dispatches your prompt together with all attached file contexts, active macros, and parameters to the running ConPTY terminal session."
      },
      cancel: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="14" height="14" x="5" y="5" rx="2" fill="#ef4444"/></svg>',
        name: "Emergency Interrupt (SIGINT)",
        type: "EMERGENCY STOP",
        shortcut: "Ctrl+C",
        desc: "Immediately sends SIGINT (Ctrl+C) to the ConPTY terminal, terminating current generation or running subagents and reclaiming terminal control."
      }
    },
    de: {
      context: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48"/></svg>',
        name: "Dateikontext anfügen",
        type: "DATEI-DIALOG & DROP",
        shortcut: "Klick / Drag & Drop",
        desc: "Öffnet den nativen Windows-Dateidialog, um Quellcode-Dateien, Logfiles, PDFs oder Screenshots direkt als Referenzkontext an den Prompt anzuhängen. Unterstützt auch direktes Hineinziehen per Drag & Drop."
      },
      model: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#818cf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20v2"/><path d="M12 2v2"/><path d="M17 20v2"/><path d="M17 2v2"/><path d="M2 12h2"/><path d="M2 17h2"/><path d="M2 7h2"/><path d="M20 12h2"/><path d="M20 17h2"/><path d="M20 7h2"/><path d="M7 20v2"/><path d="M7 2v2"/><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="8" y="8" width="8" height="8" rx="1"/></svg>',
        name: "Modell-Auswahl & Provider-Switch",
        type: "AUSWAHL-MODAL",
        shortcut: "Strg+M oder Numpad 0",
        desc: "Öffnet den kompakten Schnellwähler für KI-Modelle. Wechsle im laufenden Betrieb zwischen Google Gemini 3.8 Flash, Anthropic Claude Sonnet 5.5, OpenAI GPT-6.1 Sol Pro und DeepSeek V4.1 – inklusive Anzeige gemessener API-Latenzen."
      },
      deepthink: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f59e0b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13,2 L3,14 h9 L11,22 L21,10 h-9 L13,2 z"/></svg>',
        name: "DeepThink Denkmodus (Reasoning)",
        type: "LOGIK-TOGGLE",
        shortcut: "Numpad 1",
        desc: "Schaltet das erweiterte Denkbudget (Extended Thinking / Chain-of-Thought) für komplexe architektonische Aufgaben, Compiler-Debugging und knifflige mathematische Algorithmen ein oder aus."
      },
      docs: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22,19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V5 A2,2 0 0 1 4,3 h5 L11,6 h9 A2,2 0 0 1 22,8 z M2,9 h20"/></svg>',
        name: "Projekt-Dokumentation injizieren",
        type: "PROMPT-MAKRO",
        shortcut: "Numpad 2",
        desc: "Liest automatisch die aktuellen Projektdokumente (docs/knowledge.md, docs/protocol.md, project_rules.json) ein und injiziert sie als strukturierten Systemkontext in den nächsten Prompt."
      },
      subagent: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#a78bfa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 v4 M8,6 h8 M4,10 A2,2 0 0 1 6,8 h12 A2,2 0 0 1 20,10 v8 A2,2 0 0 1 18,20 H6 A2,2 0 0 1 4,18 v-8 z M9,13 h0.01 M15,13 h0.01"/></svg>',
        name: "Subagenten-Schwärme koordinieren",
        type: "SCHWARM-MODAL",
        shortcut: "Numpad 3",
        desc: "Öffnet den Subagenten-Manager zur Initiierung spezialisierter Agenten (z. B. neutraler Code-Auditor, Sicherheits-Prüfer, parallele Rechercheure). Aufgaben werden parallel verteilt, ohne das Hauptkontextfenster zu überlasten."
      },
      cobrowser: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 A10,10 0 1 0 22,12 A10,10 0 0 0 12,2 z M2,12 h20 M12,2 A15.3,15.3 0 0 1 16,12 A15.3,15.3 0 0 1 12,22 A15.3,15.3 0 0 1 8,12 A15.3,15.3 0 0 1 12,2 z"/></svg>',
        name: "KI-Co-Browser Split-Screen",
        type: "SPLIT-SCREEN-TOGGLE",
        shortcut: "Numpad 5",
        desc: "Öffnet oder schließt den integrierten Chromium-basierten WebView2-Browser direkt neben dem Terminal. Ermöglicht visuelle DOM-Prüfung, interaktive Element-Nummerierung und nahtlosen menschlichen Eingriff (Human Takeover)."
      },
      reflexion: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#60a5fa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1,12 S5,4 12,4 S23,12 23,12 S19,20 12,20 S1,12 1,12 z M12,15 A3,3 0 1 0 12,9 A3,3 0 0 0 12,15 z"/></svg>',
        name: "Auto-Reflexion (Code-Prüfung vor Abschluss)",
        type: "QUALITÄTS-MAKRO",
        shortcut: "Numpad 4",
        desc: "Fügt die Anweisung für eine obligatorische Selbstprüfung aller geänderten Dateien vor dem finalen Arbeitsabschluss an. Schützt vor unbemerkten Syntaxfehlern, regressionsbedingten Typenkonflikten und toten Referenzen."
      },
      e2e: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#34d399" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 A10,10 0 1 0 22,12 A10,10 0 0 0 12,2 z M10,8 L16,12 L10,16 z"/></svg>',
        name: "E2E- & Integrations-Testlauf",
        type: "QUALITÄTS-MAKRO",
        shortcut: "Numpad 7",
        desc: "Injiziert ein standardisiertes Test-Makro: Bei Web- und Frontend-Projekten wird zwingend der integrierte Dashboard KI-Co-Browser im Split-Screen geöffnet und interaktiv getestet. Bei Desktop-Apps oder Backend-APIs führt der Agent automatisierte Test-Suites (dotnet test, npm test, pytest) aus."
      },
      deadcode: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f87171" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12.5,17 L12,16 L11.5,17 Z M15,22 a1,1 0 0,0 1,-1 v-1 a2,2 0 0,0 1.56,-3.25 a8,8 0 1,0 -11.12,0 A2,2 0 0,0 8,20 v1 a1,1 0 0,0 1,1 Z M14,12 a1,1 0 1,0 2,0 a1,1 0 1,0 -2,0 M8,12 a1,1 0 1,0 2,0 a1,1 0 1,0 -2,0"/></svg>',
        name: "Dead-Code- & Bereinigungs-Scan",
        type: "QUALITÄTS-MAKRO",
        shortcut: "Numpad 8",
        desc: "Beauftragt den Agenten mit der gezielten Suche nach ungenutzten Variablen, verwaisten Methoden, unreferenzierten Hilfsdateien und toten Imports zur Verschlankung des Repositories."
      },
      errorsearch: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#fbbf24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M11.5 14.5a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M14 17l-1.8-1.8"/></svg>',
        name: "Globale Fehlersuche (Root-Cause-Analyse)",
        type: "ANALYSE-MAKRO",
        shortcut: "Klick",
        desc: "Injiziert ein tiefgreifendes Diagnose-Makro für unklare Compiler-Fehler, Stacktraces oder Laufzeitabstürze. Der Agent analysiert die Aufrufkette vom Auslöser bis zum Kernel."
      },
      irrweg: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#f97316" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3,19 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M15,5 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M9,19 h8.5 c0.4,0 0.9,-0.1 1.3,-0.2 M5.2,5.2 A3.5,3.53 0 0 0 6.5,12 H12 M2,2 l20,20 M21,15.3 a3.5,3.5 0 0 0 -3.3,-3.3 M15,5 h-4.3"/></svg>',
        name: "Irrweg- & Workaround-Cleanup",
        type: "CLEANUP-MAKRO",
        shortcut: "Numpad 9",
        desc: "Weist den Agenten an, obsolete temporäre Fixes, ungenutzte Workarounds und aufgeblähte Codeteile vorheriger Lösungsversuche gezielt aufzuräumen, sobald die eigentliche Fehlerursache ermittelt und behoben ist."
      },
      simplify: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4,14 h6 v6 M20,10 h-6 V4 M14,10 l7,-7 M3,21 l7,-7"/></svg>',
        name: "Code Simplify & Entschlackung",
        type: "REFACTORING-MAKRO",
        shortcut: "Klick",
        desc: "Weist den Agenten an, überkomplizierte Verschachtelungen, redundante Abfragen und überflüssigen Boilerplate-Code radikal zu vereinfachen, ohne die bestehende Funktionalität zu verändern."
      },
      zerobug: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#4ade80" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,20 v-9 M14,7 a4,4 0 0 1 4,4 v3 a6,6 0 0 1 -12,0 v-3 a4,4 0 0 1 4,-4 z M14.12,3.88 L16,2 M21,21 a4,4 0 0 0 -3.81,-4 M21,5 a4,4 0 0 1 -3.55,3.97 M22,13 h-4 M3,21 a4,4 0 0 1 3.81,-4 M3,5 a4,4 0 0 0 3.55,3.97 M6,13 H2 M8,2 l1.88,1.88 M9,7.13 V6 a3,3 0 1 1 6,0 v1.13"/></svg>',
        name: "Zero-Bug Qualitätsoffensive",
        type: "AUDIT-MAKRO",
        shortcut: "Klick",
        desc: "Aktiviert strenge Defensiv-Programmierung mit strikter Null-Pointer-Prüfung, Typsicherheit, vollständiger Exception-Behandlung und Randfall-Abdeckung für produktionskritische Komponenten."
      },
      robustness: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#a855f7" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10,12 a4,4 0 0,0 8,0 a8,8 0 1,0 -16,0 c0,3.584 1.571,6.801 4.063,9 M14,12 a4,4 0 0,0 -8,0 a8,8 0 1,0 16,0 a11.97,11.97 0 0,0 -4,-8.944"/></svg>',
        name: "DAU- & Real-User Chaos-Test",
        type: "CHAOS-AUDIT",
        shortcut: "Klick",
        desc: "Startet einen Multi-Agenten-Schwarm zur Simulation diverser Nutzer-Archetypen (B2C-Kunden, Senioren, Azubis, mobile Netze, RTL-Zeichensätze). Testet Formulare mit fehlerhaften Payloads und Rage-Clicks live im Co-Browser."
      },
      customprompt: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#c084fc" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12,2 L15.09,8.26 L22,9.27 L17,14.14 L18.18,21.02 L12,17.77 L5.82,21.02 L7,14.14 L2,9.27 L8.91,8.26 Z"/></svg>',
        name: "Eigene System-Prompts & Rollen",
        type: "TEMPLATES & ROLLEN",
        shortcut: "Klick",
        desc: "Öffnet deine gespeicherten Prompt-Templates, Coding-Rollen (z. B. Senior Architect, Penetration Tester, Doc Writer) und benutzerdefinierten Direktiven zur sofortigen Wiederverwendung."
      },
      gitstatus: {
        icon: '<svg viewBox="0 0 78 78" width="22" height="22"><path fill="#f43f5e" transform="translate(10 10) rotate(-45 29 29)" d="M5,58c-2.76142,0 -5,-2.23858 -5,-5v-48c0,-2.76142 2.23858,-5 5,-5h33v12.54404c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,0.73514 0.13221,1.43941 0.37415,2.09031l-15.28384,15.28384c-0.6509,-0.24194 -1.35517,-0.37415 -2.09031,-0.37415c-3.31371,0 -6,2.68629 -6,6c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-0.73514 -0.13221,-1.43941 -0.37415,-2.09031l14.87415,-14.87415l0,11.50851c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.08808c2.06553,-0.94801 3.5,-3.03446 3.5,-5.45596c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.54404h10c2.76142,0 5,2.23858 5,5v48c0,2.76142 -2.23858,5 -5,5z"/></svg>',
        name: "Git Repository Live-Status",
        type: "GIT-TRACKER",
        shortcut: "Klick",
        desc: "Zeigt den aktuellen Git-Branch und die Anzahl ungesicherter Änderungen live an. Ein Klick öffnet die Git-Zentrale zur Anzeige von Commit-Historie, Dateiänderungen und Rollback-Optionen."
      },
      protocol: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8"/></svg>',
        name: "Projekt-Protokoll (docs/protocol.md)",
        type: "DOKUMENTATION",
        shortcut: "Klick",
        desc: "Öffnet das lückenlose Sitzungsprotokoll des Projekts. Zeigt alle durchgeführten Code- und Konfigurationsänderungen chronologisch im Markdown-Format an."
      },
      knowledge: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#818cf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77"/></svg>',
        name: "Wissensdatenbank (docs/knowledge.md)",
        type: "KNOWLEDGE-BASE",
        shortcut: "Klick",
        desc: "Verwaltet projektspezifisches Wissen, Architekturrichtlinien, Framework-Eigenheiten und dokumentierte Best Practices, die der Agent automatisch vor jeder Aufgabe berücksichtigt."
      },
      shield: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#60a5fa" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>',
        name: "Auto-Reflexion Global Toggle",
        type: "SCHUTZ-TOGGLE",
        shortcut: "Klick",
        desc: "Aktiviert oder deaktiviert den automatischen Sicherheits-Schild im Dashboard. Bei aktivem Schild fordert das System bei kritischen Dateioperationen automatisch ein Review an."
      },
      undo: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#94a3b8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/></svg>',
        name: "Prompt-Verlauf & Undo",
        type: "HISTORIE",
        shortcut: "Klick",
        desc: "Blättert durch die Historie der zuletzt abgeschickten Eingaben und erlaubt das schnelle Wiederherstellen oder Korrigieren vorheriger Prompts."
      },
      voice: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19v3"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><rect x="9" y="2" width="6" height="13" rx="3"/></svg>',
        name: "1-Klick Spracheingabe (Diktatmodus)",
        type: "VOICE-DIKTAT",
        shortcut: "Klick (Start / Stopp)",
        desc: "Startet oder stoppt den offiziellen Spracheingabe-Diktatmodus der Antigravity CLI (/voice). Klicke auf das Mikrofon, sprich deine Anweisung ein und klicke erneut – der transkribierte Text landet direkt am Cursor."
      },
      send: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z"/><path d="m21.854 2.147-10.94 10.939"/></svg>',
        name: "Prompt an Agenten absenden",
        type: "AKTION",
        shortcut: "Enter (oder Strg+Enter)",
        desc: "Übermittelt deine Eingabe mitsamt allen angehängten Kontextdateien, aktiven Makros und Parametern an die laufende CLI-Instanz im ConPTY-Terminal."
      },
      cancel: {
        icon: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="14" height="14" x="5" y="5" rx="2" fill="#ef4444"/></svg>',
        name: "Laufenden Vorgang abbrechen",
        type: "NOT-HALT",
        shortcut: "Strg+C",
        desc: "Sendet sofort ein SIGINT (Strg+C) an das ConPTY-Terminal, stoppt die laufende KI-Generierung oder den Hintergrundprozess und gibt das Terminal wieder frei."
      }
    }
  };

  function getLanguage() {
    return currentLang;
  }

  function setLanguage(lang) {
    if (!translations[lang]) lang = 'en';
    currentLang = lang;
    localStorage.setItem(STORAGE_KEY, lang);
    document.documentElement.lang = lang;

    // Apply document metadata
    if (translations[lang].page_title) {
      document.title = translations[lang].page_title;
    }
    const metaDesc = document.querySelector('meta[name="description"]');
    if (metaDesc && translations[lang].page_meta_desc) {
      metaDesc.setAttribute("content", translations[lang].page_meta_desc);
    }

    // Apply all elements with data-i18n
    const elements = document.querySelectorAll("[data-i18n]");
    elements.forEach(el => {
      const key = el.getAttribute("data-i18n");
      const val = translations[lang][key];
      if (val !== undefined) {
        if (val.includes("<") || val.includes("&")) {
          el.innerHTML = val;
        } else {
          el.textContent = val;
        }
      }
    });

    // Apply all elements with data-i18n-title
    const titleElements = document.querySelectorAll("[data-i18n-title]");
    titleElements.forEach(el => {
      const key = el.getAttribute("data-i18n-title");
      const val = translations[lang][key];
      if (val !== undefined) {
        el.setAttribute("title", val);
      }
    });

    // Update switcher buttons active state
    document.querySelectorAll(".lang-btn").forEach(btn => {
      if (btn.getAttribute("data-lang") === lang) {
        btn.classList.add("active");
      } else {
        btn.classList.remove("active");
      }
    });

    // Notify listeners
    listeners.forEach(cb => {
      try { cb(lang); } catch (e) { console.error("i18n listener error:", e); }
    });
  }

  function onLanguageChange(cb) {
    if (typeof cb === 'function') listeners.push(cb);
  }

  function getToolbarItem(id) {
    const dict = toolbarData[currentLang] || toolbarData.en;
    return dict[id] || null;
  }

  function init() {
    const saved = localStorage.getItem(STORAGE_KEY) || 'en';
    setLanguage(saved);

    // Bind click events on language toggle buttons
    document.querySelectorAll(".lang-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        const lang = btn.getAttribute("data-lang");
        if (lang) setLanguage(lang);
      });
    });
  }

  return {
    init,
    getLanguage,
    setLanguage,
    onLanguageChange,
    getToolbarItem,
    translations,
    toolbarData
  };
})();

// Auto-initialize when DOM is ready
if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", () => AgyI18n.init());
} else {
  AgyI18n.init();
}
