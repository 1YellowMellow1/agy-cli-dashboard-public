#!/usr/bin/env node
/**
 * scripts/browser-tool.js
 * Ultra-leichter CLI-Client für den Antigravity KI-Co-Browser HTTP-RPC Server.
 * Nutzt natives Node.js fetch (keine npm-Dependencies nötig).
 *
 * Beispiele:
 *   node scripts/browser-tool.js status
 *   node scripts/browser-tool.js map
 *   node scripts/browser-tool.js content
 *   node scripts/browser-tool.js network --include-body
 *   node scripts/browser-tool.js axtree
 *   node scripts/browser-tool.js click --id 1
 *   node scripts/browser-tool.js click --selector "#submit-btn"
 *   node scripts/browser-tool.js type --id 2 --text "Max" --enter
 *   node scripts/browser-tool.js fill --data '{"#name": "Max Mustermann", "#city": "Berlin"}' --submit "#save-btn"
 *   node scripts/browser-tool.js screenshot --out screen.png
 *   node scripts/browser-tool.js handoff --reason "Bitte 2FA bestätigen"
 */

const fs = require('fs');
const path = require('path');

// 1. Port-Discovery aus AppData
function getBaseUrl() {
  const localAppData = process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || '', 'AppData', 'Local');
  const endpointFile = path.join(localAppData, 'AgyCliDashboard', 'co_browser_endpoint.json');
  if (fs.existsSync(endpointFile)) {
    try {
      const data = JSON.parse(fs.readFileSync(endpointFile, 'utf8'));
      if (data && data.apiPort) {
        return `http://127.0.0.1:${data.apiPort}`;
      }
    } catch {}
  }
  return 'http://127.0.0.1:9223';
}

const BASE_URL = getBaseUrl();

// 2. Argument-Parser
function parseArgs() {
  const args = process.argv.slice(2);
  const command = args[0] || 'status';
  const options = {};

  for (let i = 1; i < args.length; i++) {
    const arg = args[i];
    if (arg.startsWith('--')) {
      const key = arg.slice(2);
      const next = args[i + 1];
      if (next && !next.startsWith('--')) {
        options[key] = next;
        i++;
      } else {
        options[key] = true;
      }
    }
  }

  return { command, options };
}

async function request(endpoint, method = 'GET', body = null) {
  const url = `${BASE_URL}${endpoint}`;
  const init = {
    method,
    headers: { 'Content-Type': 'application/json' }
  };
  if (body) {
    init.body = JSON.stringify(body);
  }

  try {
    const res = await fetch(url, init);
    const data = await res.json();
    return data;
  } catch (err) {
    console.error(`[BrowserTool Error] Verbindung zu ${url} fehlgeschlagen:`, err.message);
    process.exit(1);
  }
}

