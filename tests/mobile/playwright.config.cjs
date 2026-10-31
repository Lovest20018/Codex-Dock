'use strict';
// Reuse the unchanged upstream browser scenarios against their synthetic server.
const path=require('node:path');
const root=path.resolve(__dirname,'../..'),upstream=path.join(root,'src/CodexMobile/upstream');
const config=require(path.join(upstream,'tests/browser/playwright.config.cjs'));
module.exports={...config,testMatch:['critical.spec.cjs'],outputDir:path.join(root,'artifacts/upstream-browser-results'),
  reporter:[['list'],['json',{outputFile:path.join(root,'artifacts/upstream-browser-report.json')}]],
  projects:[{name:'chrome-wide',use:{browserName:'chromium',viewport:{width:1280,height:900},launchOptions:{executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe'}}},
            {name:'chrome-narrow',use:{browserName:'chromium',viewport:{width:390,height:844},launchOptions:{executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe'}}}],
  webServer:{...config.webServer,command:'"C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe" -B tests/mobile/upstream_browser_fixture.py --port 18279',cwd:root}};
