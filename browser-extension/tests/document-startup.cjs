const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const core=fs.readFileSync(path.resolve('browser-extension/core.js'),'utf8');
const content=fs.readFileSync(path.resolve('browser-extension/content.js'),'utf8');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe'});
 try {
  for(const contentType of ['text/html','application/xhtml+xml']){
   const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.route('https://document.fixture/**',route=>route.fulfill({contentType,body:'<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Document fixture</title></head><body style="margin:80px"><textarea id="prose" aria-label="Nachricht" style="width:300px;height:100px">Das ist ein Tset.</textarea></body></html>'}));
   await page.goto('https://document.fixture/');
   await page.evaluate(()=>{globalThis.chrome={runtime:{connect(){let receive;return{onMessage:{addListener(fn){receive=fn;}},onDisconnect:{addListener(){}},disconnect(){},postMessage(m){queueMicrotask(()=>{if(m.type==='state')receive({...m,type:'state',enabled:true,prepare:false,shortcutModifiers:3,shortcutVirtualKey:188});if(m.type==='correct')receive({...m,type:'result',text:m.text.replace('Tset','Test')});});}};}}};});
   await page.evaluate(core);await page.evaluate(content);
   const field=page.locator('#prose'),aa=page.getByRole('button',{name:'Text korrigieren',exact:true});
   await field.focus();await aa.waitFor({state:'visible'});await aa.click();
   await page.waitForFunction(()=>document.querySelector('#prose').value==='Das ist ein Test.');
   await aa.click();assert.equal(await field.evaluate(e=>e.value),'Das ist ein Tset.');
   assert.equal(await page.locator('#codex-language-fix-overlay').evaluate(e=>e.namespaceURI),'http://www.w3.org/1999/xhtml');
   assert.deepEqual(errors,[]);await page.close();
  }
  for(const [contentType,body] of [['image/svg+xml','<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><foreignObject width="200" height="100"><div xmlns="http://www.w3.org/1999/xhtml"><textarea id="foreign">Fixture</textarea></div></foreignObject></svg>'],['application/xml','<root><item>Fixture</item></root>']]){
   const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.route('https://document.fixture/**',route=>route.fulfill({contentType,body}));await page.goto('https://document.fixture/');
   assert.equal(await page.evaluate(()=>typeof document.createElement('div').style),'undefined');
   await page.evaluate(()=>{globalThis.connections=0;globalThis.chrome={runtime:{connect(){connections++;throw new Error('Unexpected connection');}}};});
   if(await page.locator('#foreign').count())await page.locator('#foreign').focus();
   await page.evaluate(core);await page.evaluate(content);
   assert.equal(await page.locator('#codex-language-fix-overlay').count(),0);assert.equal(await page.evaluate(()=>connections),0);assert.deepEqual(errors,[]);await page.close();
  }
  console.log('PASS: real HTML and XHTML correction/Undo; SVG and XML startup ignored without style error or connection');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
