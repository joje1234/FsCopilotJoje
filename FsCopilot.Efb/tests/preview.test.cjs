// Real Chrome interaction checks against the standalone preview, with no MSFS.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {pathToFileURL}=require('node:url');
const repo=path.resolve(__dirname,'../..');
const output=path.join(repo,'artifacts/ui-preview');fs.mkdirSync(output,{recursive:true});
const profile=path.join(output,'chrome-test-'+Date.now());
const child=spawn(process.env.FSC_BROWSER||'C:/Program Files/Google/Chrome/Application/chrome.exe',[
    '--headless=new','--remote-debugging-address=127.0.0.1','--remote-debugging-port=0','--user-data-dir='+profile,
    '--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-extensions','--disable-sync','about:blank'
],{windowsHide:true,stdio:['ignore','ignore','pipe']});
let browserError='';child.stderr.on('data',data=>browserError=(browserError+data).slice(-2000));
child.on('error',error=>browserError=error.message);
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function main(){
    let socket;
    try {
        const portFile=path.join(profile,'DevToolsActivePort');
        for(let i=0;i<100&&!fs.existsSync(portFile);i++)await delay(100);
        assert(fs.existsSync(portFile),'Chrome debugger unavailable: '+browserError);
        const port=fs.readFileSync(portFile,'utf8').split('\n')[0].trim();
        const page=await(await fetch('http://127.0.0.1:'+port+'/json/new?about:blank',{method:'PUT'})).json();
        socket=new WebSocket(page.webSocketDebuggerUrl);
        await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
        let sequence=0;const pending=new Map(),errors=[];
        socket.addEventListener('message',event=>{
            const value=JSON.parse(event.data);
            if(value.method==='Runtime.exceptionThrown')errors.push(value.params.exceptionDetails.text+': '+JSON.stringify(value.params.exceptionDetails.exception));
            if(!value.id)return;const item=pending.get(value.id);if(!item)return;pending.delete(value.id);
            value.error?item.reject(new Error(JSON.stringify(value.error))):item.resolve(value.result);
        });
        socket.addEventListener('close',()=>{for(const item of pending.values())item.reject(new Error('Chrome debugger closed: '+browserError));pending.clear();});
        const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});
        const evaluate=async(expression)=>{
            const value=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
            assert(!value.exceptionDetails,JSON.stringify(value.exceptionDetails));return value.result.value;
        };
        const wait=async(expression)=>{for(let i=0;i<80;i++){if(await evaluate(expression))return;await delay(50);}throw new Error('Timed out: '+expression);};
        const point=selector=>evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
        const move=async selector=>{
            for(let attempt=0;attempt<10;attempt++) {
                await send('Input.dispatchMouseEvent',{type:'mouseMoved',...await point(selector)});
                if(await evaluate(`document.querySelector(${JSON.stringify(selector)}).matches(':hover')`))return;
                await delay(50);
            }
            throw new Error('Pointer did not reach '+selector);
        };
        const click=async selector=>{
            const p=await point(selector);await send('Input.dispatchMouseEvent',{type:'mouseMoved',...p});
            await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});
            await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});
        };
        const css=(selector,property)=>evaluate(`getComputedStyle(document.querySelector(${JSON.stringify(selector)})).getPropertyValue(${JSON.stringify(property)}).trim()`);
        const hit=g=>'[data-control="'+g+'"] > .fsc-hit';
        await send('Runtime.enable');await send('Page.enable');await send('Page.bringToFront');
        // Keep live-reload notifications from interrupting an automated gesture.
        await send('Network.enable');await send('Network.setBlockedURLs',{urls:['*/__preview/events']});
        await send('Emulation.setDeviceMetricsOverride',{width:1320,height:980,deviceScaleFactor:1,mobile:false});
        await send('Page.navigate',{url:process.env.FSC_PREVIEW_URL||pathToFileURL(path.join(repo,'FsCopilot.Efb/preview.html')).href});
        await wait('!!window.fscPreview && fscPreview.controller.state().usable');
        await wait('document.readyState === "complete"');
        await evaluate('document.fonts.ready.then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))');
        assert.equal(await evaluate('document.querySelectorAll("[data-seat-choice][aria-pressed=true]").length'),0);
        assert.equal(await css('[data-seat-choice="me"] rect','fill'),'rgb(82, 110, 135)');
        assert.equal(await css('[data-seat-choice="mock"] rect','fill'),'rgb(140, 104, 73)');
        await move('[data-seat-choice="me"] .fsc-seat-back');await delay(200);
        assert.equal(await css('[data-seat-choice="me"] rect','fill'),'rgb(105, 142, 171)');
        await move('[data-seat-choice="mock"] .fsc-seat-back');await delay(200);
        assert.equal(await css('[data-seat-choice="mock"] rect','fill'),'rgb(180, 138, 98)');
        await click('[data-seat-choice="mock"] .fsc-seat-back');await delay(200);
        assert.equal(await css('[data-seat-choice="mock"] rect','stroke'),'rgb(255, 255, 255)');
        assert.equal(await css('[data-seat-choice="mock"] rect','stroke-width'),'3.5px');
        await move(hit('propeller'));await delay(200);
        assert.equal(await css(hit('propeller'),'fill'),'rgb(215, 160, 108)');
        assert.equal(await css(hit('propeller'),'fill-opacity'),'0.12');
        assert.equal(await css(hit('throttle'),'fill-opacity'),'0.2');
        assert.equal(await css(hit('mixture'),'fill-opacity'),'0.2');
        assert.equal(await css(hit('propeller'),'stroke'),'none');
        assert.equal(await css('.fsc-quadrant-shell','stroke'),await css('.fsc-shell','stroke'));
        await click(hit('propeller'));await wait('fscPreview.snapshot().propellerBlocked && !fscPreview.controller.pending');
        await move('h1');await delay(200);
        assert.equal(await css(hit('propeller'),'fill-opacity'),'0.2');
        await evaluate('fscPreview.setHardware("propeller1",20);fscPreview.setHardware("propeller2",20)');
        await click('[data-seat-choice="me"] .fsc-seat-back');await click(hit('propeller'));
        await wait('fscPreview.snapshot().pickup.propeller.waiting && !fscPreview.controller.pending');
        assert.equal(await css(hit('propeller'),'animation-name'),'fsc-await-alignment');
        assert.equal(await css(hit('propeller'),'animation-duration'),'1s');
        const samples=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(hit('propeller'))}),a=e.getAnimations().find(a=>a.animationName==='fsc-await-alignment');a.pause();a.currentTime=250;const on=getComputedStyle(e).fillOpacity;a.currentTime=750;const off=getComputedStyle(e).fillOpacity;a.play();return {on,off}})()`);
        assert.deepEqual(samples,{on:'0.28',off:'0'});
        await click('h1');await wait('!fscPreview.snapshot().pickup.propeller.waiting && !fscPreview.controller.pending');
        assert(await evaluate('fscPreview.snapshot().propellerBlocked'));
        assert.equal(await evaluate('document.querySelectorAll("[data-seat-choice][aria-pressed=true]").length'),0);
        await evaluate('fscPreview.reset("waiting")');await wait('fscPreview.snapshot().pickup.throttle.waiting && !fscPreview.controller.pending');
        await click('[data-seat-choice="me"] .fsc-seat-back');
        await wait('!fscPreview.snapshot().pickup.throttle.waiting && !fscPreview.controller.pending');
        assert(await evaluate('fscPreview.snapshot().throttleBlocked'));
        await evaluate('fscPreview.reset("waiting")');await wait('fscPreview.snapshot().pickup.throttle.waiting && !fscPreview.controller.pending');
        const waitingShot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'waiting.png'),Buffer.from(waitingShot.data,'base64'));
        await click('[data-align]');await wait('!fscPreview.snapshot().throttleBlocked && !fscPreview.snapshot().pickup.throttle.waiting');
        assert(await evaluate('fscPreview.snapshot().propellerBlocked && fscPreview.snapshot().mixtureBlocked'));
        await click('h1');await delay(250);
        assert(!await evaluate('fscPreview.snapshot().throttleBlocked'));
        const cleanLayout=await evaluate(`(()=>{
            const root=document.querySelector('[data-cockpit]'),shell=root.querySelector('.fsc-quadrant-shell').getBBox();
            const labels=[...root.querySelectorAll('.fsc-group-owner,.fsc-group-summary,.fsc-pair-owner,.fsc-group-actions')];
            return {hidden:labels.every(e=>getComputedStyle(e).display==='none'),emptyPanel:root.querySelector('[data-extra-axes]').children.length===0,
                labelsInside:[...root.querySelectorAll('.fsc-pair-mode')].every(e=>e.getBBox().y+e.getBBox().height<shell.y+shell.height),
                steeringGap:shell.x-root.querySelector('.fsc-yoke-hit').getBBox().x-root.querySelector('.fsc-yoke-hit').getBBox().width,
                seatGap:shell.x-root.querySelector('.fsc-seat-back').getBBox().x-root.querySelector('.fsc-seat-back').getBBox().width,
                brakes:[...root.querySelectorAll('[data-axis-graphic="leftBrake"],[data-axis-graphic="rightBrake"]')].every(e=>!!e.closest('[data-pedal-station]')),
                pedalPairs:root.querySelectorAll('[data-rudder-pair]').length,auto:fscPreview.snapshot().autoMask};})()`);
        assert(cleanLayout.hidden&&cleanLayout.emptyPanel&&cleanLayout.labelsInside&&cleanLayout.brakes,JSON.stringify(cleanLayout));
        assert(cleanLayout.steeringGap>=40&&cleanLayout.seatGap>=50,JSON.stringify(cleanLayout));
        assert.equal(cleanLayout.pedalPairs,2);assert.equal(cleanLayout.auto,14);
        await evaluate('fscPreview.reset("me");fscPreview.setHardware("rudder",75)');
        assert.equal(await evaluate('document.querySelector("[data-pedal-input=left]").getAttribute("transform")'),'translate(0 -6)');
        assert.equal(await evaluate('document.querySelector("[data-pedal-input=right]").getAttribute("transform")'),'translate(0 6)');
        await evaluate('fscPreview.reset("waiting")');await wait('fscPreview.snapshot().pickup.throttle.waiting && !fscPreview.controller.pending');
        const p=await point(hit('throttle'));
        await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'right',clickCount:1});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'right',clickCount:1});
        await wait('!document.querySelector("[data-control-menu]").hidden');await delay(900);
        assert(!await evaluate('document.querySelector("[data-control-menu]").hidden'),'Menu closed on a normal status update');
        assert.deepEqual(await evaluate('[...document.querySelector("[data-control-menu]").querySelectorAll("button")].filter(e=>!e.hidden).map(e=>e.textContent)'),['Force takeover']);
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape'});
        async function double(selector){const p=await point(selector);for(const count of [1,2]){
            await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:count});
            await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:count});
        }}
        await double(hit('throttle'));await wait('!fscPreview.snapshot().throttleBlocked && !fscPreview.controller.pending');
        assert.equal(await evaluate('fscPreview.commands.filter(e=>e==="force|throttle|me").length'),1);
        assert(await evaluate('fscPreview.snapshot().propellerBlocked && fscPreview.snapshot().mixtureBlocked'));
        await evaluate('fscPreview.reset("mixed")');await double(hit('throttle'));await delay(200);
        assert(await evaluate('fscPreview.snapshot().throttleBlocked && !fscPreview.commands.some(e=>e.startsWith("force|"))'));
        await click('[data-seat-choice="mock"] .fsc-seat-back');await double(hit('throttle'));await delay(200);
        assert(!await evaluate('fscPreview.commands.some(e=>e.startsWith("force|"))'));
        await evaluate('fscPreview.reset("mixed")');await click('[data-seat-choice="me"] .fsc-seat-back');
        await double(hit('throttle'));await wait('!fscPreview.snapshot().throttleBlocked && !fscPreview.controller.pending');
        assert.equal(await evaluate('fscPreview.commands.filter(e=>e==="force|throttle|me").length'),1,'Double-click during the first acknowledgement must queue one force');
        await click('[data-size="compact"]');
        const layout=await evaluate('(()=>{const a=document.querySelector(".fsc-controls");return {height:a.clientHeight,scroll:a.scrollHeight,width:a.clientWidth}})()');
        assert(layout.width<=420&&layout.scroll<=layout.height+1,JSON.stringify(layout));
        await delay(200);const shot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'compact.png'),Buffer.from(shot.data,'base64'));
        await click('[data-size="normal"]');await evaluate('fscPreview.reset("mixed")');await delay(200);
        const mixed=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'mixed.png'),Buffer.from(mixed.data,'base64'));
        assert.deepEqual(errors,[],'Runtime errors in preview');
        console.log('PASS standalone Chrome preview: blue/orange seats, individual hovers, clean labels, padded layout, pedal rudder/brakes, empty instrument panel, stable Force-only menu, Auto on, scoped double-click force (including delayed acknowledgement), blink/cancel/alignment and compact layout. No simulator access.');
        await send('Browser.close').catch(()=>{});
    } finally {if(socket)socket.close();if(!child.killed)child.kill();}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
