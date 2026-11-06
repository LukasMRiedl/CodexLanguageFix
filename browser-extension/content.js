(()=>{
'use strict';
const HTML_NAMESPACE='http://www.w3.org/1999/xhtml';
if(document.documentElement?.namespaceURI!==HTML_NAMESPACE||document.documentElement.localName!=='html')return;
const id=()=>Array.from(crypto.getRandomValues(new Uint8Array(16)),b=>b.toString(16).padStart(2,'0')).join('');
const C=globalThis.CodexFixCore, documentId=id();
let target=null, port=null, revision=0, pending=null, prepared=null, undo=null, timer=0, raf=0, observer=null, resize=null, containerObserver=null, composing=false, writing=false, observedText=null;
let connectionReady=false, handshakeId=null, handshakeTimer=0, manualRetry=null;
let config={enabled:true,prepare:false,shortcutModifiers:3,shortcutVirtualKey:188};
const htmlElement=tag=>document.createElementNS(HTML_NAMESPACE,tag);
const host=htmlElement('div');
host.id='codex-language-fix-overlay';
host.style.cssText='position:fixed;z-index:2147483647;display:none;width:28px;height:28px';
const shadow=host.attachShadow({mode:'open'});
const overlayStyle=htmlElement('style');
overlayStyle.textContent=':host{all:initial}button{font:13px system-ui;width:28px;height:28px;border:1px solid #777;border-radius:7px;background:#fff;color:#222;cursor:pointer;padding:0}button:disabled{opacity:.6}span{position:absolute;right:0;top:32px;background:#fff;color:#222;border:1px solid #888;padding:5px;font:12px system-ui;max-width:220px;width:max-content}';
const button=htmlElement('button');
button.type='button';button.setAttribute('aria-label','Text korrigieren');button.textContent='Aa';
const status=htmlElement('span');status.hidden=true;
const originalButton=htmlElement('button');
originalButton.id='original';originalButton.type='button';originalButton.hidden=true;
originalButton.style.cssText='position:absolute;right:0;top:60px;width:auto';originalButton.textContent='Original anzeigen';
const originalText=htmlElement('textarea');
originalText.id='originalText';originalText.readOnly=true;originalText.hidden=true;
originalText.style.cssText='position:absolute;right:0;top:94px;width:220px;height:150px';
shadow.append(overlayStyle,button,status,originalButton,originalText);
originalButton.addEventListener('mousedown',e=>e.preventDefault());originalButton.addEventListener('click',()=>{if(undo?.recovery){originalText.value=undo.original;originalText.hidden=false;}});
button.addEventListener('mousedown',e=>e.preventDefault());button.addEventListener('click',()=>undo?revert():correct());
function text(el){return el instanceof HTMLInputElement||el instanceof HTMLTextAreaElement?el.value:el.innerText;}
function eligible(el){if(!(el instanceof HTMLElement))return false;if(el.closest('pre,code,.cm-editor,.cm-content,.CodeMirror,.monaco-editor,.ace_editor,[role=code]'))return false;const form=el.closest('form');if(form?.querySelector('input[type=password],input[autocomplete=username],input[autocomplete=current-password],input[autocomplete=new-password],input[autocomplete=one-time-code]'))return false;const input=el.matches('input,textarea');if(!input&&!el.isContentEditable)return false;return C.eligibleMetadata({type:input?el.type:'contenteditable',disabled:el.disabled,readOnly:el.readOnly,password:el.type==='password',name:el.name,id:el.id,autocomplete:el.autocomplete,label:[el.getAttribute('aria-label'),...Array.from(el.labels||[],label=>label.textContent),...((el.getAttribute('aria-labelledby')||'').split(/\s+/).filter(Boolean).map(id=>el.getRootNode().getElementById?.(id)?.textContent||''))].join(' '),role:el.getAttribute('role')})&&!el.closest('[contenteditable="false"]');}
function parentOf(e){return e?.parentElement||e?.getRootNode()?.host||null;}function containsComposed(ancestor,e){for(let n=e;n;n=parentOf(n))if(n===ancestor)return true;return false;}
function focused(){let e=document.activeElement;while(e?.shadowRoot?.activeElement)e=e.shadowRoot.activeElement;return e;}
function valid(){return target&&target.isConnected&&!document.hidden&&document.hasFocus()&&(focused()===target||target.contains(focused()))&&eligible(target);}
function envelope(type,value){return {version:1,type,requestId:id(),documentId,frameId:0,fieldId:target?.dataset.codexFixField||'',revision,...(value===undefined?{}:{text:value})};}
function send(type,value){
 if(!port || (!connectionReady && type!=='state' && type!=='cancel')){message('Desktop-App nicht erreichbar. Zum erneuten Verbinden Aa anklicken.');return null;}
 const m=envelope(type,value);
 try{port.postMessage(m);return m;}catch{disconnect('Desktop-App nicht erreichbar. Zum erneuten Verbinden Aa anklicken.');return null;}
}
function message(s){status.textContent=s;status.hidden=!s;}
function disconnect(reason){
 clearTimeout(handshakeTimer);handshakeTimer=0;connectionReady=false;handshakeId=null;manualRetry=null;pending=null;prepared=null;button.disabled=false;
 const previous=port;port=null;try{previous?.disconnect();}catch{}
 if(reason)message(reason);
}
function connect(retry=false){
 if(retry&&valid())manualRetry={fieldId:target.dataset.codexFixField,revision,text:text(target)};
 if(port)return;
 try{
  port=chrome.runtime.connect({name:'editor'});connectionReady=false;const connectedPort=port;
  port.onMessage.addListener(m=>{if(port===connectedPort)receive(m);});
  port.onDisconnect.addListener(()=>{if(port===connectedPort)disconnect('Desktop-App nicht erreichbar. Zum erneuten Verbinden Aa anklicken.');});
  const request=send('state');if(!request)return;handshakeId=request.requestId;
  handshakeTimer=setTimeout(()=>{if(port===connectedPort&&!connectionReady)disconnect('Desktop-Verbindung antwortet nicht. Zum Wiederholen Aa anklicken.');},5000);
 }catch{disconnect('Desktop-App nicht erreichbar. Zum erneuten Verbinden Aa anklicken.');}
}
function detach(){clearTimeout(timer);timer=0;if(port)send('cancel');disconnect();target=null;pending=null;prepared=null;undo=null;originalButton.hidden=true;originalText.hidden=true;originalText.value='';observer?.disconnect();resize?.disconnect();containerObserver?.disconnect();containerObserver=null;observer=null;resize=null;host.style.display='none';message('');button.textContent='Aa';button.disabled=false;}
function activate(el){if(el?.isContentEditable){while(el.parentElement?.isContentEditable)el=el.parentElement;}if(el===target){if(valid()&&!port)connect();schedule();return;}detach();if(!eligible(el)||!document.hasFocus())return;target=el;observedText=text(el);target.dataset.codexFixField ||= id();revision=0;document.documentElement.append(host);observer=new MutationObserver(changed);observer.observe(target,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['style','class','hidden','disabled','readonly']});resize=new ResizeObserver(schedule);resize.observe(target);containerObserver=new MutationObserver(schedule);for(let ancestor=parentOf(target),depth=0;ancestor&&depth<16;ancestor=parentOf(ancestor),depth++){resize.observe(ancestor);containerObserver.observe(ancestor,{childList:true,attributes:true,attributeFilter:['style','class','hidden']});}connect();schedule();queuePreparation();}
function changed(){if(!target||writing)return;const next=text(target);if(next===observedText){schedule();return;}observedText=next;revision++;prepared=null;undo=null;originalButton.hidden=true;originalText.hidden=true;originalText.value='';button.textContent='Aa';if(pending){send('cancel');pending=null;button.disabled=false;}message('');schedule();queuePreparation();}
function queuePreparation(){clearTimeout(timer);if(connectionReady&&config.enabled&&config.prepare&&!composing&&valid())timer=setTimeout(()=>{if(valid()&&!pending&&text(target).trim()&&text(target).length<=20000){const request=send('prepare',text(target));if(request)pending={...request,original:text(target),manual:false};}},1200);}
function schedule(){if(!raf)raf=requestAnimationFrame(()=>{raf=0;place();});}
function clipToAncestors(bounds,start){
 const clipped={...bounds};
 for(let parent=start;parent;parent=parentOf(parent)){
  const style=getComputedStyle(parent),r=parent.getBoundingClientRect();
  if(/hidden|clip|scroll|auto/.test(style.overflowX)){clipped.left=Math.max(clipped.left,r.left);clipped.right=Math.min(clipped.right,r.right);}
  if(/hidden|clip|scroll|auto/.test(style.overflowY)){clipped.top=Math.max(clipped.top,r.top);clipped.bottom=Math.min(clipped.bottom,r.bottom);}
 }
 return clipped;
}
function whatsappComposer(){
 return location.hostname==='web.whatsapp.com'&&target?.matches('[data-testid="conversation-compose-box-input"][data-lexical-editor="true"][contenteditable="true"][role="textbox"]')?target.closest('footer'):null;
}
function whatsappPlacement(field,viewport){
 const composer=whatsappComposer();if(!composer)return null;
 const bounds=composer.getBoundingClientRect(),limits=clipToAncestors(viewport,composer);
 const occupied=[];let nodes=0,targetRectCount=0;
 const walker=document.createTreeWalker(composer,NodeFilter.SHOW_TEXT);let node;
 while(node=walker.nextNode()){
  if(++nodes>512)return null;
  if(!node.data.trim())continue;
  const parent=node.parentElement,style=parent&&getComputedStyle(parent);
  if(!parent||style.display==='none'||style.visibility!=='visible')continue;
  const range=document.createRange();range.selectNodeContents(node);
  for(const r of range.getClientRects())if(r.width>0&&r.height>0&&[r.left,r.top,r.right,r.bottom].every(Number.isFinite)){occupied.push(r);if(target.contains(node))targetRectCount++;}
 }
 if(text(target).trim()&&!targetRectCount)return null;
 for(const control of composer.querySelectorAll('button,[role="button"],input,textarea,[contenteditable="true"],img,svg,video,canvas,a')){
  if(control===target)continue;
  const r=control.getBoundingClientRect();if(r.width>0&&r.height>0)occupied.push(r);
 }
 const point=C.innerPosition(field,bounds,limits,occupied);
 return point?{point,composer}:null;
}
function hitTest(point,composer=null){
 for(const [x,y] of [[point.x+1,point.y+1],[point.x+27,point.y+1],[point.x+1,point.y+27],[point.x+27,point.y+27]]){
  const under=document.elementFromPoint(x,y);
  if(!under)return false;
  if(composer){if(under!==composer&&!composer.contains(under))return false;}
  else if(under!==parentOf(target)&&!containsComposed(under,target))return false;
 }
 return true;
}
function place(){
 if(!valid()){if((focused()===host||focused()===originalText)&&!originalText.hidden)return;detach();return;}
 host.style.display='none';
 if(!config.enabled||(!text(target).trim()&&!undo?.recovery))return;
 const r=target.getBoundingClientRect(),v=visualViewport;
 const viewport={left:v?.offsetLeft||0,top:v?.offsetTop||0,right:(v?.offsetLeft||0)+(v?.width||innerWidth),bottom:(v?.offsetTop||0)+(v?.height||innerHeight)};
 if(r.bottom<=viewport.top||r.top>=viewport.bottom||r.right<=viewport.left||r.left>=viewport.right)return;
 const cover=document.elementFromPoint(Math.max(viewport.left,Math.min(r.right-1,r.left+2)),Math.max(viewport.top,Math.min(r.bottom-1,r.top+2)));
 if(cover!==target&&!target.contains(cover)&&!containsComposed(cover,target))return;
 const candidates=[];
 for(const e of target.parentElement?.children||[]){if(e!==target&&e!==host&&e instanceof HTMLElement){const b=e.getBoundingClientRect();if(b.width&&b.height)candidates.push(b);}}
 let point=C.position(r,clipToAncestors(viewport,parentOf(target)),candidates);
 if(!point||!hitTest(point)){
  const inside=whatsappPlacement(r,viewport);
  if(!inside||!hitTest(inside.point,inside.composer))return;
  point=inside.point;
 }
 host.style.left=point.x+'px';host.style.top=point.y+'px';host.style.display='block';
}
function correct(){if(!valid()||composing)return;if(!port||!connectionReady){connect(true);return;}if(!config.enabled)return;const original=text(target);if(!original.trim()||original.length>20000)return;if(prepared&&prepared.original===original){apply(prepared.text,original);return;}clearTimeout(timer);const request=send('correct',original);if(!request){button.disabled=false;return;}pending={...request,original,manual:true};button.disabled=true;message('Wird korrigiert …');}
function receive(m){if(m.version!==1)return;if(m.type==='state'){
 if(typeof m.enabled!=='boolean'||m.documentId!==documentId||m.fieldId!==target?.dataset.codexFixField)return;
 if(!connectionReady&&m.requestId!==handshakeId)return;
 clearTimeout(handshakeTimer);handshakeTimer=0;handshakeId=null;connectionReady=true;config=m;
 const retry=manualRetry;manualRetry=null;
 if(retry&&valid()&&retry.fieldId===target.dataset.codexFixField&&retry.revision===revision&&retry.text===text(target)&&config.enabled){correct();return;}
if(!config.enabled){host.style.display='none';clearTimeout(timer);if(pending)send('cancel');pending=null;prepared=null;button.disabled=false;}else{schedule();queuePreparation();}return;}if(m.type==='error'&&!connectionReady){disconnect(m.message||m.error||'Desktop-Verbindung fehlgeschlagen');return;}if(!config.enabled||composing)return;if(m.type==='error'){if(m.requestId&&(!pending||m.requestId!==pending.requestId))return;pending=null;button.disabled=false;message(m.message||m.error||'Korrektur nicht verfügbar');return;}if(!pending||m.requestId!==pending.requestId||m.documentId!==pending.documentId||m.fieldId!==pending.fieldId||m.revision!==revision||!valid()||text(target)!==pending.original)return;const job=pending;pending=null;button.disabled=false;if(m.type==='result'&&typeof m.text==='string'&&m.text.length<=40000){if(job.manual)apply(m.text,job.original);else{prepared={original:job.original,text:m.text};message('Korrektur vorbereitet');}}}
function replace(expected,replacement,allowRollback=true){if(!valid()||!config.enabled||composing||text(target)!==expected)return false;const el=target,patch=C.edit(expected,replacement);if(patch.start===patch.end&&!patch.text)return true;if(el instanceof HTMLInputElement||el instanceof HTMLTextAreaElement){const setter=Object.getOwnPropertyDescriptor(el instanceof HTMLInputElement?HTMLInputElement.prototype:HTMLTextAreaElement.prototype,'value').set;setter.call(el,replacement);el.dispatchEvent(new Event('input',{bubbles:true,composed:true}));return text(el)===replacement;}
// Preflight every minimal edit before changing any editor-managed DOM.
// The verified WhatsApp managed editor must receive one input transaction.
// The same-node preflight below rejects a contiguous edit across formatting boundaries.
const patches=whatsappComposer()?[patch]:C.edits(expected,replacement);
function locate(value,p){const walker=document.createTreeWalker(el,NodeFilter.SHOW_TEXT);let node,offset=0;while(node=walker.nextNode()){if(!node.data)continue;offset=value.indexOf(node.data,offset);if(offset<0)return null;if(p.start>=offset&&p.end<=offset+node.length)return {node,start:p.start-offset,end:p.end-offset};offset+=node.length;}return null;}
if(patches.some(p=>!locate(expected,p)))return false;
let current=expected;
for(const p of patches.slice().reverse()){
 const location=locate(current,p);if(!location||text(el)!==current){if(allowRollback&&current!==expected&&text(el)===current)replace(current,expected,false);return false;}
 const selection=getSelection(),range=document.createRange();range.setStart(location.node,location.start);range.setEnd(location.node,location.end);selection.removeAllRanges();selection.addRange(range);
 if(selection.toString()!==current.slice(p.start,p.end)){if(allowRollback&&current!==expected&&text(el)===current)replace(current,expected,false);return false;}
 const next=current.slice(0,p.start)+p.text+current.slice(p.end);
 document.execCommand('insertText',false,p.text);
 if(text(el)!==next){if(allowRollback&&current!==expected&&text(el)===current)replace(current,expected,false);return false;}
 current=next;
}
return text(el)===replacement;
}function retainRecovery(original,element){if(target!==element||!valid()||text(target)===original)return false;undo={element,original,corrected:text(target),recovery:true};observedText=text(target);button.textContent='↶';button.title='Original wiederherstellen';originalButton.hidden=false;message('Änderung nicht bestätigt. Original wiederherstellen oder anzeigen.');return true;}
function apply(replacement,original){const el=target;writing=true;let applied;try{applied=replace(original,replacement);}finally{writing=false;observer?.takeRecords();clearTimeout(timer);}if(!applied){if(!retainRecovery(original,el))message('Dieses Feld konnte nicht sicher geändert werden.');return;}observer?.takeRecords();observedText=text(target);revision++;prepared=null;undo={element:el,original,corrected:replacement};button.textContent='↶';button.title='Rückgängig';message('Korrigiert – Rückgängig mit ↶');schedule();}
function revert(){const u=undo;let applied=false;writing=true;try{if(u&&target===u.element)applied=replace(u.corrected,u.original);}finally{writing=false;observer?.takeRecords();clearTimeout(timer);}if(!applied){if(!u||!retainRecovery(u.original,u.element))message('Rückgängig nicht mehr möglich.');return;}observer?.takeRecords();observedText=text(target);revision++;undo=null;originalButton.hidden=true;originalText.hidden=true;originalText.value='';button.textContent='Aa';button.title='Text korrigieren';message('Rückgängig');schedule();}
function key(e){if(e.repeat||e.isComposing||composing||!valid()||!config.enabled||!text(target).trim())return;const altGraph=e.getModifierState('AltGraph');const modifiers=((e.altKey||altGraph)?1:0)|((e.ctrlKey||altGraph)?2:0)|(e.shiftKey?4:0)|(e.metaKey?8:0);if(modifiers===config.shortcutModifiers&&e.keyCode===config.shortcutVirtualKey){e.preventDefault();e.stopPropagation();correct();}}
document.addEventListener('focusin',e=>{if(e.composedPath().includes(host))return;activate(e.composedPath()[0]);},true);
document.addEventListener('focusout',()=>queueMicrotask(()=>{if(!valid()&&!((focused()===host||focused()===originalText)&&!originalText.hidden))detach();}),true);
document.addEventListener('input',e=>{if(target&&(e.composedPath().includes(target)||target.contains(e.target)))changed();},true);
document.addEventListener('compositionstart',()=>{composing=true;clearTimeout(timer);if(pending)send('cancel');pending=null;button.disabled=false;},true);document.addEventListener('compositionend',()=>{composing=false;changed();},true);
document.addEventListener('keydown',key,true);document.addEventListener('scroll',schedule,true);window.addEventListener('resize',schedule);visualViewport?.addEventListener('resize',schedule);visualViewport?.addEventListener('scroll',schedule);
window.addEventListener('blur',detach);window.addEventListener('focus',()=>activate(focused()));document.addEventListener('visibilitychange',()=>document.hidden?detach():activate(focused()));window.addEventListener('pagehide',detach);activate(focused());
})();
