'use strict';
// Reuse the unchanged upstream browser scenarios against their synthetic server.
const path=require('node:path');
const root=path.resolve(__dirname,'../..'),upstream=path.join(root,'src/CodexMobile/upstream');
const config=require(path.join(upstream,'tests/browser/playwright.config.cjs'));
const python = process.env.CODEX_DOCK_TEST_PYTHON || (process.platform === 'win32' ? 'python' : 'python3');
const pythonCommand = process.platform === 'win32' ? '"%CODEX_DOCK_TEST_PYTHON%"' : '"$CODEX_DOCK_TEST_PYTHON"';
const chrome = process.env.CODEX_DOCK_TEST_CHROME || (process.platform === 'win32' ? 'C:/Program Files/Google/Chrome/Application/chrome.exe' : undefined);
module.exports={...config,testMatch:['critical.spec.cjs'],outputDir:path.join(root,'artifacts/upstream-browser-results'),
  reporter:[['list'],['json',{outputFile:path.join(root,'artifacts/upstream-browser-report.json')}]],
  projects:[{name:'chrome-wide',use:{browserName:'chromium',viewport:{width:1280,height:900},launchOptions:{executablePath:chrome}}},
            {name:'chrome-narrow',use:{browserName:'chromium',viewport:{width:390,height:844},launchOptions:{executablePath:chrome}}}],
  webServer:{
    ...config.webServer,
    command:pythonCommand + ' -B tests/mobile/upstream_browser_fixture.py --port 18279',
    env:{...config.webServer.env,CODEX_DOCK_TEST_PYTHON:python},
    cwd:root
  }};
