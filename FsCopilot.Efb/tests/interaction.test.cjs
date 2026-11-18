// Embedded-input constraints: SVG-root targeting, no CSS pseudo hover, pointer
// secondary buttons and primary/touch holds. No simulator or network connection.
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {parseHTML}=require(process.env.FSC_DOM_MODULE||'linkedom');
const withPairs=require('./status_fixture.cjs');
const browser=parseHTML('<html><body><main id="app"></main></body></html>');
let now=0,nextTimer=0,revision=0;const timers=new Map(),packets=[];
let preferences={api:1,revision:1,relay:'relay.test',port:3600,local:'pilot001',peer:'copil001',role:1,seat:1,backend:1,code:0};
class ClockDate extends Date {static now(){return now;}}
const window={document:browser.document,console,Map,Set,Date:ClockDate,Math,FSC_CONTROLS_PREVIEW:true,
    setTimeout(fn,delay){const id=++nextTimer;timers.set(id,{fn,at:now+delay});return id;},clearTimeout(id){timers.delete(id);}};
window.window=window;
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../PackageSources/FsCopilotControls/FsCopilotControls.js'),'utf8'),window);
const controller=new window.FscControls.Controller({send(p){packets.push(p);}},{client:'embedded-input',now:()=>now});
const app=browser.document.querySelector('#app'),unmount=window.FscControls.mount(app,controller);
const find=selector=>app.querySelector(selector),graphic=find('[data-cockpit]');
let box={left:100,top:30,width:900,height:680};graphic.getBoundingClientRect=()=>box;
const axis=id=>({id,label:id,input:50,target:50,calibrated:true,fresh:true,aligned:true});
function frame(extra={}) {
    controller.receive(withPairs({version:5,session:'abc',revision:++revision,client:controller.client,request:controller.nextRequest,
        ready:true,applying:false,advanced:true,autoMask:14,steeringBlocked:true,quadrantBlocked:true,error:'',connection:preferences,
        steering:{seen:0,blocked:0,last:'',value:0},quadrant:{seen:0,blocked:0,last:'',value:0},
        pickup:{steering:{waiting:false,tolerance:10,axes:['roll','pitch'].map(axis)},quadrant:{waiting:false,tolerance:10,axes:['throttle1','propeller1','mixture1','throttle2','propeller2','mixture2'].map(axis)}},...extra}));
    assert(controller.state().online,'Fixture must be live');
}
function advance(ms){now+=ms;for(const [id,timer]of [...timers])if(timer.at<=now){timers.delete(id);timer.fn();}}
function input(target,type,x,y,extra={}) {
    const event=new browser.Event(type,{bubbles:true,cancelable:true});
    const scale=Math.min(box.width/900,box.height/680);
    Object.assign(event,{clientX:box.left+(box.width-900*scale)/2+x*scale,clientY:box.top+(box.height-680*scale)/2+y*scale,...extra});
    target.dispatchEvent(event);return event;
}
function root(type,x,y,extra){return input(graphic,type,x,y,extra);}
function closeMenu(){const event=new browser.Event('keydown',{bubbles:true,cancelable:true});event.key='Escape';graphic.dispatchEvent(event);}
frame();
root('pointermove',230,540);assert.equal(find('[data-seat-choice="me"]').getAttribute('data-hovered'),'true');
root('mousemove',670,540);assert.equal(find('[data-seat-choice="me"]').getAttribute('data-hovered'),null);assert.equal(find('[data-seat-choice="mock"]').getAttribute('data-hovered'),'true');
root('click',230,540,{button:0});assert.equal(find('[data-seat-choice="me"]').getAttribute('aria-pressed'),'true');
assert.equal(packets.length,0,'Seat selection must not change ownership');
root('pointermove',450,330);assert.equal(find('[data-control="propeller"]').getAttribute('data-hovered'),'true');
assert.equal(find('[data-control="throttle"]').getAttribute('data-hovered'),null);
frame();assert.equal(find('[data-control="propeller"]').getAttribute('data-hovered'),'true','Status refresh erased pointer feedback');
box={left:31,top:54,width:1800,height:680};root('pointermove',502,330);
assert.equal(find('[data-control="mixture"]').getAttribute('data-hovered'),'true','Horizontal letterboxing broke SVG picking');
box={left:31,top:54,width:450,height:700};root('pointermove',230,360);
assert.equal(find('[data-control="steering"]').getAttribute('data-hovered'),'true','Vertical letterboxing broke SVG picking');
app.dispatchEvent(new browser.Event('mouseleave'));assert.equal(find('[data-control="steering"]').getAttribute('data-hovered'),null);
box={left:100,top:30,width:900,height:680};
preferences={...preferences,revision:2,seat:2};frame();
root('pointermove',670,540);assert.equal(find('[data-seat-choice="me"]').getAttribute('data-hovered'),'true','Seat swap broke coordinate picking');
preferences={...preferences,revision:3,seat:1};frame();
// Pointer-only right click, including a compatibility click misreported as left.
let count=packets.length;root('pointerdown',398,330,{button:0,buttons:2});
assert(!find('[data-control-menu]').hidden);root('pointerup',398,330,{button:2,buttons:0});root('click',398,330,{button:0});
assert(!find('[data-control-menu]').hidden,'Secondary compatibility click dismissed the menu');assert.equal(packets.length,count);
input(find('[data-force]'),'click',0,0,{button:0});assert(packets.at(-1).endsWith('|force|throttle|me'));frame();
// Holding never sends a request; release cannot become an assignment click.
count=packets.length;root('pointerdown',502,330,{button:0});root('mousedown',502,330,{button:0});
advance(649);assert(find('[data-control-menu]').hidden);advance(1);assert(!find('[data-control-menu]').hidden);
advance(5000);frame();root('pointerup',502,330,{button:0});root('click',502,330,{button:0});
assert(!find('[data-control-menu]').hidden,'A long hold consumed its suppression before release');assert.equal(packets.length,count);closeMenu();
// A short click follows normal aligned takeover, and movement cancels a hold.
root('click',230,540,{button:0});
root('pointerdown',230,360,{button:0});advance(100);root('pointerup',230,360,{button:0});root('click',230,360,{button:0});
assert(packets.at(-1).endsWith('|set|steering|me'));frame();
root('pointerdown',450,330,{button:0});root('pointermove',461,330,{button:0});advance(700);assert(find('[data-control-menu]').hidden);root('pointerup',461,330,{button:0});
// Touch uses the same deliberate menu path; no button ownership side effect.
count=packets.length;
input(graphic,'touchstart',398,330,{touches:[{clientX:498,clientY:360}]});advance(650);
assert(!find('[data-control-menu]').hidden);input(graphic,'touchend',398,330,{touches:[]});root('click',398,330,{button:0});
assert.equal(packets.length,count);assert(!find('[data-control-menu]').hidden);closeMenu();
root('pointerdown',502,330,{button:0});frame({session:'def'});advance(650);
assert(find('[data-control-menu]').hidden,'An old-session hold opened a new-session menu');
root('pointerup',502,330,{button:0});frame({session:'def'});root('pointerdown',398,330,{button:0});unmount();advance(1000);assert.equal(timers.size,0);
console.log('PASS embedded input: SVG-root picking, scaling/letterboxing, independent hover across updates, pointer right click, trailing-click suppression, long/short/touch holds, movement cancellation, session fences and timer cleanup. No simulator access.');
