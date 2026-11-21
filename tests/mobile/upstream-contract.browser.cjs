'use strict';

const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const readline = require('node:readline');
const { test } = require('node:test');
const { chromium } = require('playwright-core');

const root = path.resolve(__dirname, '../..');
const upstreamWeb = path.join(root, 'src/CodexMobile/upstream/web');

// Dock's overlay moves controls and hides upstream shortcuts by these selectors.
const stockSelectors = [
  '#app > #sidebar', '#sidebar .list-heading .appearance-button', '#sidebar .chat-list-toolbar #search',
  '#sidebar .list-filters #archived', '#sidebar .list-filters #list-mode',
  '#sidebar .list-filters #source-filter-label #source-filter', '#sidebar .session-list-region #sessions',
  '#sidebar #host-errors', '#sidebar #new-chat', '#sidebar .sidebar-foot #accounts-button',
  '#sidebar .sidebar-foot #pushplus-settings', '#sidebar .sidebar-foot #clients-button',
  '#sidebar .client-navigation .client-switch-dock',
  '#welcome > h1', '#welcome > p', '#chat > .chat-head .appearance-button',
  '#chat .chat-info #chat-details-button #chat-title', '#chat .timeline #messages',
  '#chat .timeline #approvals', '#chat .timeline #queued', 'form#composer .composer-tools #model-button',
  '#composer .composer-tools #notify-button', '#composer .composer-tools #skills-button',
  '#composer .composer-tools #permissions-button', '#composer #composer-body textarea#message',
  '#composer #send-error', '#composer .compose-bottom .compose-actions #send',
  '#composer select#send-mode', '#composer select#work-mode',
  'dialog#appearance-dialog.picker > .picker-head', '#appearance-dialog > .appearance-actions #appearance-reset',
  '#appearance-dialog label #appearance-clients', '#appearance-dialog label #appearance-accounts',
  '#appearance-dialog label #appearance-pushplus',
];

function startStockFixture(t) {
  const python = process.env.CODEX_DOCK_TEST_PYTHON || (process.platform === 'win32' ? 'python' : 'python3');
  const child = spawn(python, ['-B', 'tests/mobile/upstream_browser_fixture.py'], { cwd: root, windowsHide: true });
  t.after(() => child.kill());
  return new Promise((resolve, reject) => {
    const lines = readline.createInterface({ input: child.stdout });
    const timer = setTimeout(() => reject(new Error('Stock upstream fixture did not start.')), 15000);
    child.stderr.on('data', chunk => process.stderr.write(chunk));
    child.on('error', reject);
    child.on('exit', code => reject(new Error('Stock upstream fixture stopped with exit code ' + code)));
    lines.once('line', line => {
      clearTimeout(timer);
      lines.close();
      if (/^http:\/\/127\.0\.0\.1:\d+$/.test(line)) resolve(line);
      else reject(new Error('Unexpected upstream fixture address: ' + line));
    });
    child.once('close', () => clearTimeout(timer));
  });
}

