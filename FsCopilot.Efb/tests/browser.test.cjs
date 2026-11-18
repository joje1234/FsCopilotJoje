// Optional real-browser checks. Requires Node with global WebSocket and Chrome/Edge.
const assert = require('node:assert/strict');
const withPairs = require('./status_fixture.cjs');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { pathToFileURL } = require('node:url');
const repo = path.resolve(__dirname, '../..');
const output = process.env.FSC_BROWSER_OUTPUT ? path.resolve(process.env.FSC_BROWSER_OUTPUT) : path.join(repo, 'artifacts/efb-control-test/ui-preview');
fs.mkdirSync(output, { recursive: true });
const bundle = fs.readFileSync(path.join(__dirname, '../PackageSources/FsCopilotControls/FsCopilotControls.js'), 'utf8').replace(/<\/script/gi, '<\\/script');
const css = fs.readFileSync(path.join(__dirname, '../src/controls.css'), 'utf8');
const html = `<!doctype html><html><head><meta charset="utf-8"><style>html,body,#app{height:100%;width:100%;margin:0} ${css}</style></head><body><main id="app"></main>
<script>window.FSC_CONTROLS_PREVIEW=true;</script><script>${bundle}</script><script>
var withPairs = ${withPairs.toString()};
window.fixturePackets=[];
window.fixtureController=new FscControls.Controller({send:function(packet){fixturePackets.push(packet)}},{client:'browser-test'});
FscControls.mount(document.getElementById('app'),fixtureController,{preview:true});
var fixtureRevision=0;
window.setFixture=function(mode){
 function axis(id){return {id:id,label:id,input:50,target:50,calibrated:true,fresh:true,aligned:true}}
 var steering=['roll','pitch','rudder','steering','leftBrake','rightBrake'].map(axis);
 var quadrant=['throttle1','propeller1','mixture1','throttle2','propeller2','mixture2'].map(axis);
 if(mode==='waiting'){quadrant[0].input=20;quadrant[0].target=70;quadrant[0].aligned=false;quadrant[5].calibrated=false;quadrant[5].input=null;quadrant[5].aligned=false;steering[0].input=65;steering[0].target=50;steering[0].aligned=false;steering[1].input=30;steering[1].target=55;steering[1].aligned=false}
 if(mode==='learning')steering.concat(quadrant).forEach(function(a){a.input=null;a.calibrated=false;a.aligned=false});
 if(mode==='four-engines'||mode==='advanced-four')for(var n=3;n<=4;n++)['throttle','propeller','mixture'].forEach(function(t){quadrant.push(axis(t+n))});
 var state=withPairs({version:5,session:'abc',revision:++fixtureRevision,client:'',request:0,ready:mode!=='error',applying:false,steeringBlocked:false,quadrantBlocked:['split','waiting'].includes(mode),error:mode==='error'?'Simulator connection lost. Controls unconfirmed; reload the package.':'',learningStorage:'ready',steering:{seen:60,blocked:0,last:'AXIS_AILERONS_SET',value:0},quadrant:{seen:80,blocked:20,last:'THROTTLE_AXIS_SET_EX1',value:0},pickup:{steering:{waiting:false,tolerance:10,axes:steering},quadrant:{waiting:mode==='waiting',tolerance:10,axes:quadrant}}});
 state.advanced=true;
 if(mode.startsWith('wizard-')){
  state.wizard={phase:mode==='wizard-input'?2:mode==='wizard-align'?5:7,group:'steering',needed:3,axis:mode==='wizard-failed'?5:21,ready:false,message:mode==='wizard-failed'?'The requested cockpit movement was not confirmed.':'',prerequisite:'',resetReady:false,resetReason:mode==='wizard-failed'?'Return all controls to Me before resetting setup.':'Finish or cancel the wizard before resetting setup.'};
  state.outputCheck={available:true,writable:true,phase:mode==='wizard-failed'?5:0,group:'steering',passed:0,saved:0,restored:mode==='wizard-failed',storage:'ready'};
  state.steeringBlocked=mode!=='wizard-input';
 }
 if(['advanced','auto-waiting','advanced-four','basic-auto'].includes(mode)){
  state.advanced=true;state.throttleBlocked=true;state.quadrantMixed=true;
  if(mode==='auto-waiting'||mode==='basic-auto'){
   state.autoMask=14;state.mixtureBlocked=true;
   state.pickup.quadrant.autoEnabled=true;
   ['throttle','propeller','mixture'].forEach(function(group){state.pickup[group].autoEnabled=true});
   ['throttle','mixture'].forEach(function(group){state.pickup[group].autoEnabled=true;state.pickup[group].waiting=true;state.pickup[group].axes[0].input=20;state.pickup[group].axes[0].aligned=false});
  }
 }
 if(mode.startsWith('shared-')){
  const pending=mode==='shared-pending',ended=mode==='shared-ended';
  state.advanced=true;state.shared={phase:ended?4:3,owners:[1,1,2,1],attached:pending?13:15,pending:pending?2:0,waiting:0,other:pending?2:0,holding:true};
  state.throttleBlocked=pending||ended;state.propellerBlocked=true;state.mixtureBlocked=ended;
  state.quadrantBlocked=ended;state.quadrantMixed=!ended;
 }
 if(mode==='medium'||mode==='loose')Object.values(state.pickup).forEach(function(group){group.tolerance=mode==='medium'?20:30});
 if(mode==='connection'){
  state.connection={api:1,revision:0,relay:'',port:0,local:'',peer:'',role:0,seat:0,backend:1,code:0};
  state.shared={phase:0,owners:[0,0,0,0],attached:0,pending:0,waiting:0,other:0,holding:false,needsRecovery:false};
 }
 if(mode==='custom-tolerance'){state.pickup.throttle.tolerance=state.pickup.quadrant.tolerance=20;state.quadrantToleranceMixed=true}
 if(mode==='monitor')state.cockpit={available:true,sharing:false,count:2,known:1,result:0,monitor:{profile:'abc',sequence:1,catalog:[{index:0,name:'A:AUTOPILOT HEADING LOCK DIR',units:'degrees',shared:true,value:170},{index:1,name:'L:HOST_SWITCH_WITH_A_LONG_NAME_FOR_NARROW_LAYOUTS',units:'Number',shared:false,value:null}],changes:[{index:0,sequence:1,before:100,value:170}]}};
 fixtureController.pending=null;fixtureController.receive(state);
 if(mode==='offline'){fixtureController.receivedAt=-Infinity;fixtureController.notify()}
 return true;
};setFixture('split');window.fixtureReady=true;
</script></body></html>`;
const fixture = path.join(output, 'fixture.html'); fs.writeFileSync(fixture, html);
const browserPath = process.env.FSC_BROWSER || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const profile = path.join(output, 'browser-profile-' + Date.now());
const child = spawn(browserPath, ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0', '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-component-update', '--disable-extensions', '--disable-sync', 'about:blank'], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
let browserErrors='';child.stderr.on('data',data=>{browserErrors=(browserErrors+data.toString()).slice(-4000)});
child.on('error',error=>{browserErrors=error.message});
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function main() {
 let socket;
 try {
  const portFile = path.join(profile, 'DevToolsActivePort');
  for(let i=0; i<100 && !fs.existsSync(portFile); i++) await delay(100);
  assert(fs.existsSync(portFile), 'Browser did not expose local debugging port');
  const port = fs.readFileSync(portFile, 'utf8').split('\n')[0].trim();
  const page = await (await fetch('http://127.0.0.1:' + port + '/json/new?about:blank', { method:'PUT' })).json();
  socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true})});
  let sequence=0;const pending=new Map();
  socket.addEventListener('message',event=>{const msg=JSON.parse(event.data);if(!msg.id)return;const wait=pending.get(msg.id);if(!wait)return;pending.delete(msg.id);msg.error?wait.reject(new Error(JSON.stringify(msg.error))):wait.resolve(msg.result)});
  socket.addEventListener('close',()=>{for(const wait of pending.values())wait.reject(new Error('Browser debugger closed: '+browserErrors));pending.clear()});
  const send=(method,params={})=>new Promise((resolve,reject)=>{
   const id=++sequence;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('Browser command timed out: '+method+'; '+browserErrors))},10000);
   pending.set(id,{resolve:value=>{clearTimeout(timer);resolve(value)},reject:error=>{clearTimeout(timer);reject(error)}});
   socket.send(JSON.stringify({id,method,params}));
  });
  const evaluate=async expression=>{const value=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(value.exceptionDetails)throw new Error(JSON.stringify(value.exceptionDetails));return value.result.value};
  await send('Page.enable'); await send('Runtime.enable');
  await send('Page.navigate',{url:pathToFileURL(fixture).href});
  for(let i=0;i<100 && !await evaluate('!!window.fixtureReady');i++)await delay(50);
  assert(await evaluate('!!window.fixtureReady'),'Fixture did not mount');
  const reports=[];
  for(const [width,height] of [[600,600],[760,700],[1024,600],[420,560]]){
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   for(const mode of ['split','waiting','learning','offline','error','four-engines','advanced','auto-waiting','advanced-four','basic-auto','medium','loose','custom-tolerance','shared-owned','shared-pending','shared-ended']){
    await evaluate('setFixture('+JSON.stringify(mode)+')');
    const layout=await evaluate(`(()=>{const root=document.querySelector('.fsc-controls'),footer=document.querySelector('.fsc-footer'),r=footer.getBoundingClientRect();return {width:innerWidth,height:innerHeight,scrollWidth:document.documentElement.scrollWidth,scrollHeight:document.documentElement.scrollHeight,rootHeight:root.clientHeight,rootScroll:root.scrollHeight,footerBottom:r.bottom,footerRight:r.right}})()`);
    assert(layout.scrollWidth<=width+1 && layout.scrollHeight<=height+1 && layout.rootScroll<=layout.rootHeight+1 && layout.footerBottom<=height+1 && layout.footerRight<=width+1, JSON.stringify({mode,layout}));
    const actionOverlaps=await evaluate(`(()=>{const labels=[...document.querySelectorAll('.fsc-group-label,.fsc-group-owner,.fsc-group-summary')];return [...document.querySelectorAll('[data-menu-for]')].flatMap(button=>{const b=button.getBoundingClientRect();return labels.filter(label=>{const r=label.getBoundingClientRect();return b.left<r.right && b.right>r.left && b.top<r.bottom && b.bottom>r.top}).map(label=>button.getAttribute('data-menu-for')+': '+label.textContent)})})()`);
    assert.deepEqual(actionOverlaps,[],'Actions icons obscure group labels: '+JSON.stringify({mode,width,height,actionOverlaps}));
    reports.push({mode,...layout});
    if(width===760 && ['split','waiting','offline','advanced','auto-waiting','basic-auto','medium','loose'].includes(mode)){
     const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
     fs.writeFileSync(path.join(output,'cockpit-'+mode+'.png'),Buffer.from(shot.data,'base64'));
    }
   }
  }
  await send('Emulation.setDeviceMetricsOverride',{width:760,height:700,deviceScaleFactor:1,mobile:false});
  await evaluate("setFixture('split')");
  const colors=await evaluate(`(()=>{const get=s=>getComputedStyle(document.querySelector(s));return {meSeat:get('[data-seat-choice="me"] rect').fill,mockSeat:get('[data-seat-choice="mock"] rect').fill,meOwner:get('[data-control="steering"] .fsc-group-owner').fill,steering:get('[data-control="steering"] .fsc-yoke-base').stroke,quadrant:get('.fsc-quadrant-shell').stroke,shell:get('.fsc-shell').stroke}})()`);
  assert.equal(colors.meOwner,colors.steering);assert.equal(colors.shell,colors.quadrant);assert.notEqual(colors.meSeat,colors.mockSeat);
  async function clickPoint(x,y,button='left'){
   const point=await evaluate(`(()=>{const s=document.querySelector('[data-cockpit]'),p=s.createSVGPoint();p.x=${x};p.y=${y};const t=p.matrixTransform(s.getScreenCTM());return {x:t.x,y:t.y}})()`);
   await send('Input.dispatchMouseEvent',{type:'mousePressed',button,clickCount:1,...point});
   await send('Input.dispatchMouseEvent',{type:'mouseReleased',button,clickCount:1,...point});
  }
  await clickPoint(230,538); // Choose Me.
  await clickPoint(410,346); // Actual lever hit area.
  assert.equal(await evaluate('fixturePackets[0]'),'5|browser-test|1|set|throttle|me');
  await evaluate("setFixture('split')");
  await clickPoint(670,538); // Choose mock.
  await clickPoint(230,364); // Drawn left yoke.
  assert.equal(await evaluate('fixturePackets[1]'),'5|browser-test|2|set|steering|mock');
  await evaluate("setFixture('split');document.querySelector('[data-settings]').click()");
  assert.equal(await evaluate("document.querySelector('[data-setup]').hidden"),false);
  await send('Emulation.setDeviceMetricsOverride',{width:600,height:600,deviceScaleFactor:1,mobile:false});
  const setupBefore=await evaluate(`(()=>{const s=document.querySelector('[data-setup-scroll]'),r=s.getBoundingClientRect(),h=document.querySelector('.fsc-setup-header').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2,height:s.clientHeight,total:s.scrollHeight,headerTop:h.top,headerBottom:h.bottom}})()`);
  assert(setupBefore.total>setupBefore.height,'Settings fixture must overflow its bounded body');
  await send('Input.dispatchMouseEvent',{type:'mouseWheel',x:setupBefore.x,y:setupBefore.y,deltaX:0,deltaY:700});
  await new Promise(resolve=>setTimeout(resolve,80));
  const setupAfter=await evaluate(`(()=>{const s=document.querySelector('[data-setup-scroll]'),h=document.querySelector('.fsc-setup-header').getBoundingClientRect(),q=document.querySelector('[data-tolerance="quadrant"]').getBoundingClientRect();return {scroll:s.scrollTop,headerTop:h.top,headerBottom:h.bottom,quadrantTop:q.top,quadrantBottom:q.bottom}})()`);
  assert(setupAfter.scroll>0,'Mouse wheel did not scroll Setup');
  assert.equal(setupAfter.headerTop,setupBefore.headerTop,'Setup header scrolled out of reach');
  await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=document.querySelector('[data-setup-scroll]').scrollHeight");
  const bottom=await evaluate(`(()=>{const s=document.querySelector('[data-setup-scroll]'),r=s.getBoundingClientRect(),m=document.querySelector('[data-axis-list="mixture"] [data-axis="mixture2"]').getBoundingClientRect();return {scroll:s.scrollTop,top:r.top,bottom:r.bottom,mixtureTop:m.top,mixtureBottom:m.bottom}})()`);
  assert(bottom.mixtureTop>=bottom.top && bottom.mixtureBottom<=bottom.bottom+1,'Bottom quadrant diagnostics are clipped/unreachable');
  await evaluate("setFixture('split')");
  assert.equal(await evaluate("document.querySelector('[data-setup-scroll]').scrollTop"),bottom.scroll,'Status refresh reset Setup scroll position');
  await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=0");
  await send('Emulation.setTouchEmulationEnabled',{enabled:true});
  await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:setupBefore.x,y:setupBefore.y+80}]});
  await send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:setupBefore.x,y:setupBefore.y-80}]});
  await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  assert(await evaluate("document.querySelector('[data-setup-scroll]').scrollTop")>100,'Touch drag did not scroll Setup');
  await send('Emulation.setTouchEmulationEnabled',{enabled:false});
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
  assert.equal(await evaluate("document.querySelector('[data-setup]').hidden"),true);
  await evaluate("document.getElementById('app').style.cssText='box-sizing:border-box;padding:24px 0 56px'");
  const safeInsets=[];
  for(const [width,height] of [[600,600],[760,700],[1024,600],[420,560]]){
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   for(const mode of ['waiting','auto-waiting','basic-auto','medium','loose','custom-tolerance','shared-owned','shared-pending','shared-ended']){
   await evaluate('setFixture('+JSON.stringify(mode)+')');
   const layout=await evaluate(`(()=>{const r=document.querySelector('.fsc-controls'),f=document.querySelector('.fsc-footer').getBoundingClientRect();return {scroll:r.scrollHeight,height:r.clientHeight,top:r.getBoundingClientRect().top,bottom:f.bottom}})()`);
   assert(layout.scroll<=layout.height+1 && layout.top>=24 && layout.bottom<=height-56+1,JSON.stringify({width,height,layout}));
   safeInsets.push({mode,width,height,...layout});
   }
  }
  assert.equal(await evaluate("!!document.querySelector('[data-restore]')"),false);
  // Simulate the embedded UI omitting contextmenu, while keeping real mouse events.
  await evaluate("window.blockContext=e=>{e.preventDefault();e.stopImmediatePropagation()};document.addEventListener('contextmenu',blockContext,true)");
  for(const [group,x,y] of [['throttle',410,346],['steering',230,364]]){
   await evaluate("setFixture('split')");
   const count=await evaluate('fixturePackets.length');
   await clickPoint(x,y,'right');
   assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),false,'Right mouse button without contextmenu did not open the menu');
   assert.equal(await evaluate('fixturePackets.length'),count,'Right-click changed ownership');
   const menu=await evaluate(`(()=>{const m=document.querySelector('[data-control-menu]'),r=m.getBoundingClientRect(),c=document.querySelector('.fsc-controls').getBoundingClientRect(),b=m.querySelector('button').getBoundingClientRect();return {label:m.getAttribute('aria-label'),left:r.left,top:r.top,right:r.right,bottom:r.bottom,container:c.toJSON(),x:b.x+b.width/2,y:b.y+b.height/2}})()`);
   assert(menu.left>=menu.container.left && menu.right<=menu.container.right && menu.top>=menu.container.top && menu.bottom<=menu.container.bottom,'Menu clipped by EFB');
   await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x:menu.x,y:menu.y});
   await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x:menu.x,y:menu.y});
   assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'force|'+group+'|me');
   assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),true);
  }
  await evaluate("document.removeEventListener('contextmenu',blockContext,true)");
  assert(await evaluate('[...document.querySelectorAll(".fsc-group-actions")].every(e=>getComputedStyle(e).display==="none")'),'Obsolete action buttons remain visible');
  await evaluate("setFixture('split');document.querySelector('[data-control=steering]').focus()");
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'F10',code:'F10',modifiers:8});
  assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),false);
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
  assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),true);
  // Small EFB viewport: real clicks must target pairs separately, and the
  // existing visible quadrant menu must provide each pair's Auto switch.
  await evaluate("setFixture('advanced')");
  await clickPoint(670,538);
  await clickPoint(499,346);
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'set|mixture|mock');
  await evaluate("setFixture('advanced')");
  await clickPoint(499,346,'right');
  const advancedMenu=await evaluate(`(()=>{const m=document.querySelector('[data-control-menu]'),r=m.getBoundingClientRect(),c=document.querySelector('.fsc-controls').getBoundingClientRect();return {left:r.left,top:r.top,right:r.right,bottom:r.bottom,container:c.toJSON(),simpleDisabled:document.querySelector('[data-advanced]').disabled}})()`);
  assert(advancedMenu.left>=advancedMenu.container.left && advancedMenu.right<=advancedMenu.container.right && advancedMenu.top>=advancedMenu.container.top && advancedMenu.bottom<=advancedMenu.container.bottom,'Advanced menu clipped');
  assert(await evaluate("document.querySelector('[data-advanced]').hidden"),'Simple quadrant view must not be offered');
  async function clickSelector(selector){
   const point=await evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
   await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...point});
   await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...point});
  }
  assert(await evaluate('[...document.querySelectorAll("[data-menu-pair]")].every(e=>e.hidden)'),'Per-pair actions remain exposed');
  assert.equal(await evaluate("document.querySelector('[data-menu-title]').textContent"),'Mixture');
  assert.equal(await evaluate("document.querySelector('[data-auto]').hidden"),true,'Per-pair Auto setting remains exposed');
  await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
  await evaluate('fixtureController.setAuto(true)'); // Internal API remains for wizard policy changes.
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'auto|quadrant|on');
  await evaluate("setFixture('basic-auto')");
  assert.equal(await evaluate("!!document.querySelector('[data-control=throttle]')"),true,'Individual engine claims must always remain available');
  assert.equal(await evaluate("document.querySelector('[data-pair-owner=throttle]').getAttribute('visibility')"),'visible');
  await clickPoint(499,346,'right');
  assert.equal(await evaluate("document.querySelector('[data-auto]').hidden"),true);
  await evaluate('fixtureController.setAuto(false)');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'auto|quadrant|off');
  await evaluate("setFixture('auto-waiting');document.querySelector('[data-settings]').click()");
  await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=10000");
  assert(await evaluate("document.querySelector('[data-tolerance=mixture]').getBoundingClientRect().bottom<document.querySelector('[data-setup]').getBoundingClientRect().bottom"),'Advanced mixture setup unreachable');
  await evaluate("document.querySelector('[data-close-settings]').click()");
  await evaluate("setFixture('split')");
  for(const [percent,name] of [[20,'Medium'],[30,'Loose'],[10,'Tight']]){
   await clickSelector('[data-cycle-tolerance]');
   assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'tolerance|all|'+percent);
   assert.equal(await evaluate("document.querySelector('[data-cycle-tolerance]').disabled"),true);
   await evaluate(`(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.revision=++fixtureRevision;next.client='browser-test';next.request=Number(fixturePackets.at(-1).split('|')[2]);Object.values(next.pickup).forEach(group=>group.tolerance=${percent});fixtureController.receive(next)})()`);
   assert.equal(await evaluate("document.querySelector('[data-cycle-tolerance]').textContent"),'Tolerance: '+name);
   assert.equal(await evaluate('fixtureController.snapshot.quadrantBlocked'),true,'Preset changed quadrant ownership');
  }
  await evaluate("setFixture('shared-pending')");
  assert.equal(await evaluate("document.querySelector('[data-pair-group=throttle]').classList.contains('fsc-owner-me')"),true);
  assert.equal(await evaluate("document.querySelector('[data-pair-group=throttle]').classList.contains('fsc-waiting')"),true);
  await clickSelector('[data-settings]');
  await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=0");
  await clickSelector('[data-shared-session]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'leave');
  await clickSelector('[data-close-settings]');
  await evaluate("setFixture('shared-owned');document.querySelector('[data-settings]').click()");
  await evaluate("(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.shared.phase=0;fixtureController.receive(next)})()");
  await clickSelector('[data-shared-session]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'join');
  const validationLayouts=[];
  for(const [width,height] of [[760,700],[420,560]]) {
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   for(const mask of [0,2,15]) {
    await evaluate(`(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.revision=++fixtureRevision;next.outputCheck={available:true,writable:true,phase:0,group:'steering',passed:${mask},saved:${mask},storage:'ready',restored:false};fixtureController.receive(next);document.querySelector('[data-setup-scroll]').scrollTop=0})()`);
    const result=await evaluate("(()=>{const node=document.querySelector('[data-output-check]'),r=node.getBoundingClientRect(),pane=document.querySelector('[data-setup-scroll]');return {text:node.textContent,bodyWidth:document.documentElement.scrollWidth,paneWidth:pane.clientWidth,paneScroll:pane.scrollWidth,left:r.left,right:r.right}})()");
    assert(result.bodyWidth<=width+1 && result.paneScroll<=result.paneWidth+1 && result.left>=0 && result.right<=width+1,'Output validation text overflows: '+JSON.stringify(result));
    assert(result.text.includes(mask===15?'Complete and saved for this aircraft.':mask===2?'Still needed: Steering, RPM, Mixture.':'Still needed: Steering, Throttle, RPM, Mixture.'));
    validationLayouts.push({width,height,mask,...result});
    if(mask===2){const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});fs.writeFileSync(path.join(output,'output-validation-'+width+'x'+height+'.png'),Buffer.from(shot.data,'base64'))}
   }
  }
  fs.writeFileSync(path.join(output,'output-validation-layouts.json'),JSON.stringify(validationLayouts,null,2));
  for(const [width,height] of [[360,640],[700,500],[1024,768]]){
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   await evaluate("setFixture('monitor');document.querySelector('[data-settings]').click();if(document.querySelector('[data-monitor-body]').hidden)document.querySelector('[data-monitor-action=open]').click();document.querySelector('[data-monitor-filter]').value='HOST';document.querySelector('[data-monitor-filter]').dispatchEvent(new Event('input',{bubbles:true}))");
   const result=await evaluate("(()=>{const p=document.querySelector('[data-instrument-monitor]'),pane=document.querySelector('[data-setup-scroll]');return {text:p.innerText,width:p.clientWidth,scroll:p.scrollWidth,paneWidth:pane.clientWidth,paneScroll:pane.scrollWidth}})()");
   assert(result.scroll<=result.width+1&&result.paneScroll<=result.paneWidth+1,JSON.stringify({width,height,result}));
   assert(result.text.includes('Covered')&&result.text.includes('Host → copilot')&&result.text.includes('Readout unavailable'),JSON.stringify({width,height,result}));
   if(width===700){await evaluate("document.querySelector('[data-instrument-monitor]').scrollIntoView({block:'start'})");const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});fs.writeFileSync(path.join(output,'instrument-monitor.png'),Buffer.from(shot.data,'base64'));}
  }
  const wizardLayouts=[];
  for(const [width,height] of [[360,640],[700,500],[1024,768]]) for(const mode of ['wizard-input','wizard-align','wizard-failed']){
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   await evaluate("setFixture('"+mode+"');document.querySelector('[data-settings]').click();document.querySelector('[data-setup-scroll]').scrollTop=0");
   const result=await evaluate("(()=>{const p=document.querySelector('[data-wizard]'),pane=document.querySelector('[data-setup-scroll]');return {text:p.innerText,width:p.clientWidth,scroll:p.scrollWidth,paneWidth:pane.clientWidth,paneScroll:pane.scrollWidth,bodyWidth:document.documentElement.scrollWidth}})()");
   assert(result.scroll<=result.width+1 && result.paneScroll<=result.paneWidth+1 && result.bodyWidth<=width+1,'Wizard layout overflows: '+JSON.stringify(result));
   assert(result.text.includes(mode==='wizard-input'?'fixed simulator mappings; no manual learning or matching':mode==='wizard-align'?'Align yoke roll and pitch':'requested cockpit movement'));
   wizardLayouts.push({width,height,mode,...result});
   if(mode==='wizard-align'){const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});fs.writeFileSync(path.join(output,'wizard-'+width+'x'+height+'.png'),Buffer.from(shot.data,'base64'))}
  }
  fs.writeFileSync(path.join(output,'wizard-layouts.json'),JSON.stringify(wizardLayouts,null,2));
  await evaluate("setFixture('connection');document.querySelector('[data-settings]').click();document.querySelector('[data-setup-scroll]').scrollTop=0");
  async function typeField(key,value){
   await evaluate("fixtureController.receive(JSON.parse(JSON.stringify(fixtureController.snapshot)))");
   const selector='[data-connection-field="'+key+'"]';
   await evaluate(`document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'center'})`);
   await clickSelector(selector);
   await evaluate(`document.querySelector(${JSON.stringify(selector)}).select()`);
   await send('Input.insertText',{text:value});
  }
  await typeField('local','pilot001');await typeField('peer','copil001');
  const beforeChoices=await evaluate('fixturePackets.length');
  await evaluate("document.querySelector('[data-connection-choice=role]').scrollIntoView({block:'center'})");
  await clickSelector('[data-connection-choice="role"][data-choice-value="2"]');
  assert.equal(await evaluate("document.querySelector('[data-connection-field=role]').value"),'2');
  await clickSelector('[data-connection-choice="role"][data-choice-value="1"]');
  await evaluate("document.querySelector('[data-connection-choice=seat]').scrollIntoView({block:'center'})");
  await clickSelector('[data-connection-choice="seat"][data-choice-value="2"]');
  assert.equal(await evaluate("document.querySelector('[data-connection-choice=seat][data-choice-value=\"2\"]').getAttribute('aria-pressed')"),'true');
  assert.equal(await evaluate('fixturePackets.length'),beforeChoices,'Choosing role/seat sent a simulator command before Save');
  await evaluate("document.querySelector('.fsc-shared-settings details').open=true");
  await typeField('relay','server.local');
  assert.equal(await evaluate("document.querySelector('[data-save-connection]').disabled"),false,'Real EFB connection inputs did not enable Save');
  await evaluate("fixtureController.notify()");
  assert.equal(await evaluate("document.querySelector('[data-connection-field=relay]').value"),'server.local','Live display refresh overwrote a connection draft');
  assert.equal(await evaluate("document.querySelector('[data-connection-field=seat]').value"),'2','Live display refresh overwrote a clicked seat');
  await evaluate("document.querySelector('[data-save-connection]').scrollIntoView({block:'center'})");
  await clickSelector('[data-save-connection]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'connect|server.local|3600|pilot001|copil001|host|copilot');
  assert.equal(await evaluate('fixtureController.snapshot.shared.phase'),0,'Save implicitly joined');
  await evaluate("(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.revision=++fixtureRevision;next.client='browser-test';next.request=Number(fixturePackets.at(-1).split('|')[2]);next.connection={api:1,revision:1,relay:'server.local',port:3600,local:'pilot001',peer:'copil001',role:1,seat:2,backend:2,code:0};fixtureController.receive(next)})()");
  assert.equal(await evaluate("document.querySelector('[data-seat-choice=me]').getAttribute('transform')"),'translate(440 0)');
  const connectionLayouts=[];
  for(const [width,height] of [[760,700],[420,560]]){
   await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=0");
   const layout=await evaluate("(()=>{const pane=document.querySelector('[data-setup-scroll]');return {width:innerWidth,height:innerHeight,bodyWidth:document.documentElement.scrollWidth,client:pane.clientWidth,scroll:pane.scrollWidth,scrollHeight:pane.scrollHeight,clientHeight:pane.clientHeight}})()");
   assert(layout.bodyWidth<=width+1 && layout.scroll<=layout.client+1,'Connection setup overflows horizontally: '+JSON.stringify(layout));
   await evaluate("document.querySelector('[data-shared-session]').scrollIntoView({block:'center'})");
   assert(await evaluate("document.querySelector('[data-shared-session]').getBoundingClientRect().bottom<=document.querySelector('[data-setup-scroll]').getBoundingClientRect().bottom"),'Join is unreachable in connection Setup');
   connectionLayouts.push(layout);
   await evaluate("document.querySelector('[data-setup-scroll]').scrollTop=0");
   const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
   fs.writeFileSync(path.join(output,'connection-'+width+'x'+height+'.png'),Buffer.from(shot.data,'base64'));
  }
  await typeField('peer','changed1');assert.equal(await evaluate("document.querySelector('[data-shared-session]').disabled"),true,'Unsaved friend code left Join enabled');
  await typeField('peer','copil001');
  await evaluate("document.querySelector('[data-shared-session]').scrollIntoView({block:'center'})");await clickSelector('[data-shared-session]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'join');
  fs.writeFileSync(path.join(output,'layout-checks.json'),JSON.stringify({reports,safeInsets,colors,connectionLayouts,connectionSetup:'Real typing, draft retention, native Save acknowledgement, independent seat, unsaved-edit Join guard and reachable narrow Setup passed',setupScrolling:{setupBefore,setupAfter,bottom},hitTests:'passed',sharedSession:'Real Join/Leave clicks, confirmed owner during suppression and pending color passed',contextMenu:'Both groups: real right mouse with contextmenu suppressed, left-click actions icons, viewport bounds, scoped command, keyboard and Escape passed'},null,2));
  const setSeat=async(seat,pending,revision,ticket)=>evaluate(`(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.revision=++fixtureRevision;next.client='browser-test';next.request=Number(fixturePackets.at(-1).split('|')[2]);Object.assign(next.shared,{phase:3,seatRevision:${revision},seat:${seat},seatPending:${pending},seatRequest:${ticket}});next.camera={seat:${seat},phase:3};next.altimeters={serial:1,fresh:true,pressure16:[15986,16200],standard:[true,false]};fixtureController.receive(next)})()`);
  await setSeat(2,0,1,0);
  await evaluate("document.querySelector('[data-seat-action=request]').scrollIntoView({block:'center'})");
  await clickSelector('[data-seat-action=request]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'seat|request|1|0');
  await setSeat(2,1,2,1);
  assert(await evaluate("document.querySelector('[data-seat-action=accept]').hidden && !document.querySelector('[data-seat-action=cancel]').hidden"));
  await setSeat(2,3,3,1);
  assert(await evaluate("document.querySelector('[data-seat-action=accept]').hidden && !document.querySelector('[data-seat-action=cancel]').hidden"));
  assert((await evaluate("document.querySelector('[data-seat-status]').textContent")).includes('simulator confirmation'));
  assert.equal(await evaluate("document.querySelector('[data-seat-choice=me]').getAttribute('transform')"),'translate(440 0)');
  await evaluate("document.querySelector('[data-seat-status]').scrollIntoView({block:'center'})");
  const waitingShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  fs.writeFileSync(path.join(output,'seat-instrument-wait-420x560.png'),Buffer.from(waitingShot.data,'base64'));
  await setSeat(2,2,4,2);
  await evaluate("document.querySelector('[data-seat-action=accept]').scrollIntoView({block:'center'})");
  const seatShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  fs.writeFileSync(path.join(output,'seat-confirmation-420x560.png'),Buffer.from(seatShot.data,'base64'));
  await clickSelector('[data-seat-action=accept]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'seat|accept|4|2');
  await setSeat(1,0,5,0);
  assert.equal(await evaluate("document.querySelector('[data-seat-choice=me]').getAttribute('transform')"),'translate(0 0)');
  assert.equal(await evaluate("document.querySelector('[data-camera-status]').textContent"),'Pilot view selected.');
  assert((await evaluate("document.querySelector('[data-altimeter=\"0\"]').textContent")).includes('Tuned 999.13 hPa · STD on'));
  assert((await evaluate("document.querySelector('[data-altimeter=\"1\"]').textContent")).includes('Tuned 1012.50 hPa · STD off'));
  await evaluate("document.querySelector('[data-altimeters]').scrollIntoView({block:'center'})");
  const baroShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  fs.writeFileSync(path.join(output,'altimeters-420x560.png'),Buffer.from(baroShot.data,'base64'));
  fs.writeFileSync(path.join(output,'seat-results.json'),JSON.stringify({request:true,peerConfirmation:true,liveSeatOverridesSavedPreference:true,cameraReadbackStatus:true},null,2));
  const setInstruments=async(flags,phase)=>evaluate(`(()=>{const next=JSON.parse(JSON.stringify(fixtureController.snapshot));next.revision=++fixtureRevision;next.client='browser-test';next.request=Number(fixturePackets.at(-1).split('|')[2]);Object.assign(next.shared,{phase:3,seatRevision:7,seat:1,seatPending:0,seatRequest:0,session:'dddddddddddddddddddddddddddddddd',serial:++window.instrumentUiSerial});next.altimeters={serial:20,fresh:true,pressure16:[15986,16200],standard:[true,false],nativeFlags:${flags},reported:true,sharedPhase:${phase},pending:0};fixtureController.receive(next)})()`);
  await evaluate('window.instrumentUiSerial=200');
  await setInstruments(559,2);
  assert.equal(await evaluate("document.querySelector('[data-altimeter-title]').textContent"),'Altimeters · shared');
  await setInstruments(515,2);
  assert.equal(await evaluate("document.querySelector('[data-altimeter-title]').textContent"),'Altimeters · local'); // Active report without a verified native profile grants no ownership display.
  await setInstruments(819,4);
  await evaluate("document.querySelector('[data-altimeters]').scrollIntoView({block:'center'})");
  const heldShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  fs.writeFileSync(path.join(output,'instrument-held-420x560.png'),Buffer.from(heldShot.data,'base64'));
  await clickSelector('[data-instruments-local]');
  assert.equal(await evaluate('fixturePackets.at(-1).split("|").slice(3).join("|")'),'instruments|local|dddddddddddddddddddddddddddddddd');
  await setInstruments(503,8);
  assert(await evaluate("document.querySelector('[data-instruments-local]').disabled"));
  assert.equal(await evaluate("document.querySelector('[data-altimeter-title]').textContent"),'Altimeters · releasing inputs');
  await setInstruments(579,8);
  assert.equal(await evaluate("document.querySelector('[data-altimeter-title]').textContent"),'Altimeters · local');
  assert(await evaluate("document.querySelector('[data-instruments-local]').hidden"));
  const localShot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});
  fs.writeFileSync(path.join(output,'instrument-local-420x560.png'),Buffer.from(localShot.data,'base64'));
  fs.writeFileSync(path.join(output,'instrument-results.json'),JSON.stringify({sharedRequiresNativeProof:true,held:true,realScopedLocalClick:true,waitsForReadback:true,localAfterInputRelease:true},null,2));
  // Emulate the embedded view's constraints rather than relying on Chrome's
  // SVG descendant picking or its modern hover/focus pseudo-class support.
  await evaluate(`(()=>{
   document.querySelector('[data-close-settings]').click();setFixture('split');
   for(const sheet of document.styleSheets)for(let i=sheet.cssRules.length-1;i>=0;i--){
    const rule=sheet.cssRules[i];if(rule.selectorText && /:hover|:focus-visible/.test(rule.selectorText))sheet.deleteRule(i);
   }
   window.emitSvgRoot=function(type,x,y,fields){const s=document.querySelector('[data-cockpit]'),p=s.createSVGPoint();p.x=x;p.y=y;const c=p.matrixTransform(s.getScreenCTM());
    s.dispatchEvent(new MouseEvent(type,Object.assign({bubbles:true,cancelable:true,clientX:c.x,clientY:c.y,button:0},fields||{})));
   };
   window.svgInputImmediate=(()=>{emitSvgRoot('mousemove',230,540);return document.querySelector('[data-seat-choice=me]').getAttribute('data-hovered')})();
  })()`);
  assert.equal(await evaluate('svgInputImmediate'),'true','SVG-root event did not resolve the seat');
  async function placePointer(x,y){
   const point=await evaluate(`(()=>{const s=document.querySelector('[data-cockpit]'),p=s.createSVGPoint();p.x=${x};p.y=${y};const c=p.matrixTransform(s.getScreenCTM());return {x:c.x,y:c.y}})()`);
   await send('Input.dispatchMouseEvent',{type:'mouseMoved',...point});
  }
  // Keep Chrome's real cursor at the synthetic location; layout can otherwise
  // emit an unrelated mouseover for its previous position after the root event.
  await placePointer(230,540);
  await new Promise(resolve=>setTimeout(resolve,200));
  assert.equal(await evaluate("getComputedStyle(document.querySelector('[data-seat-choice=me] rect')).fill"),'rgb(105, 142, 171)','Pointer feedback requires native pseudo hover');
  await evaluate("emitSvgRoot('click',230,540);emitSvgRoot('pointermove',450,330)");
  await placePointer(450,330);
  await new Promise(resolve=>setTimeout(resolve,200));
  assert.equal(await evaluate("document.querySelector('[data-seat-choice=me]').getAttribute('aria-pressed')"),'true');
  assert.equal(await evaluate("getComputedStyle(document.querySelector('[data-control=propeller] > .fsc-hit')).fillOpacity"),'0.12');
  const beforeMenu=await evaluate('fixturePackets.length');
  await evaluate("emitSvgRoot('pointerdown',450,330,{buttons:2});emitSvgRoot('pointerup',450,330,{button:2});emitSvgRoot('click',450,330)");
  assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),false);
  assert.equal(await evaluate('fixturePackets.length'),beforeMenu,'Opening a secondary menu sent an ownership command');
  await evaluate("document.querySelector('[data-force]').click();setFixture('split')");
  assert.equal(await evaluate("fixturePackets.at(-1).split('|').slice(3).join('|')"),'force|propeller|me');
  const beforeHold=await evaluate('fixturePackets.length');
  await evaluate("emitSvgRoot('mousedown',502,330)");await new Promise(resolve=>setTimeout(resolve,700));
  assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),false,'Primary hold did not open a fallback menu');
  await evaluate("emitSvgRoot('mouseup',502,330);emitSvgRoot('click',502,330)");
  assert.equal(await evaluate("document.querySelector('[data-control-menu]').hidden"),false,'Release dismissed the hold menu');
  assert.equal(await evaluate('fixturePackets.length'),beforeHold,'Hold/release changed ownership');
  fs.writeFileSync(path.join(output,'embedded-input-results.json'),JSON.stringify({pseudoHoverDisabled:true,svgRootTargeting:true,seatHover:true,independentPairHover:true,pointerOnlySecondaryMenu:true,primaryHold:true,openingNeverAssigns:true,simulatorContacted:false},null,2));
  console.log('Browser passed: 64 layouts, 36 EFB insets, seat-swap request/confirmation and camera status, shared Join/Leave, tolerance, Auto, Setup scrolling, scoped force takeover and embedded-input constraints (no pseudo hover, SVG-root targets, pointer-only secondary click, primary hold).');
  await send('Browser.close').catch(()=>{});
 } finally { if(socket)socket.close();if(!child.killed)child.kill(); }
}
main().catch(error=>{console.error(error);process.exitCode=1});
