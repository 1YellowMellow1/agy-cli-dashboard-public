#!/usr/bin/env node
/**
 * scripts/co-browser-mcp.js
 * High-Performance Model Context Protocol (MCP) Server für den Antigravity KI-Co-Browser.
 * Kommuniziert über Stdio (JSON-RPC 2.0) direkt mit der Antigravity CLI und leitet
 * Tool-Calls latenzfrei an den In-Process HTTP-RPC Server (Port 9223) des Dashboards weiter.
 */

const fs = require('fs');
const path = require('path');
const readline = require('readline');

// 1. Port-Discovery aus AppData
let cachedEndpoint = { baseUrl: 'http://127.0.0.1:9223', token: '' };

function getEndpointInfo(forceFresh = false) {
  if (!forceFresh && cachedEndpoint.token && cachedEndpoint.baseUrl !== 'http://127.0.0.1:9223') {
    return cachedEndpoint;
  }
  const localAppData = process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || '', 'AppData', 'Local');
  const candidates = [
    path.join(localAppData, 'AgyCliDashboard', 'co_browser_endpoint.json'),
    path.join(localAppData, 'AgyCliDashboard-Public', 'co_browser_endpoint.json')
  ].filter(f => fs.existsSync(f))
   .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);

  const endpointFile = candidates[0];
  if (endpointFile) {
    try {
      const data = JSON.parse(fs.readFileSync(endpointFile, 'utf8'));
      if (data && data.apiPort) {
        cachedEndpoint = {
          baseUrl: `http://127.0.0.1:${data.apiPort}`,
          token: data.authToken || cachedEndpoint.token || ''
        };
        return cachedEndpoint;
      }
    } catch {}
  }
  return cachedEndpoint;
}

function toBool(val, defaultVal = false) {
  if (val === undefined || val === null) return defaultVal;
  if (typeof val === 'boolean') return val;
  if (typeof val === 'number') return val !== 0;
  if (typeof val === 'string') {
    const s = val.trim().toLowerCase();
    if (s === 'true' || s === '1' || s === 'yes') return true;
    if (s === 'false' || s === '0' || s === 'no') return false;
  }
  return Boolean(val);
}

function toNum(val, defaultVal = 0) {
  if (val === undefined || val === null) return defaultVal;
  if (typeof val === 'number') return Number.isNaN(val) ? defaultVal : val;
  const n = Number(val);
  return Number.isNaN(n) ? defaultVal : n;
}

let lastInteractiveElements = [];

async function apiRequest(endpoint, method = 'GET', body = null, timeoutMs = 15000) {
  let { baseUrl, token } = getEndpointInfo();
  let url = `${baseUrl}${endpoint}`;
  let headers = { 'Content-Type': 'application/json' };
  if (token) {
    headers['X-Agy-Token'] = token;
  }
  const convId = (process.env.ANTIGRAVITY_CONVERSATION_ID || '').trim();
  if (convId) {
    headers['X-Conversation-Id'] = convId;
  }
  if ((body && (body.force === true || body.force === 'true')) || endpoint.includes('force=true')) {
    headers['X-Agy-Force'] = 'true';
  }
  const init = {
    method,
    headers,
    signal: AbortSignal.timeout(timeoutMs)
  };
  if (body) {
    init.body = JSON.stringify(body);
  }

  try {
    let res = await fetch(url, init);
    // Bei 401 Unauthorized einmaligen Re-Discovery Retry mit aktuellem Token durchführen
    if (res.status === 401) {
      await new Promise(r => setTimeout(r, 100));
      const fresh = getEndpointInfo(true);
      if (fresh.token && fresh.token !== token) {
        headers['X-Agy-Token'] = fresh.token;
        url = `${fresh.baseUrl}${endpoint}`;
        const retryInit = { ...init, headers, signal: AbortSignal.timeout(timeoutMs) };
        res = await fetch(url, retryInit);
      }
    }
    const data = await res.json();
    return data;
  } catch (err) {
    // Retry bei Verbindungsfehler nach Re-Discovery mit frischem AbortSignal
    try {
      await new Promise(r => setTimeout(r, 150));
      const fresh = getEndpointInfo(true);
      if (fresh.baseUrl !== baseUrl || (fresh.token && fresh.token !== token)) {
        url = `${fresh.baseUrl}${endpoint}`;
        if (fresh.token) headers['X-Agy-Token'] = fresh.token;
        const retryInit = { ...init, headers, signal: AbortSignal.timeout(timeoutMs) };
        const retryRes = await fetch(url, retryInit);
        return await retryRes.json();
      }
    } catch {}

    return {
      success: false,
      error: `Verbindung zum Co-Browser fehlgeschlagen (${url}): ${err.message}. Ist das Dashboard geöffnet?`
    };
  }
}