test('Dock overlays retain their required upstream browser hooks', { timeout: 60000 }, async t => {
  const systemChrome = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
  const windowsExecutable = process.platform === 'win32' && await fs.access(systemChrome).then(() => true, () => false)
    ? systemChrome : undefined;
  const browser = await chromium.launch({ executablePath: process.env.CODEX_DOCK_TEST_CHROME || windowsExecutable });
  t.after(() => browser.close());

  await t.test('stock authenticated web layout and client navigation', async t => {
    const address = await startStockFixture(t);
    const page = await browser.newPage();
    t.after(() => page.close());
    await page.goto(address + '/?case=session-menu');
    await page.locator('#app').waitFor();
    assert.equal(await page.locator('.dock-ui').count(), 0, 'Contract must inspect the upstream DOM before Dock moves controls.');
    const mismatched = await page.evaluate(selectors => selectors
      .map(selector => [selector, document.querySelectorAll(selector).length])
      .filter(([, count]) => count !== 1), stockSelectors);
    assert.deepEqual(mismatched, [], 'Update the Dock overlay when an upstream selector is missing or ambiguous.');

    const missing = await page.evaluate(() => {
      const nav = typeof clientNavigation === 'undefined' ? null : clientNavigation;
      if (!nav) return ['clientNavigation'];
      const checks = [
        ...['footer', 'computer', 'navigation', 'switchPreferences', 'switchToggle', 'settingsActions']
          .map(name => [name, nav[name] instanceof HTMLElement]),
        ...['paint', 'openManager', 'managementClients', 'connection']
          .map(name => [name, typeof nav[name] === 'function']),
      ];
      if (typeof nav.openManager === 'function') {
        nav.openManager();
        checks.push(['manager.dialog', nav.manager?.dialog instanceof HTMLDialogElement],
          ['manager.dialog heading', !!nav.manager?.dialog?.querySelector('.picker-head h2')],
          ['manager.note', nav.manager?.note instanceof HTMLElement]);
      }
      return checks.filter(([, present]) => !present).map(([name]) => name);
    });
    assert.deepEqual(missing, [], 'Dock connection settings rely on these client navigation members.');
  });

  await t.test('local manager hooks on stock upstream account panel instances', async t => {
    const page = await browser.newPage();
    t.after(() => page.close());
    await page.setContent('<section id="accounts"></section><section id="cli"></section><section id="wsl"></section>');
    await page.addScriptTag({ path: path.join(upstreamWeb, 'shared/i18n.js') });
    for (const asset of ['account.js', 'accounts.js', 'cli-access.js', 'wsl-accounts.js']) {
      await page.addScriptTag({ path: path.join(upstreamWeb, 'features/accounts', asset) });
    }
    const missing = await page.evaluate(() => {
      const constructors = [
        ['AccountsPanel', typeof AccountsPanel === 'function' ? AccountsPanel : null],
        ['CliAccessPanel', typeof CliAccessPanel === 'function' ? CliAccessPanel : null],
        ['WslAccountsPanel', typeof WslAccountsPanel === 'function' ? WslAccountsPanel : null],
      ];
      if (constructors.some(([, type]) => type === null))
        return constructors.filter(([, type]) => type === null).map(([name]) => name);
      const account = new AccountsPanel({ root: document.querySelector('#accounts'), desktop: true });
      const cli = new CliAccessPanel({ root: document.querySelector('#cli'), provider: 'codex' });
      const wsl = new WslAccountsPanel({ root: document.querySelector('#wsl'), provider: 'codex', desktop: true });
      const checks = [
        ['accounts.tools summary', !!account.tools?.querySelector('summary')],
        ...['refreshButton', 'updates'].map(name => ['accounts.' + name, account[name] instanceof HTMLElement]),
        ['accounts.refresh', typeof account.refresh === 'function'],
        ...['root', 'rows', 'status', 'error', 'native', 'schemeRow', 'cancel']
          .map(name => ['cli.' + name, cli[name] instanceof HTMLElement]),
        ...['name', 'baseUrl', 'key', 'model']
          .map(name => ['cli.' + name + ' label and input', cli[name]?.label instanceof HTMLLabelElement &&
            cli[name]?.input instanceof HTMLInputElement]),
        ['cli.form', cli.form instanceof HTMLFormElement],
        ...['.cli-access-model-hint', '.cli-access-form-actions']
          .map(selector => ['cli.form ' + selector, cli.form?.querySelectorAll(selector).length === 1]),
        ...['setBusy', 'edit', 'resetForm', 'refresh'].map(name => ['cli.' + name, typeof cli[name] === 'function']),
        ['cli.cancel.onclick', typeof cli.cancel?.onclick === 'function'],
        ['wsl.select', typeof wsl.select === 'function'],
      ];
      return checks.filter(([, present]) => !present).map(([name]) => name);
    });
    assert.deepEqual(missing, [], 'Update manager/app.js when upstream panel members or form anchors change.');
  });
});
