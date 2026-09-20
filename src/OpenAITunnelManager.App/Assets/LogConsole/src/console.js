import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { SearchAddon } from '@xterm/addon-search';
import { WebLinksAddon } from '@xterm/addon-web-links';

const host = document.getElementById('scroll-host');
const terminalElement = document.getElementById('terminal');
const fitAddon = new FitAddon();
const searchAddon = new SearchAddon();
let wrapEnabled = true;
let maxColumns = 160;
let search = { query: '', regex: false, caseSensitive: false };
let suppressViewportMessage = false;

const terminal = new Terminal({
  allowTransparency: true,
  convertEol: true,
  cursorBlink: false,
  disableStdin: true,
  fontFamily: "'Cascadia Mono', Consolas, 'Microsoft YaHei UI', monospace",
  fontSize: 13,
  lineHeight: 1.15,
  scrollback: 30000,
  smoothScrollDuration: 0,
  theme: darkTheme()
});

terminal.loadAddon(fitAddon);
terminal.loadAddon(searchAddon);
terminal.loadAddon(new WebLinksAddon((_event, uri) => {
  post({ type: 'openLink', url: uri });
}));
terminal.open(terminalElement);
fitAddon.fit();

terminal.onScroll(() => postViewport());
terminal.onSelectionChange(() => {
  post({ type: 'selection', hasSelection: terminal.hasSelection() });
});

window.addEventListener('resize', () => applySize());

window.chrome?.webview?.addEventListener('message', event => {
  const message = event.data ?? {};
  switch (message.type) {
    case 'append':
      appendLines(Array.isArray(message.lines) ? message.lines : [], message.followTail !== false);
      break;
    case 'replaceAll':
      replaceAll(Array.isArray(message.lines) ? message.lines : [], message.followTail !== false);
      break;
    case 'clear':
      terminal.reset();
      terminal.clear();
      maxColumns = 160;
      applySize();
      postViewport();
      break;
    case 'scrollToBottom':
      terminal.scrollToBottom();
      postViewport();
      break;
    case 'search':
      search = {
        query: message.query ?? '',
        regex: message.regex === true,
        caseSensitive: message.caseSensitive === true
      };
      if (search.query) findNext();
      break;
    case 'findNext':
      findNext();
      break;
    case 'findPrevious':
      findPrevious();
      break;
    case 'setWrap':
      wrapEnabled = message.enabled !== false;
      applySize();
      break;
    case 'setTheme':
      terminal.options.theme = message.mode === 'light' ? lightTheme() : darkTheme();
      break;
    case 'copy':
      copySelection();
      break;
    case 'selectAll':
      terminal.selectAll();
      break;
    case 'focus':
      terminal.focus();
      break;
  }
});

function appendLines(lines, followTail) {
  if (lines.length === 0) return;
  updateMaxColumns(lines);
  const payload = lines.join('');
  suppressViewportMessage = followTail;
  terminal.write(payload, () => {
    applySize();
    if (followTail) terminal.scrollToBottom();
    suppressViewportMessage = false;
    postViewport();
  });
}

function replaceAll(lines, followTail) {
  terminal.reset();
  terminal.clear();
  maxColumns = 160;
  updateMaxColumns(lines);
  const payload = lines.join('');
  suppressViewportMessage = true;
  terminal.write(payload, () => {
    applySize();
    if (followTail) terminal.scrollToBottom();
    suppressViewportMessage = false;
    postViewport();
  });
}

function updateMaxColumns(lines) {
  for (const line of lines) {
    const plain = String(line)
      .replace(/\x1b\[[0-9;]*m/g, '')
      .replace(/[\r\n]+$/g, '');
    maxColumns = Math.max(maxColumns, Math.min(1024, plain.length + 4));
  }
}

function applySize() {
  if (!terminal.element) return;
  if (wrapEnabled) {
    host.classList.remove('no-wrap');
    terminal.element.style.width = '100%';
    try { fitAddon.fit(); } catch {}
    return;
  }

  host.classList.add('no-wrap');
  const rows = Math.max(2, terminal.rows);
  const cols = Math.max(160, Math.min(1024, maxColumns));
  if (terminal.cols !== cols) terminal.resize(cols, rows);
  requestAnimationFrame(() => {
    const screen = terminal.element?.querySelector('.xterm-screen');
    const width = screen?.getBoundingClientRect().width ?? 0;
    if (width > 0) terminal.element.style.width = Math.ceil(width) + 'px';
  });
}

function findNext() {
  if (!search.query) return;
  searchAddon.findNext(search.query, {
    regex: search.regex,
    caseSensitive: search.caseSensitive
  });
}

function findPrevious() {
  if (!search.query) return;
  searchAddon.findPrevious(search.query, {
    regex: search.regex,
    caseSensitive: search.caseSensitive
  });
}

async function copySelection() {
  const text = terminal.getSelection();
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    post({ type: 'copyText', text });
  }
}

function postViewport() {
  if (suppressViewportMessage) return;
  const buffer = terminal.buffer.active;
  post({
    type: 'viewport',
    atBottom: buffer.viewportY >= buffer.baseY
  });
}

function post(message) {
  window.chrome?.webview?.postMessage(message);
}

function darkTheme() {
  return {
    background: '#00000000',
    foreground: '#d6d6d6',
    cursor: '#d6d6d6',
    selectionBackground: '#3a5f8a88',
    black: '#0c0c0c',
    red: '#e74856',
    green: '#16c60c',
    yellow: '#f9f1a5',
    blue: '#3b78ff',
    magenta: '#b4009e',
    cyan: '#61d6d6',
    white: '#cccccc',
    brightBlack: '#767676',
    brightRed: '#ff6b72',
    brightGreen: '#5fd35f',
    brightYellow: '#fff59d',
    brightBlue: '#6ea1ff',
    brightMagenta: '#d76bd7',
    brightCyan: '#87e5e5',
    brightWhite: '#f2f2f2'
  };
}

function lightTheme() {
  return {
    background: '#00000000',
    foreground: '#242424',
    cursor: '#242424',
    selectionBackground: '#8bb8e888',
    black: '#242424',
    red: '#c42b1c',
    green: '#0f7b0f',
    yellow: '#8a6400',
    blue: '#005fb8',
    magenta: '#881798',
    cyan: '#007c91',
    white: '#e5e5e5',
    brightBlack: '#5d5d5d',
    brightRed: '#a80000',
    brightGreen: '#107c10',
    brightYellow: '#7a5d00',
    brightBlue: '#004e8c',
    brightMagenta: '#744da9',
    brightCyan: '#005b70',
    brightWhite: '#ffffff'
  };
}

post({ type: 'ready' });
postViewport();
