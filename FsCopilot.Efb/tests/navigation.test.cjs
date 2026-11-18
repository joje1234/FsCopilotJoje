const assert=require('node:assert/strict');
const {RouteInspector,canonicalRoute}=require('../src/navigation.js');
function route(from='ENBR',to='ENGM') {return {departureAirport:{ident:from,type:'A'},destinationAirport:{ident:to,type:'A'},enroute:[{fixIcao:{ident:'FIX1',type:'W'}}]};}
function fixture({replies=[{id:17,route:route()}],efb=route('ENVA','ENBO'),early=false,silent=false,pendingRequest=false}={}) {
    let handler;const calls=[],reports=[],errors=[];
    const manager={avionicsRouteRequestResponse:{on(callback){handler=callback;return {destroy(){handler=null;}};}},
        requestAvionicsRoute(){calls.push('REQUEST_AVIONICS_ROUTE');if(pendingRequest)return new Promise(()=>{});const send=()=>{if(!silent)replies.forEach(reply=>handler&&handler(manager,{requestId:reply.id,route:reply.route}));};if(early)send();else setTimeout(send,0);return Promise.resolve(17);},
        getEfbRoute(){calls.push('GET_EFB_ROUTE');return efb instanceof Promise?efb:Promise.resolve(efb);}};
    const environment={msfssdk:{FlightPlanRouteManager:{getManager:async()=>manager}},setTimeout,clearTimeout};
    const inspector=new RouteInspector(environment,{settleMs:10,timeoutMs:60,comparisonMs:10,report:value=>reports.push(value),reportError:value=>errors.push(JSON.parse(value))});
    return {inspector,calls,reports,errors,environment};
}
async function run(){
    const a=fixture({early:true});assert(await a.inspector.inspect());assert.equal(a.inspector.state.phase,'observed');
    assert(a.inspector.state.summary.includes('ENBR → ENGM'));assert.equal(a.reports[0],canonicalRoute(route()));
    assert.deepEqual(a.calls,['REQUEST_AVIONICS_ROUTE','GET_EFB_ROUTE']);a.inspector.dispose();
    const b=fixture({replies:[{id:99,route:{}},{id:17,route:route()}]});assert(await b.inspector.inspect());b.inspector.dispose();
    const c=fixture({replies:[{id:17,route:route()},{id:17,route:route('ENBR','ENTO')}]});
    assert(!await c.inspector.inspect());assert(c.inspector.state.message.includes('Multiple avionics'));assert.equal(c.reports.length,0);c.inspector.dispose();
    const same=fixture({efb:route()});assert(await same.inspector.inspect());assert(same.inspector.state.message.includes('same route as the EFB'));same.inspector.dispose();
    const silence=fixture({silent:true});assert(!await silence.inspector.inspect());assert(silence.inspector.state.message.includes('No avionics'));assert(silence.inspector.state.message.includes('edition'));assert(!silence.calls.includes('GET_EFB_ROUTE'));
    assert.equal(silence.errors[0].stage,'response');assert.equal(silence.errors[0].requestId,17);assert.equal(silence.errors[0].interface,'sdk');
    assert.throws(()=>canonicalRoute(silence.errors[0]));silence.inspector.dispose();
    const unacknowledged=fixture({pendingRequest:true});assert(!await unacknowledged.inspector.inspect());assert(unacknowledged.inspector.state.message.includes('did not acknowledge'));assert.equal(unacknowledged.errors[0].stage,'request');unacknowledged.inspector.dispose();
    const notReady=fixture();notReady.environment.msfssdk.FlightPlanRouteManager.getManager=()=>new Promise(()=>{});
    assert(!await notReady.inspector.inspect());assert(notReady.inspector.state.message.includes('listener did not become ready'));assert.deepEqual(notReady.calls,[]);assert.equal(notReady.errors[0].stage,'listener');notReady.inspector.dispose();
    const pending=fixture({silent:true});const work=pending.inspector.inspect();assert(!await pending.inspector.inspect());pending.inspector.dispose();assert(!await work);
    const comparison=fixture({efb:new Promise(()=>{})});assert(await comparison.inspector.inspect());comparison.inspector.dispose();
    const unsupported=new RouteInspector({setTimeout,clearTimeout},{timeoutMs:40});assert(!await unsupported.inspect());assert(unsupported.state.message.includes('unavailable'));unsupported.dispose();
    let receive;const calls=[];
    const direct=new RouteInspector({setTimeout,clearTimeout,msfssdk:{FlightPlanRouteManager:{getManager(){throw new Error('EFB must not wait on the SDK GameState singleton when a direct listener exists.');}}},RegisterViewListener(name,ready){assert.equal(name,'JS_LISTENER_PLANNEDROUTE');ready();return {
        on(event,callback){assert.equal(event,'AvionicsRouteRequestResponse');receive=callback;},
        off(event,callback){assert.equal(event,'AvionicsRouteRequestResponse');assert.equal(callback,receive);receive=null;},
        call(name){calls.push(name);if(name==='GET_EFB_ROUTE')return Promise.resolve(route());receive(route(),42);return Promise.resolve(42);}
    };}},{settleMs:10,timeoutMs:60});
    assert(await direct.inspect());assert.deepEqual(calls,['REQUEST_AVIONICS_ROUTE','GET_EFB_ROUTE']);direct.dispose();assert.equal(receive,null);
    let finishConnect, subscriptions=0;
    const late=new RouteInspector({setTimeout,clearTimeout,msfssdk:{FlightPlanRouteManager:{getManager:()=>new Promise(resolve=>finishConnect=resolve)}}},{timeoutMs:40});
    const lateRead=late.inspect();late.dispose();finishConnect({avionicsRouteRequestResponse:{on(){subscriptions++;}}});
    assert(!await lateRead);assert.equal(subscriptions,0);
    assert.throws(()=>canonicalRoute({enroute:[],bad:NaN}));assert.throws(()=>canonicalRoute(JSON.parse('{"enroute":[],"__proto__":{}}')));
    assert.throws(()=>canonicalRoute({enroute:new Array(2049).fill(0)}));
    assert.equal(canonicalRoute({enroute:[],b:'ø',a:1}),'{"a":1,"b":"\\u00f8","enroute":[]}');
    console.log('Avionics route inspector passed: correlated responses, ambiguity, EFB distinction, listener/request/response timeout diagnostics, direct EFB listener preference, cancellation and bounded canonical data. No route writers called; simulator interfaces mocked.');
}
run().catch(error=>{console.error(error);process.exitCode=1;});
