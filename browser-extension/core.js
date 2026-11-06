(function(root){
  const forbidden = /password|passwd|passwort|kennwort|kartennummer|geheimzahl|api.?key|api.?token|access.?token|\btoken\b|secret|\bpin\b|credit.?card|kreditkarte|card.?number|security.?code|cvv|cvc|one.?time|otp|verification.?code|bestätigungscode|username|benutzername|iban|routing|terminal|command|befehl|code.?editor/i;
  function eligibleMetadata(m){return !m.disabled && !m.readOnly && !m.password && !forbidden.test([m.name,m.id,m.autocomplete,m.label,m.role].join(' ')) && !['password','email','url','number','tel','date','time','hidden','search'].includes(m.type);}
  function edit(original,replacement){let start=0;while(start<original.length&&start<replacement.length&&original[start]===replacement[start])start++;let end=original.length,newEnd=replacement.length;while(end>start&&newEnd>start&&original[end-1]===replacement[newEnd-1]){end--;newEnd--;}return {start,end,text:replacement.slice(start,newEnd)};}
  function position(field,viewport,occupied){const size=28,gap=4;const candidates=[{x:field.right+gap,y:field.bottom-size},{x:field.left-size-gap,y:field.bottom-size},{x:field.right-size,y:field.bottom+gap},{x:field.right-size,y:field.top-size-gap}];return candidates.find(p=>p.x>=viewport.left&&p.y>=viewport.top&&p.x+size<=viewport.right&&p.y+size<=viewport.bottom&&!occupied.some(r=>p.x<r.right+gap&&p.x+size>r.left-gap&&p.y<r.bottom+gap&&p.y+size>r.top-gap))||null;}
  function innerPosition(field,composer,viewport,occupied){
    const size=28,gap=4,left=Math.max(field.left,composer.left+gap,viewport.left),right=Math.min(field.right,composer.right-gap,viewport.right);
    const top=Math.max(composer.top+gap,viewport.top),bottom=Math.min(composer.bottom-gap,viewport.bottom);
    if(right-left<size||bottom-top<size)return null;
    const preferred=right-size-gap,y=Math.max(top,Math.min(field.top+(Math.min(field.bottom-field.top,size)-size)/2,bottom-size));
    const xs=[preferred,left+gap,...occupied.flatMap(r=>[r.left-size-gap,r.right+gap])].sort((a,b)=>Math.abs(a-preferred)-Math.abs(b-preferred));
    return xs.map(x=>({x,y})).find(p=>p.x>=left&&p.x+size<=right&&!occupied.some(r=>p.x<r.right+gap&&p.x+size>r.left-gap&&p.y<r.bottom+gap&&p.y+size>r.top-gap))||null;
  }
  function edits(a,b){
 const trace=[],limit=Math.min(128,a.length+b.length);let v=new Map([[1,0]]);
 for(let d=0;d<=limit;d++){trace.push(new Map(v));for(let k=-d;k<=d;k+=2){let x=(k===-d||(k!==d&&(v.get(k-1)??-1)<(v.get(k+1)??-1)))?(v.get(k+1)??0):(v.get(k-1)??0)+1;let y=x-k;while(x<a.length&&y<b.length&&a[x]===b[y]){x++;y++;}v.set(k,x);if(x>=a.length&&y>=b.length){let ax=a.length,by=b.length,ops=[];for(let depth=d;depth>=0;depth--){const prior=trace[depth],diagonal=ax-by,pk=(diagonal===-depth||(diagonal!==depth&&(prior.get(diagonal-1)??-1)<(prior.get(diagonal+1)??-1)))?diagonal+1:diagonal-1;const px=prior.get(pk)??0,py=px-pk;while(ax>px&&by>py){ops.push(['=',a[--ax]]);by--;}if(depth){if(ax===px)ops.push(['+',b[--by]]);else ops.push(['-',a[--ax]]);}}ops.reverse();const result=[];let at=0,current=null;for(const [op,c] of ops){if(op==='='){if(current){result.push(current);current=null;}at++;}else{current ||= {start:at,end:at,text:''};if(op==='-'){at++;current.end=at;}else current.text+=c;}}if(current)result.push(current);return result;}}}return [edit(a,b)];
}
 const api={eligibleMetadata,edit,edits,position,innerPosition};if(typeof module!=='undefined')module.exports=api;else root.CodexFixCore=api;
})(globalThis);