// 2. Tool-Definitionen
const TOOLS = [
  {
    name: 'cobrowser_status',
    description: 'Fragt den aktuellen Status des KI-Co-Browsers ab (URL, Titel, Ports, Takeover-Status, Initialisierung). Unterstützt optional die Einbettung von Diagnose- und Latenz-Statistiken.',
    inputSchema: {
      type: 'object',
      properties: {
        diagnostics: {
          type: 'boolean',
          description: 'Wenn true, werden zusätzlich Flight-Recorder-Metriken (Latenzen, Fehlerquoten, DOM-Fingerprint) mitgeliefert.'
        }
      }
    }
  },
  {
    name: 'cobrowser_diagnostics',
    description: 'Liefert den detaillierten Flight-Recorder (Ringpuffer der letzten Aktionen mit Latenzen), Latenz-Ausreißer (>1000ms), DOM-Framework-Fingerprint und aufgetretene Fehler für das automatisierte Co-Browser-Audit.',
    inputSchema: {
      type: 'object',
      properties: {
        limit: {
          type: 'number',
          description: 'Maximale Anzahl der Historien-Einträge (Standard: 20).'
        },
        slowOnly: {
          type: 'boolean',
          description: 'Wenn true, werden nur Aktionen mit Fehlern oder Latenzen >= 1000ms geliefert.'
        },
        reset: {
          type: 'boolean',
          description: 'Wenn true, wird das gesammelte Flight-Log nach der Auswertung auf 0 zurückgesetzt (rotiert nach .previous.jsonl) für den nächsten Audit-Zyklus.'
        }
      }
    }
  },
  {
    name: 'cobrowser_interactive_map',
    description: 'Erstellt eine semantische DOM-Element-Map mit numerischen IDs (#1, #2, ...), Typen und Beschreibungen. Spart >95% Tokens gegenüber Screenshots. Unterstützt visuelle Badges auf dem Bildschirm.',
    inputSchema: {
      type: 'object',
      properties: {
        overlay: {
          type: 'boolean',
          description: 'Wenn true, werden cyanfarbene Nummern-Badges [1], [2] direkt auf dem Browser-Bildschirm eingeblendet.'
        },
        scanAll: {
          type: 'boolean',
          description: 'Wenn true, scrollt der Browser die Seite stufenweise nach unten, um dynamisch nachladende Elemente (Lazy-Loading) zu erfassen.'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt Scan-All auch wenn menschlicher Takeover im Browser aktiv ist.'
        }
      }
    }
  },
  {
    name: 'cobrowser_read_content',
    description: 'Liest den bereinigten Haupttext der aktuellen Webseite im Co-Browser. Filtert Menüs, Banner und Werbung automatisch heraus. Wandelt Datentabellen 100% terminal-freundlich in vertikale Key-Value-Karten um (ohne zerschießende ASCII-Tabellen).',
    inputSchema: {
      type: 'object',
      properties: {
        selector: {
          type: 'string',
          description: 'Optionaler CSS-Selektor des gewünschten Inhaltsbereichs (z. B. "article", "main", "#content"). Standardmäßig wird der Hauptbereich automatisch erkannt.'
        },
        clean: {
          type: 'boolean',
          description: 'Wenn true (Standard), werden Navigationsleisten, Header, Footer, Cookie-Banner und Werbeblöcke automatisch weggeschnitten.'
        },
        maxLength: {
          type: 'number',
          description: 'Maximale Zeichenanzahl des Textes (Standard: 60000).'
        }
      }
    }
  },
  {
    name: 'cobrowser_network',
    description: 'Liest abgefangene HTTP-, XHR- und Fetch-Netzwerkanfragen (insb. JSON-APIs) des Co-Browsers aus. Erlaubt der KI das direkte Extrahieren von Server-Rohdaten ohne DOM-Scraping.',
    inputSchema: {
      type: 'object',
      properties: {
        type: {
          type: 'string',
          description: 'Filter nach Ressourcentyp (z. B. "xhr,fetch", "json", "all"). Standard: "xhr,fetch,json".'
        },
        search: {
          type: 'string',
          description: 'Optionaler Suchbegriff zum Filtern der URL (z. B. "api/v1", "graphql").'
        },
        includeBody: {
          type: 'boolean',
          description: 'Wenn true, wird der Antwortinhalt (z. B. JSON-Payload) für die Treffer direkt mitgeliefert.'
        },
        requestId: {
          type: 'string',
          description: 'Optionale spezifische Request-ID, um Details und Body einer einzelnen Anfrage abzurufen.'
        },
        limit: {
          type: 'number',
          description: 'Maximale Anzahl zurückzugebender Einträge (Standard: 25, max: 100).'
        },
        clear: {
          type: 'boolean',
          description: 'Wenn true, wird der bisherige Netzwerk-Puffer geleert.'
        }
      }
    }
  },
  {
    name: 'cobrowser_console',
    description: 'Liest abgefangene Browser-Konsolenmeldungen (console.log, console.warn, console.error, unhandled exceptions) des Co-Browsers aus. Ideal für Live-Audits, Smoke-Tests und Fehlerdiagnosen.',
    inputSchema: {
      type: 'object',
      properties: {
        type: {
          type: 'string',
          description: 'Filter nach Meldungstyp (z. B. "error", "warning", "info", "log", "exception", "all"). Standard: "all".'
        },
        search: {
          type: 'string',
          description: 'Optionaler Suchbegriff zum Filtern der Konsolentexte oder Script-URLs.'
        },
        limit: {
          type: 'number',
          description: 'Maximale Anzahl zurückzugebender Einträge (Standard: 50, max: 200).'
        },
        clear: {
          type: 'boolean',
          description: 'Wenn true, wird der bisherige Konsolen-Puffer geleert.'
        },
        currentUrlOnly: {
          type: 'boolean',
          description: 'Wenn true (Standard), werden nur Konsolenmeldungen der aktuellen Domain/URL zurückgegeben (verhindert veraltete Logs vorangegangener Seiten).'
        }
      }
    }
  },
  {
    name: 'cobrowser_eval',
    description: 'Führt einen JavaScript-Ausdruck direkt im Kontext der geladenen Seite im Co-Browser aus (CDP Runtime.evaluate) und gibt das Ergebnis zurück.',
    inputSchema: {
      type: 'object',
      properties: {
        script: {
          type: 'string',
          description: 'Das auszuführende JavaScript-Snippet (z. B. "document.title", "window.location.href").'
        },
        expression: {
          type: 'string',
          description: 'Alternative Bezeichnung für script (kompatibel mit CDP Runtime.evaluate).'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt die Script-Ausführung auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      }
    }
  },
  {
    name: 'cobrowser_axtree',
    description: 'Ruft den semantischen Barrierefreiheits-Baum (Accessibility Tree) der aktuellen Webseite im Co-Browser ab. Liefert exakte UI-Rollen (button, combobox, textbox) samt Namen, Werten und Zuständen (disabled, expanded) ohne störendes CSS-Layout-Rauschen.',
    inputSchema: {
      type: 'object',
      properties: {
        interactiveOnly: {
          type: 'boolean',
          description: 'Wenn true (Standard), werden nur semantisch interaktive Knoten (Buttons, Eingabefelder, Menüs, Links) ausgegeben.'
        },
        maxDepth: {
          type: 'number',
          description: 'Maximale Verschachtelungstiefe des Baums (Standard: 10).'
        }
      }
    }
  },
  {
    name: 'cobrowser_overlay',
    description: 'Blendet die cyanfarbenen Nummern-Badges auf dem Browser-Bildschirm ein oder aus.',
    inputSchema: {
      type: 'object',
      properties: {
        show: {
          type: 'boolean',
          description: 'true = Badges anzeigen, false = Badges ausblenden.'
        }
      },
      required: ['show']
    }
  },
  {
    name: 'cobrowser_click',
    description: 'Klickt ein Element im Co-Browser via numerischer ID oder Selektor mit nativem Click-Dispatching und Auto-Waiting.',
    inputSchema: {
      type: 'object',
      properties: {
        id: {
          type: 'number',
          description: 'Die numerische Element-ID aus der interactive-map (z. B. 1, 2, 14).'
        },
        elementId: {
          type: 'number',
          description: 'Alias für id: Die numerische Element-ID aus der interactive-map (z. B. 1, 2, 14).'
        },
        selector: {
          type: 'string',
          description: 'Optionaler CSS-Selektor (z. B. "#submit-btn", "button.save").'
        },
        text: {
          type: 'string',
          description: 'Optionaler Textinhalt des Buttons/Links (z. B. "Weiter", "Absenden").'
        },
        timeoutMs: {
          type: 'number',
          description: 'Maximales Timeout für das Erscheinen des Elements in Millisekunden (Standard: 5000).'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt den Klick auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      }
    }
  },
  {
    name: 'cobrowser_type',
    description: 'Tippt Text mit echten Chromium-OS-Tastenanschlägen (CDP Input.insertText) ein. Funktioniert zuverlässig in React 18/19, Vue, Monaco, Rich-Text und Autocomplete-Dropdowns.',
    inputSchema: {
      type: 'object',
      properties: {
        id: {
          type: 'number',
          description: 'Die numerische Element-ID aus der interactive-map.'
        },
        elementId: {
          type: 'number',
          description: 'Alias für id: Die numerische Element-ID aus der interactive-map.'
        },
        selector: {
          type: 'string',
          description: 'Optionaler CSS-Selektor des Input- oder Textarea-Felds.'
        },
        text: {
          type: 'string',
          description: 'Der einzugebende Text.'
        },
        clear: {
          type: 'boolean',
          description: 'Wenn true (Standard), wird das Feld vor der Eingabe via Ctrl+A + Backspace geleert.'
        },
        pressEnter: {
          type: 'boolean',
          description: 'Wenn true, wird nach der Eingabe die Enter-Taste gedrückt.'
        },
        timeoutMs: {
          type: 'number',
          description: 'Maximales Timeout in Millisekunden (Standard: 5000).'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt die Texteingabe auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      },
      required: ['text']
    }
  },
  {
    name: 'cobrowser_fill_form',
    description: 'Befüllt mehrere Formularfelder in einem einzigen schnellen Batch-Aufruf und kann das Formular direkt absenden.',
    inputSchema: {
      type: 'object',
      properties: {
        fields: {
          type: 'object',
          description: 'Key-Value Mapping von Selektoren/IDs zu Werten, z. B. {"#name": "Max Mustermann", "#city": "Berlin", "#agb": true}.'
        },
        submit: {
          type: 'string',
          description: 'Optionaler Selektor des Submit-Buttons, der nach dem Ausfüllen geklickt werden soll.'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt das Befüllen auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      },
      required: ['fields']
    }
  },
  {
    name: 'cobrowser_scroll',
    description: 'Scrollt die Webseite sanft nach oben/unten oder fokussiert ein bestimmtes Element.',
    inputSchema: {
      type: 'object',
      properties: {
        direction: {
          type: 'string',
          enum: ['down', 'up', 'top', 'bottom'],
          description: 'Scroll-Richtung ("down", "up", "top", "bottom"). Standard: "down".'
        },
        pixels: {
          type: 'number',
          description: 'Pixelanzahl beim Scrollen (Standard: 600).'
        },
        y: {
          type: 'number',
          description: 'Absolute vertikale Scroll-Position in Pixeln (z. B. 0 für Seitenanfang, 450 für Abschnitt). Überschreibt direction/pixels, falls angegeben.'
        },
        selector: {
          type: 'string',
          description: 'Optionaler Selektor eines Elements, zu dem sanft gescrollt werden soll.'
        },
        id: {
          type: 'number',
          description: 'Optionale Element-ID aus der interactive-map, zu der gescrollt werden soll.'
        },
        elementId: {
          type: 'number',
          description: 'Alias für id: Optionale Element-ID aus der interactive-map.'
        },
        offsetY: {
          type: 'number',
          description: 'Optionaler vertikaler Offset in Pixeln beim Scrollen zu einem Element (z. B. -60 für fixierte Header).'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt das Scrollen auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      }
    }
  },
  {
    name: 'cobrowser_upload',
    description: 'Lädt eine lokale Datei headless in ein <input type="file"> hoch (via CDP DOM.setFileInputFiles), ohne dass ein blockierender Windows-Dateidialog aufpoppt.',
    inputSchema: {
      type: 'object',
      properties: {
        filePath: {
          type: 'string',
          description: 'Absoluter lokaler Dateipfad (z. B. "C:/Users/.../dokument.pdf").'
        },
        selector: {
          type: 'string',
          description: 'CSS-Selektor des File-Input-Elements (Standard: "input[type=\'file\']").'
        },
        id: {
          type: 'number',
          description: 'Optionale Element-ID des File-Inputs aus der interactive-map.'
        },
        elementId: {
          type: 'number',
          description: 'Alias für id: Optionale Element-ID des File-Inputs aus der interactive-map.'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt den Datei-Upload auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      },
      required: ['filePath']
    }
  },
  {
    name: 'cobrowser_open',
    description: 'Öffnet das Split-Screen-Fenster des KI-Co-Browsers im Dashboard für den Benutzer sichtbar (optional mit Navigation zu einer URL).',
    inputSchema: {
      type: 'object',
      properties: {
        url: {
          type: 'string',
          description: 'Optionale Ziel-URL, die beim Öffnen direkt geladen werden soll.'
        }
      }
    }
  },
  {
    name: 'cobrowser_close',
    description: 'Schließt bzw. klappt das Split-Screen-Fenster des KI-Co-Browsers im Dashboard ein.',
    inputSchema: {
      type: 'object',
      properties: {}
    }
  },
  {
    name: 'cobrowser_navigate',
    description: 'Navigiert zu einer URL oder führt Browser-Verlaufsaktionen aus (reload, back, forward). Öffnet das Split-Screen-Fenster im Dashboard standardmäßig automatisch.',
    inputSchema: {
      type: 'object',
      properties: {
        url: {
          type: 'string',
          description: 'Ziel-URL (z. B. "https://google.com").'
        },
        action: {
          type: 'string',
          enum: ['reload', 'back', 'forward'],
          description: 'Navigationsaktion ("reload", "back", "forward").'
        },
        open: {
          type: 'boolean',
          description: 'Wenn true (Standard), wird das Split-Screen-Fenster im Dashboard automatisch für den Benutzer geöffnet.'
        },
        waitForLoad: {
          type: 'boolean',
          description: 'Wenn true (Standard), wartet der Befehl, bis die Zielseite vollständig geladen ist (NavigationCompleted). Setze auf false für schnellen asynchronen Aufruf.'
        },
        timeoutMs: {
          type: 'number',
          description: 'Maximales Lade-Timeout in Millisekunden (Standard: 15000).'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt die Navigation auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      }
    }
  },
  {
    name: 'cobrowser_screenshot',
    description: 'Erfasst einen visuellen Screenshot der aktuellen Webseite im Co-Browser.',
    inputSchema: {
      type: 'object',
      properties: {
        outPath: {
          type: 'string',
          description: 'Optionaler lokaler Speicherpfad für die PNG-Datei.'
        }
      }
    }
  },
  {
    name: 'cobrowser_handoff',
    description: 'Übergibt die Kontrolle an den Menschen (z. B. für Captcha, 2FA, Bezahlung) oder signalisiert Bereitschaft.',
    inputSchema: {
      type: 'object',
      properties: {
        action: {
          type: 'string',
          enum: ['waiting_for_user', 'ready', 'controlling'],
          description: 'Aktion: "waiting_for_user" (Mensch ist dran), "ready" (Bereit), "controlling" (KI steuert).'
        },
        reason: {
          type: 'string',
          description: 'Begründung für den Benutzer (z. B. "Bitte 2FA auf dem Smartphone bestätigen").'
        },
        force: {
          type: 'boolean',
          description: 'Erzwingt den Statuswechsel auch wenn der manuelle Takeover-Modus im Browser aktiv ist (Bypass).'
        }
      },
      required: ['action']
    }
  }
];

// 3. Tool-Ausführung
async function executeTool(name, args = {}) {
  switch (name) {
    case 'cobrowser_status': {
      let query = [];
      if (toBool(args.diagnostics, false)) query.push('diagnostics=true');
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/status${qStr}`);
    }

    case 'cobrowser_diagnostics': {
      let query = [];
      if (args.limit) query.push(`limit=${toNum(args.limit, 20)}`);
      if (toBool(args.slowOnly, false)) query.push('slowOnly=true');
      if (toBool(args.reset, false)) query.push('reset=true');
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/diagnostics${qStr}`);
    }

    case 'cobrowser_interactive_map': {
      let query = [];
      if (toBool(args.overlay, false)) query.push('overlay=true');
      if (toBool(args.scanAll, false)) query.push('scanAll=true');
      if (toBool(args.force, false)) query.push('force=true');
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      const res = await apiRequest(`/api/browser/interactive-map${qStr}`);
      if (res && res.elements && Array.isArray(res.elements)) {
        lastInteractiveElements = res.elements;
      }
      return res;
    }

    case 'cobrowser_read_content': {
      let query = [];
      if (args.selector) query.push(`selector=${encodeURIComponent(args.selector)}`);
      if (args.clean !== undefined) query.push(`clean=${toBool(args.clean, true)}`);
      if (args.maxLength) query.push(`maxLength=${toNum(args.maxLength, 60000)}`);
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/content${qStr}`);
    }

    case 'cobrowser_network': {
      let query = [];
      if (args.type) query.push(`type=${encodeURIComponent(args.type)}`);
      if (args.search) query.push(`search=${encodeURIComponent(args.search)}`);
      if (args.includeBody !== undefined) query.push(`includeBody=${toBool(args.includeBody, false)}`);
      if (args.requestId) query.push(`requestId=${encodeURIComponent(args.requestId)}`);
      if (args.limit) query.push(`limit=${toNum(args.limit, 100)}`);
      if (toBool(args.clear, false)) query.push('clear=true');
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/network${qStr}`);
    }

    case 'cobrowser_console': {
      let query = [];
      if (args.type) query.push(`type=${encodeURIComponent(args.type)}`);
      if (args.search) query.push(`search=${encodeURIComponent(args.search)}`);
      if (args.limit) query.push(`limit=${toNum(args.limit, 100)}`);
      if (toBool(args.clear, false)) query.push('clear=true');
      if (args.currentUrlOnly !== undefined) query.push(`currentUrlOnly=${toBool(args.currentUrlOnly, true)}`);
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/console${qStr}`);
    }

    case 'cobrowser_eval':
      return await apiRequest('/api/browser/eval', 'POST', {
        script: args.script || args.expression || args.Expression || args.code,
        force: toBool(args.force, false)
      });

    case 'cobrowser_axtree': {
      let query = [];
      if (args.interactiveOnly !== undefined) query.push(`interactiveOnly=${toBool(args.interactiveOnly, true)}`);
      if (args.maxDepth) query.push(`maxDepth=${toNum(args.maxDepth, 10)}`);
      const qStr = query.length > 0 ? `?${query.join('&')}` : '';
      return await apiRequest(`/api/browser/axtree${qStr}`);
    }

    case 'cobrowser_overlay':
      return await apiRequest('/api/browser/overlay', 'POST', { show: toBool(args.show, true) });

    case 'cobrowser_click': {
      const targetId = args.id !== undefined ? toNum(args.id) : (args.elementId !== undefined ? toNum(args.elementId) : undefined);
      let fallbackSelector = args.selector;
      let fallbackText = args.text;
      let fallbackTag = args.tag;
      if (targetId !== undefined && lastInteractiveElements && lastInteractiveElements.length > 0) {
        const cached = lastInteractiveElements.find(e => e.id === targetId);
        if (cached) {
          if (!fallbackSelector && cached.fallbackSelector) fallbackSelector = cached.fallbackSelector;
          if (!fallbackSelector && cached.domId) fallbackSelector = '#' + cached.domId;
          if (!fallbackText && cached.text) fallbackText = cached.text;
          if (!fallbackTag && cached.tag) fallbackTag = cached.tag;
        }
      }
      return await apiRequest('/api/browser/click', 'POST', {
        id: targetId,
        selector: fallbackSelector,
        text: fallbackText,
        tag: fallbackTag,
        timeoutMs: toNum(args.timeoutMs, 5000),
        force: toBool(args.force, false)
      });
    }

    case 'cobrowser_type': {
      const targetId = args.id !== undefined ? toNum(args.id) : (args.elementId !== undefined ? toNum(args.elementId) : undefined);
      let fallbackSelector = args.selector;
      if (targetId !== undefined && lastInteractiveElements && lastInteractiveElements.length > 0) {
        const cached = lastInteractiveElements.find(e => e.id === targetId);
        if (cached) {
          if (!fallbackSelector && cached.fallbackSelector) fallbackSelector = cached.fallbackSelector;
          if (!fallbackSelector && cached.domId) fallbackSelector = '#' + cached.domId;
        }
      }
      return await apiRequest('/api/browser/type', 'POST', {
        id: targetId,
        selector: fallbackSelector,
        text: args.text !== undefined ? String(args.text) : '',
        clear: toBool(args.clear, true),
        pressEnter: toBool(args.pressEnter, false),
        timeoutMs: toNum(args.timeoutMs, 5000),
        force: toBool(args.force, false)
      });
    }

    case 'cobrowser_fill_form': {
      let fieldsObj = args.fields;
      if (typeof fieldsObj === 'string') {
        try {
          fieldsObj = JSON.parse(fieldsObj);
        } catch {}
      }
      return await apiRequest('/api/browser/fill-form', 'POST', {
        fields: fieldsObj,
        submit: args.submit,
        force: toBool(args.force, false)
      });
    }

    case 'cobrowser_scroll': {
      const scrollId = args.id !== undefined ? toNum(args.id) : (args.elementId !== undefined ? toNum(args.elementId) : undefined);
      const scrollOffsetY = args.offsetY !== undefined ? toNum(args.offsetY) : (args.offset !== undefined ? toNum(args.offset) : (args.yOffset !== undefined ? toNum(args.yOffset) : undefined));
      return await apiRequest('/api/browser/scroll', 'POST', {
        direction: args.direction || 'down',
        pixels: toNum(args.pixels, 600),
        y: args.y !== undefined ? toNum(args.y) : undefined,
        offsetY: scrollOffsetY,
        selector: args.selector,
        id: scrollId,
        force: toBool(args.force, false)
      });
    }

    case 'cobrowser_upload': {
      const uploadId = args.id !== undefined ? toNum(args.id) : (args.elementId !== undefined ? toNum(args.elementId) : undefined);
      const resolvedPath = args.filePath ? path.resolve(process.cwd(), args.filePath) : '';
      return await apiRequest('/api/browser/upload', 'POST', {
        filePath: resolvedPath,
        selector: args.selector,
        id: uploadId,
        force: toBool(args.force, false)
      });
    }

    case 'cobrowser_open':
      return await apiRequest('/api/browser/open', 'POST', args.url ? { url: args.url } : {});

    case 'cobrowser_close':
      return await apiRequest('/api/browser/close', 'POST', {});

    case 'cobrowser_navigate': {
      lastInteractiveElements = [];
      const navTimeout = toNum(args.timeoutMs, 15000);
      return await apiRequest('/api/browser/navigate', 'POST', {
        url: args.url,
        action: args.action,
        open: toBool(args.open, true),
        waitForLoad: toBool(args.waitForLoad, true),
        timeoutMs: navTimeout,
        force: toBool(args.force, false)
      }, navTimeout + 2000);
    }

    case 'cobrowser_screenshot': {
      const res = await apiRequest('/api/browser/screenshot?format=png', 'GET', null, 10000);
      if (res.success && res.base64) {
        const outPath = args.outPath || path.join(process.cwd(), 'co-browser-screenshot.png');
        try {
          const buffer = Buffer.from(res.base64, 'base64');
          fs.writeFileSync(outPath, buffer);
          return { success: true, file: outPath, sizeBytes: res.sizeBytes };
        } catch (e) {
          return { success: false, error: `Screenshot konnte nicht gespeichert werden: ${e.message}` };
        }
      }
      return res;
    }

    case 'cobrowser_handoff':
      return await apiRequest('/api/browser/handoff', 'POST', {
        action: args.action,
        reason: args.reason || '',
        force: toBool(args.force, false)
      });

    default:
      return { success: false, error: `Unbekanntes Tool: ${name}` };
  }
}

// 4. Stdio JSON-RPC 2.0 Loop
function sendResponse(response) {
  process.stdout.write(JSON.stringify(response) + '\n');
}

const rl = readline.createInterface({
  input: process.stdin,
  terminal: false
});

rl.on('line', async (line) => {
  const trimmed = line.trim();
  if (!trimmed) return;

  let msg;
  try {
    msg = JSON.parse(trimmed);
  } catch (e) {
    sendResponse({
      jsonrpc: '2.0',
      id: null,
      error: { code: -32700, message: 'Parse error' }
    });
    return;
  }

  if (!msg || typeof msg !== 'object') {
    sendResponse({
      jsonrpc: '2.0',
      id: null,
      error: { code: -32600, message: 'Invalid Request: root must be an object' }
    });
    return;
  }

  const { id, method, params } = msg;

  if (method === 'initialize') {
    sendResponse({
      jsonrpc: '2.0',
      id,
      result: {
        protocolVersion: '2024-11-05',
        capabilities: {
          tools: {}
        },
        serverInfo: {
          name: 'co-browser-mcp',
          version: '1.0.0'
        }
      }
    });
    return;
  }

  if (method === 'notifications/initialized') {
    // Client-Bestätigung, keine Antwort nötig
    return;
  }

  if (method === 'ping') {
    sendResponse({
      jsonrpc: '2.0',
      id,
      result: {}
    });
    return;
  }

  if (method === 'tools/list') {
    sendResponse({
      jsonrpc: '2.0',
      id,
      result: {
        tools: TOOLS
      }
    });
    return;
  }

  if (method === 'tools/call') {
    const toolName = params?.name;
    const toolArgs = params?.arguments || {};

    try {
      const toolResult = await executeTool(toolName, toolArgs);
      sendResponse({
        jsonrpc: '2.0',
        id,
        result: {
          content: [
            {
              type: 'text',
              text: JSON.stringify(toolResult, null, 2)
            }
          ],
          isError: Boolean(!toolResult || toolResult.success === false || toolResult.error)
        }
      });
    } catch (err) {
      sendResponse({
        jsonrpc: '2.0',
        id,
        result: {
          content: [
            {
              type: 'text',
              text: JSON.stringify({ success: false, error: err.message || String(err) }, null, 2)
            }
          ],
          isError: true
        }
      });
    }
    return;
  }

  // Unbekannte Methode
  if (id !== undefined && id !== null) {
    sendResponse({
      jsonrpc: '2.0',
      id,
      error: {
        code: -32601,
        message: `Method not found: ${method}`
      }
    });
  }
});
