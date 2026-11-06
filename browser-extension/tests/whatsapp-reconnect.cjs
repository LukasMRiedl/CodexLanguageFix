const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
(async () => {
 const browser = await chromium.launch({headless:true, executablePath:process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe'});
 try {
  const page = await browser.newPage({viewport:{width:1400,height:1100}});
  await page.route('https://web.whatsapp.com/**', route => route.fulfill({contentType:'text/html', body:`<!doctype html><body><footer style="position:fixed;left:496.91px;top:963.43px;width:585.38px;height:64px;background:#eee"><div style="position:absolute;left:11.99px;top:0;width:561px;height:52px"></div><button aria-label="Attach" style="position:absolute;left:17.57px;top:6.43px;width:40px;height:40px">+</button><button aria-label="Emojis" style="position:absolute;left:57.57px;top:6.43px;width:40px;height:40px">☺</button><div style="position:absolute;left:103.57px;top:14.97px;width:419.23px;height:22.05px;overflow:hidden"><div id="editor" data-testid="conversation-compose-box-input" data-lexical-editor="true" contenteditable="true" role="textbox" style="width:419.23px;height:22.05px;line-height:22.05px;font:16px/22.05px sans-serif;white-space:nowrap;outline:none">Das ist ein Tset.</div></div><button aria-label="Send" style="position:absolute;left:527.8px;top:6.43px;width:40px;height:40px">↑</button></footer></body>`}));
  await page.goto('https://web.whatsapp.com/fixture');
  await page.evaluate(() => {
   globalThis.requests=[]; globalThis.failNextConnect=false; globalThis.connections=0;
   globalThis.chrome={runtime:{connect(){
    connections++; if(failNextConnect){failNextConnect=false;throw new Error('synthetic unavailable');}
    let receive,disconnected;const connection={onMessage:{addListener(fn){receive=fn;}},onDisconnect:{addListener(fn){disconnected=fn;}},disconnect(){},postMessage(m){if(globalThis.failNextPost===m.type){globalThis.failNextPost=null;throw new Error('synthetic disconnected post');}if(m.type==='state'&&globalThis.dropNextState){globalThis.dropNextState=false;return;}requests.push(m);setTimeout(()=>{if(m.type==='state')receive({...m,type:'state',enabled:true,prepare:false,shortcutModifiers:3,shortcutVirtualKey:188});if(m.type==='correct')receive({...m,type:'result',text:m.text.replace('Tset','Test')});},20);}};
    globalThis.disconnectCurrent=()=>disconnected();return connection;
   }}};
  });
  for(const file of ['core.js','content.js'])await page.addScriptTag({path:path.resolve('browser-extension',file)});
  const editor=page.locator('#editor'),aa=page.getByRole('button',{name:'Text korrigieren',exact:true});
  await editor.focus();await aa.waitFor({state:'visible'});await page.waitForTimeout(60);
  const box=await aa.boundingBox();assert.ok(box);assert.equal(box.width,28);assert.equal(box.height,28);assert.ok(box.x>=600&&box.x+28<=1020&&box.y>=967&&box.y+28<=1023);
  const collisions=await page.evaluate(()=>{const a=document.querySelector('#codex-language-fix-overlay').getBoundingClientRect();const range=document.createRange();range.selectNodeContents(document.querySelector('#editor'));const rects=[...range.getClientRects(),...Array.from(document.querySelectorAll('footer button'),b=>b.getBoundingClientRect())];return rects.some(r=>a.left<r.right+4&&a.right>r.left-4&&a.top<r.bottom+4&&a.bottom>r.top-4);});assert.equal(collisions,false);
  await aa.click();await page.waitForFunction(()=>document.querySelector('#editor').innerText==='Das ist ein Test.');await aa.click();assert.equal(await editor.innerText(),'Das ist ein Tset.');
  await page.evaluate(()=>{disconnectCurrent();failNextConnect=true;});await aa.click();assert.equal(await aa.isEnabled(),true);const before=await page.evaluate(()=>requests.filter(m=>m.type==='correct').length);await aa.click();await page.waitForFunction(()=>document.querySelector('#editor').innerText==='Das ist ein Test.');assert.equal(await page.evaluate(()=>requests.filter(m=>m.type==='correct').length),before+1);
  await aa.click();await page.evaluate(()=>disconnectCurrent());const connectionCount=await page.evaluate(()=>connections);await editor.dispatchEvent('focusin');await page.waitForTimeout(70);assert.equal(await page.evaluate(()=>connections),connectionCount+1);await page.waitForTimeout(100);assert.equal(await page.evaluate(()=>connections),connectionCount+1);
  await page.evaluate(()=>{globalThis.failNextPost='correct';});await aa.click();assert.equal(await aa.isEnabled(),true);assert.equal(await editor.innerText(),'Das ist ein Tset.');await aa.click();await page.waitForFunction(()=>document.querySelector('#editor').innerText==='Das ist ein Test.');await aa.click();
  await page.evaluate(()=>{disconnectCurrent();globalThis.dropNextState=true;});const timeoutConnections=await page.evaluate(()=>connections);await aa.click();await page.waitForTimeout(5200);assert.equal(await aa.isEnabled(),true);assert.equal(await page.evaluate(()=>connections),timeoutConnections+1);assert.equal(await editor.innerText(),'Das ist ein Tset.');await aa.click();await page.waitForFunction(()=>document.querySelector('#editor').innerText==='Das ist ein Test.');await aa.click();
  await editor.fill('WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW');await page.waitForTimeout(80);assert.equal(await aa.isVisible(),false);
  console.log('PASS: verified WhatsApp 22px composer inner placement, no text/control overlap, correction/Undo, disconnected send fail-fast, explicit handshake retry, refocus reconnect without loop, occupied line hides');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