async function main() {
  const { command, options } = parseArgs();

  switch (command.toLowerCase()) {
    case 'status': {
      const res = await request('/api/browser/status');
      console.log('=== KI-CO-BROWSER STATUS ===');
      console.log(`Status:       ${res.status}`);
      console.log(`Titel:        ${res.title}`);
      console.log(`URL:          ${res.url}`);
      console.log(`API Port:     ${res.apiPort}`);
      console.log(`CDP Port:     ${res.cdpPort}`);
      console.log(`Takeover:     ${res.isTakeoverActive ? 'Ja (Mensch aktiv)' : 'Nein (KI frei)'}`);
      console.log(`Bereit:       ${res.isInitialized}`);
      console.log(`Geöffnet (UI): ${res.isOpen}`);
      break;
    }

    case 'open': {
      const payload = {};
      if (options.url) payload.url = options.url;
      const res = await request('/api/browser/open', 'POST', payload);
      if (res.success) {
        console.log('[OK] KI-Co-Browser im Split-Screen geöffnet:', res);
      } else {
        console.error('[Fehler] Öffnen fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'close': {
      const res = await request('/api/browser/close', 'POST', {});
      if (res.success) {
        console.log('[OK] KI-Co-Browser im Split-Screen geschlossen:', res);
      } else {
        console.error('[Fehler] Schließen fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'map': {
      const q = [];
      if (options.overlay) q.push('overlay=true');
      if (options['scan-all'] || options.scanall) q.push('scanAll=true');
      const qStr = q.length > 0 ? `?${q.join('&')}` : '';
      const res = await request(`/api/browser/interactive-map${qStr}`);
      console.log(`\n[Interactive Map] ${res.title} (${res.url})\n`);
      console.log('ID  | TAG       | BESCHREIBUNG                            | SELEKTOR');
      console.log('----+-----------+-----------------------------------------+------------------------');
      for (const el of res.elements || []) {
        const idStr = String(el.id).padEnd(3);
        const tagStr = String(el.tag).padEnd(9);
        const cleanText = (el.text || '').replace(/\r|\n/g, ' ').trim();
        const txtStr = (cleanText.length > 39 ? cleanText.slice(0, 36) + '...' : cleanText).padEnd(39);
        console.log(`${idStr} | ${tagStr} | ${txtStr} | ${el.selector}`);
      }
      console.log(`\nInsgesamt ${res.elementCount || 0} interaktive Elemente gefunden.`);
      if (res.overlay) console.log('[Info] Nummern-Badges wurden auf dem Bildschirm eingeblendet.');
      break;
    }

    case 'content':
    case 'read': {
      const q = [];
      if (options.selector) q.push(`selector=${encodeURIComponent(options.selector)}`);
      if (options.raw) q.push('clean=false');
      if (options.maxlength || options['max-length']) {
        q.push(`maxLength=${parseInt(options.maxlength || options['max-length'], 10)}`);
      }
      const qStr = q.length > 0 ? `?${q.join('&')}` : '';
      const res = await request(`/api/browser/content${qStr}`);
      if (res && res.success) {
        console.log(`\n[Content] ${res.title} (${res.url})\n`);
        if (res.description) {
          console.log(`Beschreibung: ${res.description}\n`);
        }
        console.log(res.content);
        console.log(`\n(Zeichen: ${res.characterCount}, Konvertierte Tabellen: ${res.tableCount})\n`);
      } else {
        console.error('[Fehler] Text-Extraktion fehlgeschlagen:', res ? res.error : 'Unbekannter Fehler');
        process.exit(1);
      }
      break;
    }

    case 'network': {
      const q = [];
      if (options.type) q.push(`type=${encodeURIComponent(options.type)}`);
      if (options.search) q.push(`search=${encodeURIComponent(options.search)}`);
      if (options['include-body'] || options.includebody || options.body) q.push('includeBody=true');
      if (options['request-id'] || options.requestid || options.id) {
        q.push(`requestId=${encodeURIComponent(options['request-id'] || options.requestid || options.id)}`);
      }
      if (options.limit) q.push(`limit=${parseInt(options.limit, 10)}`);
      if (options.clear) q.push('clear=true');
      const qStr = q.length > 0 ? `?${q.join('&')}` : '';

      const res = await request(`/api/browser/network${qStr}`);
      if (res && res.success) {
        if (options.clear) {
          console.log('[OK] Netzwerk-Puffer erfolgreich geleert.');
          break;
        }
        console.log(`\n=== NETZWERK-TRAFFIC (${res.count} Treffer / ${res.totalBuffered} gepuffert) ===\n`);
        for (const entry of res.entries || []) {
          const statusStr = entry.failed ? 'ERR ' : String(entry.statusCode || '---').padEnd(4);
          const methodStr = String(entry.method || 'GET').padEnd(6);
          const typeStr = String(entry.resourceType || 'Fetch').padEnd(8);
          console.log(`• [${methodStr} ${statusStr}] (${typeStr}) ${entry.url}`);
          if (entry.mimeType) console.log(`  MIME: ${entry.mimeType} | ID: ${entry.requestId}`);
          if (entry.responseBody) {
            console.log(`  Payload:\n${entry.responseBody.slice(0, 1000)}${entry.responseBody.length > 1000 ? '...' : ''}\n`);
          }
        }
        console.log(`\n(Tipp: Nutze --include-body oder --search <Suchbegriff>, um JSON-APIs zu filtern)\n`);
      } else {
        console.error('[Fehler] Netzwerk-Abfrage fehlgeschlagen:', res ? res.error : 'Unbekannter Fehler');
        process.exit(1);
      }
      break;
    }

    case 'axtree': {
      const q = [];
      if (options.all) q.push('interactiveOnly=false');
      if (options['max-depth'] || options.maxdepth) {
        q.push(`maxDepth=${parseInt(options['max-depth'] || options.maxdepth, 10)}`);
      }
      const qStr = q.length > 0 ? `?${q.join('&')}` : '';

      const res = await request(`/api/browser/axtree${qStr}`);
      if (res && res.success) {
        console.log(`\n[Accessibility Tree] ${res.title} (${res.url})\n`);
        console.log(res.summary);
        console.log(`\n(${res.nodeCount} semantische Knoten erfasst)\n`);
      } else {
        console.error('[Fehler] AXTree-Abfrage fehlgeschlagen:', res ? res.error : 'Unbekannter Fehler');
        process.exit(1);
      }
      break;
    }

    case 'click': {
      const payload = {
        timeoutMs: options.timeout ? parseInt(options.timeout, 10) : 5000
      };
      if (options.id) payload.id = parseInt(options.id, 10);
      else if (options.selector) payload.selector = options.selector;
      else if (options.text) payload.text = options.text;
      else {
        console.error('Bitte --id <Zahl>, --selector <CSS> oder --text <String> angeben.');
        process.exit(1);
      }

      const res = await request('/api/browser/click', 'POST', payload);
      if (res.success) {
        console.log('[OK] Klick erfolgreich ausgeführt:', res);
      } else {
        console.error('[Fehler] Klick fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'type': {
      const payload = {
        text: options.text || '',
        clear: options['no-clear'] ? false : true,
        pressEnter: Boolean(options.enter),
        timeoutMs: options.timeout ? parseInt(options.timeout, 10) : 5000
      };
      if (options.id) payload.id = parseInt(options.id, 10);
      else if (options.selector) payload.selector = options.selector;
      else {
        console.error('Bitte --id <Zahl> oder --selector <CSS> angeben.');
        process.exit(1);
      }

      const res = await request('/api/browser/type', 'POST', payload);
      if (res.success) {
        console.log('[OK] Text eingegeben:', res);
      } else {
        console.error('[Fehler] Eingabe fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'fill': {
      if (!options.data) {
        console.error('Bitte --data \'{"#id": "Wert"}\' angeben.');
        process.exit(1);
      }
      let fieldsObj;
      try {
        fieldsObj = JSON.parse(options.data);
      } catch (e) {
        console.error('Ungültiges JSON in --data:', e.message);
        process.exit(1);
      }

      const payload = { fields: fieldsObj };
      if (options.submit) payload.submit = options.submit;

      const res = await request('/api/browser/fill-form', 'POST', payload);
      if (res.success) {
        console.log(`[OK] Formular ausgefüllt: ${res.filledCount} Felder befüllt (Submit: ${res.submitted})`);
      } else {
        console.error('[Fehler] Formular-Befüllung fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'eval': {
      if (!options.script) {
        console.error('Bitte --script <JS-Code> angeben.');
        process.exit(1);
      }
      const res = await request('/api/browser/eval', 'POST', { script: options.script });
      if (res.success) {
        console.log(res.raw);
      } else {
        console.error('[Fehler] Script-Ausführung fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'nav':
    case 'navigate': {
      const payload = {};
      if (options.url) payload.url = options.url;
      else if (options.action) payload.action = options.action;
      else {
        console.error('Bitte --url <URL> oder --action <reload|back|forward> angeben.');
        process.exit(1);
      }
      if (options['no-open']) {
        payload.open = false;
      } else {
        payload.open = true;
      }
      const res = await request('/api/browser/navigate', 'POST', payload);
      if (res.success) {
        console.log('[OK] Navigation ausgeführt:', res);
      } else {
        console.error('[Fehler] Navigation fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'screenshot': {
      const outPath = options.out || path.join(process.cwd(), 'co-browser-screenshot.png');
      const res = await request('/api/browser/screenshot?format=png');
      if (res.success && res.base64) {
        const buffer = Buffer.from(res.base64, 'base64');
        fs.writeFileSync(outPath, buffer);
        console.log(`[OK] Screenshot gespeichert (${res.sizeBytes} Bytes): ${outPath}`);
      } else {
        console.error('[Fehler] Screenshot fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'handoff': {
      const reason = options.reason || 'Menschliche Interaktion erforderlich';
      const res = await request('/api/browser/handoff', 'POST', {
        action: 'waiting_for_user',
        reason
      });
      if (res.success) {
        console.log(`[OK] An Benutzer übergeben: ${reason}`);
      } else {
        console.error('[Fehler] Handoff fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'ready': {
      const res = await request('/api/browser/handoff', 'POST', { action: 'ready' });
      if (res.success) {
        console.log('[OK] Status auf "Bereit" gesetzt.');
      } else {
        console.error('[Fehler] Status-Wechsel fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'overlay': {
      const show = options.hide || options.off ? false : true;
      const res = await request('/api/browser/overlay', 'POST', { show });
      if (res.success) {
        console.log(`[OK] Nummern-Overlay ${show ? 'eingeblendet' : 'ausgeblendet'}.`);
      } else {
        console.error('[Fehler] Overlay fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'scroll': {
      const payload = {
        direction: options.dir || options.direction || 'down',
        pixels: options.pixels ? parseInt(options.pixels, 10) : 600
      };
      if (options.selector) payload.selector = options.selector;
      if (options.id) payload.id = parseInt(options.id, 10);
      const res = await request('/api/browser/scroll', 'POST', payload);
      if (res.success) {
        console.log('[OK] Scroll ausgeführt:', res);
      } else {
        console.error('[Fehler] Scroll fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    case 'upload': {
      if (!options.file && !options.filepath) {
        console.error('Bitte --file <Pfad> angeben.');
        process.exit(1);
      }
      const rawPath = options.file || options.filepath;
      const absPath = path.resolve(process.cwd(), rawPath);
      const payload = {
        filePath: absPath,
        selector: options.selector || "input[type='file']"
      };
      if (options.id) payload.id = parseInt(options.id, 10);
      const res = await request('/api/browser/upload', 'POST', payload);
      if (res.success) {
        console.log('[OK] Datei headless hochgeladen:', res);
      } else {
        console.error('[Fehler] Upload fehlgeschlagen:', res.error);
        process.exit(1);
      }
      break;
    }

    default:
      console.log(`Unbekannter Befehl: ${command}`);
      console.log('Verfügbare Befehle: status, open, close, map, content, network, axtree, click, type, fill, eval, nav, scroll, upload, overlay, screenshot, handoff, ready');
      process.exit(1);
  }
}

main();
