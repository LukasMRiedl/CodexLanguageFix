const HOST='com.codexlanguagefix.chrome';
chrome.runtime.onConnect.addListener(port=>{
 if(port.name!=='editor'||port.sender?.id!==chrome.runtime.id||!port.sender.tab||!(/^https?:/.test(port.sender.url||'')||(/^about:(blank|srcdoc)$/.test(port.sender.url||'')&&/^https?:/.test(port.sender.origin||'')))){port.disconnect();return;}
 let native;
 try{native=chrome.runtime.connectNative(HOST);}catch{port.postMessage({version:1,type:'error',error:'Desktop-App nicht erreichbar'});return;}
 const send=m=>{if(!m||m.version!==1||!['state','prepare','correct','cancel'].includes(m.type)||JSON.stringify(m).length>262144)return;native.postMessage({...m,frameId:port.sender.frameId,documentId:m.documentId});};
 port.onMessage.addListener(send);
 native.onMessage.addListener(m=>{try{port.postMessage(m);}catch{}});
 native.onDisconnect.addListener(()=>{try{port.postMessage({version:1,type:'error',error:chrome.runtime.lastError?.message||'Desktop-Verbindung beendet'});port.disconnect();}catch{}});
 port.onDisconnect.addListener(()=>{native.disconnect();});
});
