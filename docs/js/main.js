// AGY CLI Dashboard - Showcase Landing Page JavaScript

document.addEventListener("DOMContentLoaded", () => {
  // 1. Mockup Tab Switcher
  const tabs = document.querySelectorAll(".mockup-tab");
  const contents = document.querySelectorAll(".mockup-content");

  tabs.forEach(tab => {
    tab.addEventListener("click", () => {
      const targetId = tab.getAttribute("data-tab");

      tabs.forEach(t => t.classList.remove("active"));
      contents.forEach(c => c.classList.remove("active"));

      tab.classList.add("active");
      const targetContent = document.getElementById(targetId);
      if (targetContent) {
        targetContent.classList.add("active");
      }
    });
  });

  // 2. Robust Copy Code to Clipboard (with execCommand fallback)
  function copyTextToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
    }
    return fallbackCopy(text);
  }

  function fallbackCopy(text) {
    return new Promise((resolve, reject) => {
      const textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      textarea.style.top = "0";
      textarea.setAttribute("readonly", "");
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();
      try {
        const successful = document.execCommand("copy");
        document.body.removeChild(textarea);
        if (successful) resolve();
        else reject(new Error("execCommand copy failed"));
      } catch (err) {
        document.body.removeChild(textarea);
        reject(err);
      }
    });
  }

  const copyButtons = document.querySelectorAll(".btn-copy");
  copyButtons.forEach(btn => {
    btn.addEventListener("click", () => {
      const textToCopy = btn.getAttribute("data-copy") || "git clone https://github.com/1YellowMellow1/agy-cli-dashboard-public.git";
      copyTextToClipboard(textToCopy).then(() => {
        const originalText = btn.innerHTML;
        btn.innerHTML = `
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          Kopiert
        `;
        btn.style.color = "#10b981";
        btn.style.borderColor = "rgba(16, 185, 129, 0.4)";

        setTimeout(() => {
          btn.innerHTML = originalText;
          btn.style.color = "";
          btn.style.borderColor = "";
        }, 2200);
      }).catch(err => {
        console.warn("Clipboard copy could not be completed:", err);
      });
    });
  });

  // 3. FAQ Accordion Toggle
  const faqQuestions = document.querySelectorAll(".faq-question");
  faqQuestions.forEach(btn => {
    btn.addEventListener("click", () => {
      const item = btn.closest(".faq-item");
      const isOpen = item.classList.contains("active");

      // Close all other items
      document.querySelectorAll(".faq-item").forEach(i => i.classList.remove("active"));

      if (!isOpen) {
        item.classList.add("active");
      }
    });
  });

  // 4. Interactive Cockpit Toolbar Guide
  const toolbarData = {
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
      desc: "Injiziert ein standardisiertes Test-Makro, das den Agenten anweist, nach Codeänderungen sofort die automatisierten Test-Suites (dotnet test, npm test, pytest) auszuführen und Testergebnisse zu validieren."
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
      name: "Irrweg-Cleanup & Git-Rollback",
      type: "ROLLBACK-MAKRO",
      shortcut: "Numpad 9",
      desc: "Setzt fehlgeschlagene Code-Experimente und verirrte Agenten-Versuche sauber auf den letzten funktionierenden Git-Commit zurück, räumt temporäre Testdateien ab und stellt den Ausgangszustand wieder her."
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
      name: "Systemweites Robustheits-Audit",
      type: "AUDIT-MAKRO",
      shortcut: "Klick",
      desc: "Umfassende Belastungsprüfung für Concurrency, Thread-Sicherheit, Ressourcen-Freigabe (IDisposable), Timeout-Handling und asynchrone Race Conditions im gesamten Projekt."
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
  };

  const tbButtons = document.querySelectorAll(".tb-btn");
  const tbIcon = document.getElementById("tb-icon");
  const tbName = document.getElementById("tb-name");
  const tbType = document.getElementById("tb-type");
  const tbShortcut = document.getElementById("tb-shortcut");
  const tbDesc = document.getElementById("tb-desc");

  function selectToolbarItem(id) {
    const item = toolbarData[id];
    if (!item) return;

    tbButtons.forEach(btn => {
      if (btn.getAttribute("data-id") === id) {
        btn.classList.add("active");
      } else {
        btn.classList.remove("active");
      }
    });

    if (tbIcon) tbIcon.innerHTML = item.icon;
    if (tbName) tbName.textContent = item.name;
    if (tbType) tbType.textContent = item.type;
    if (tbShortcut) tbShortcut.textContent = item.shortcut;
    if (tbDesc) tbDesc.textContent = item.desc;
  }

  tbButtons.forEach(btn => {
    btn.addEventListener("click", () => {
      const id = btn.getAttribute("data-id");
      if (id) selectToolbarItem(id);
    });
  });

  // Progressive Disclosure: Feature Matrix Toggle
  const matrixContainer = document.getElementById("feature-matrix");
  const btnToggleMatrix = document.getElementById("btn-toggle-matrix");
  const matrixToggleText = document.getElementById("matrix-toggle-text");
  const matrixToggleCount = document.getElementById("matrix-toggle-count");
  const matrixToggleIcon = document.getElementById("matrix-toggle-icon");

  if (matrixContainer && btnToggleMatrix) {
    btnToggleMatrix.addEventListener("click", () => {
      const isCollapsed = matrixContainer.classList.contains("is-collapsed");
      if (isCollapsed) {
        matrixContainer.classList.remove("is-collapsed");
        if (matrixToggleText) matrixToggleText.textContent = "Auf Kern-Highlights minimieren";
        if (matrixToggleCount) matrixToggleCount.textContent = "Kompakt";
        if (matrixToggleIcon) matrixToggleIcon.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="18 15 12 9 6 15"/></svg>`;
      } else {
        matrixContainer.classList.add("is-collapsed");
        if (matrixToggleText) matrixToggleText.textContent = "Alle 24 Kriterien & Detailvergleiche ausklappen";
        if (matrixToggleCount) matrixToggleCount.textContent = "+17 Kriterien";
        if (matrixToggleIcon) matrixToggleIcon.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>`;

        // Sanftes Zurückscrollen zum Matrix-Start
        const matrixTop = matrixContainer.getBoundingClientRect().top + window.pageYOffset - 90;
        window.scrollTo({ top: matrixTop, behavior: "smooth" });
      }
    });
  }
});


