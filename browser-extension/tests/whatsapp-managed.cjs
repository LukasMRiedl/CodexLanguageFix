const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const path = require('node:path');

(async () => {
 const browser = await chromium.launch({headless:true, executablePath:process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe'});
 try {
  const page = await browser.newPage({viewport:{width:1200,height:800}});
  await page.route('https://web.whatsapp.com/**', route => route.fulfill({contentType:'text/html',body:'<!doctype html><footer style="margin:100px;width:800px;height:160px"><div id="editor" data-testid="conversation-compose-box-input" data-lexical-editor="true" contenteditable="true" role="textbox" style="width:600px;height:80px"></div></footer>'}));
  await page.goto('https://web.whatsapp.com/fixture');
  await page.evaluate(() => {
   globalThis.commands = [];
   const nativeCommand = document.execCommand.bind(document);
   let reconciling = false;
   // Model a managed editor that reconciles its text node after the first input
   // and rejects further insertions until that input transaction has settled.
   document.execCommand = (command, ui, value) => {
    if (command !== 'insertText') return nativeCommand(command, ui, value);
    commands.push({value, accepted:!reconciling});
    if (reconciling) return false;
    reconciling = true;
    try {
     const result = nativeCommand(command, ui, value);
     const editor = document.querySelector('#editor');
     editor.replaceChildren(document.createTextNode(editor.innerText));
     return result;
    } finally { queueMicrotask(() => { reconciling = false; }); }
   };
   globalThis.chrome={runtime:{connect(){let receive;return {
    onMessage:{addListener(fn){receive=fn;}},onDisconnect:{addListener(){}},disconnect(){},
    postMessage(m){setTimeout(()=>{
     if(m.type==='state')receive({...m,type:'state',enabled:true,prepare:false,shortcutModifiers:3,shortcutVirtualKey:188});
     if(m.type==='correct')receive({...m,type:'result',text:m.text.replaceAll('Tset','Test').replaceAll('ncoh','noch').replaceAll('Fheler','Fehler')});
    },0);}
   };}}};
  });
  for(const file of ['core.js','content.js']) await page.addScriptTag({path:path.resolve('browser-extension',file)});
  const editor=page.locator('#editor'), aa=page.getByRole('button',{name:'Text korrigieren',exact:true});
  const original='Das ist ein Tset. Und ncoh ein Fheler.';
  const corrected='Das ist ein Test. Und noch ein Fehler.';
  await editor.fill(original); await editor.focus(); await aa.waitFor({state:'visible'});
  await page.waitForTimeout(50);
  await aa.click(); await page.waitForTimeout(100);
  assert.equal(await editor.innerText(),corrected,'One Aa click must apply all three corrections in the managed input transaction');
  assert.equal(await page.evaluate(()=>commands.length),1);
  await aa.click();
  assert.equal(await editor.innerText(),original,'One Undo click must restore the exact original');
  assert.equal(await page.evaluate(()=>commands.length),2);
  assert.equal(await page.evaluate(()=>commands.every(command=>command.accepted)),true);

  // A contiguous correction crossing formatting nodes must be rejected before input.
  await editor.evaluate(el=>{el.innerHTML='Das ist ein <b>Tset.</b> Und ncoh ein Fheler.';el.dispatchEvent(new Event('input',{bubbles:true}));});
  await page.waitForTimeout(50); await aa.click(); await page.waitForTimeout(100);
  assert.equal(await editor.innerText(),original);
  assert.equal(await editor.locator('b').innerText(),'Tset.');
  assert.equal(await page.evaluate(()=>commands.length),2,'Unsupported structured edits must not start a partial transaction');
  console.log('PASS: managed WhatsApp three-error correction and exact Undo each use one input transaction; cross-node changes are refused before writing');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
