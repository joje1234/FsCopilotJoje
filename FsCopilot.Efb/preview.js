// Browser-only simulated transport. No CommBus, SimConnect or external requests.
(function () {
    'use strict';
    const pairs = ['throttle', 'propeller', 'mixture'];
    const groups = ['steering', ...pairs];
    const definitions = [
        ['roll', 'Yoke roll', 'steering', 50], ['pitch', 'Yoke pitch', 'steering', 50], ['rudder', 'Rudder', 'steering', 50],
        ['leftBrake', 'Left brake', 'steering', 0], ['rightBrake', 'Right brake', 'steering', 0],
        ...pairs.flatMap((kind, i) => [1, 2].map(engine => [kind + engine, ({throttle:'Throttle',propeller:'RPM',mixture:'Mixture'})[kind] + ' ' + engine, kind, [65, 60, 80][i]]))
    ];
    const auto = true;
    let revision = 0, generation = 0, tolerance = 10;
    let model = {}, blocked = {}, waiting = {}, stable = {};
    const log = document.querySelector('[data-preview-status]'), commands=[];
    function say(message) { log.textContent = message; }
    function reset(scene) {
        ++generation;controller.pending=null;commands.length=0;
        groups.forEach(group => { blocked[group] = scene === 'waiting' || scene === 'mixed' && ['throttle','mixture'].includes(group); waiting[group] = false; stable[group] = 0; });
        definitions.forEach(([id,label,group,target]) => { model[id] = {id,label,group,target,input:blocked[group] && target ? 20 : target}; });
        emit();
        for(let i=0;i<3;i++) document.querySelector('#app').dispatchEvent(new KeyboardEvent('keydown', {key:'Escape',bubbles:true}));
        if (scene === 'waiting') {
            document.querySelector('[data-seat-choice="me"]').dispatchEvent(new MouseEvent('click',{bubbles:true}));
            document.querySelector('[data-control="throttle"]').dispatchEvent(new MouseEvent('click',{bubbles:true}));
        }
        say(scene === 'waiting' ? 'Throttle requested by Me. Align the hardware or click outside to cancel.' : 'Select either seat and try the controls.');
        syncSliders();
    }
    function axes(group) {
        return definitions.filter(d => group === 'quadrant' ? pairs.includes(d[2]) : d[2] === group).map(([id]) => ({
            ...model[id],calibrated:true,fresh:true,aligned:Math.abs(model[id].input-model[id].target)<=tolerance
        }));
    }
    function status(client='', request=0) {
        const value={version:5,session:'abc',revision:++revision,client,request,ready:true,applying:false,error:'',
            advanced:true,autoMask:auto?14:0,learningStorage:'ready',steeringBlocked:blocked.steering,
            quadrantBlocked:pairs.every(g=>blocked[g]),quadrantMixed:pairs.some(g=>blocked[g])&&!pairs.every(g=>blocked[g]),quadrantToleranceMixed:false,pickup:{}};
        ['steering','quadrant',...pairs].forEach(group=>{
            value[group]={seen:0,blocked:0,last:'',value:0};
            if (pairs.includes(group)) value[group+'Blocked']=blocked[group];
            value.pickup[group]={waiting:group==='quadrant'?pairs.some(g=>waiting[g]):waiting[group],autoEnabled:group!=='steering'&&auto,tolerance,axes:axes(group)};
        });
        return value;
    }
    function emit(client,request) { controller.receive(status(client,request)); }
    function setHardware(id, value) {
        const axis=model[id]; if(!axis) return;
        const previous=axis.input;axis.input=Number(value);
        if(!blocked[axis.group]) axis.target=axis.input;
        else if(auto&&pairs.includes(axis.group)&&Math.abs(previous-axis.input)>2) waiting[axis.group]=true;
        emit();syncSliders();
    }
    const transport={subscribe(){return ()=>{};},send(packet){
        const parts=packet.split('|'),client=parts[1],request=Number(parts[2]),verb=parts[3],group=parts[4],choice=parts[5];
        if(verb==='read'){emit(client,request);return;}
        commands.push(parts.slice(3).join('|'));
        const sentGeneration=generation;
        setTimeout(()=>{
            if(sentGeneration!==generation)return;
            if(verb==='set'&&groups.includes(group)) {
                if(choice==='mock'){blocked[group]=true;waiting[group]=false;}
                else if(blocked[group]) waiting[group]=true;
                say(choice==='me'?'Me requested '+group+'. Align the hardware to complete takeover.':group+' assigned to Mock.');
            } else if(verb==='force'&&groups.includes(group)) {
                blocked[group]=false;waiting[group]=false;
                definitions.filter(d=>d[2]===group).forEach(([id])=>model[id].target=model[id].input);
            } else if(verb==='tolerance') tolerance=Number(choice);
            emit(client,request);syncSliders();
        },90);
    }};
    const controller=new FscControls.Controller(transport,{client:'ui-preview'});
    const unmount=FscControls.mount(document.querySelector('#app'),controller);
    controller.subscribe(()=>{document.querySelector('[data-connection]').textContent='Preview';});
    const sliders=document.querySelector('[data-hardware-sliders]');
    const rows=[['roll','Roll'],['pitch','Pitch'],['rudder','Rudder'],['throttle','Throttle'],['propeller','RPM'],['mixture','Mixture']];
    rows.forEach(([key,label])=>{
        const row=document.createElement('label');row.className='preview-slider';row.dataset.slider=key;
        row.innerHTML='<span>'+label+'<output></output></span><div class="preview-range"><input type="range" min="0" max="100" step="1" aria-label="Simulated '+label+' hardware"></div>';
        row.querySelector('input').addEventListener('input',event=>{
            const ids=pairs.includes(key)?[key+'1',key+'2']:[key];
            ids.forEach(id=>setHardware(id,event.target.value));
        });sliders.appendChild(row);
    });
    function syncSliders() {
        rows.forEach(([key])=>{
            const row=sliders.querySelector('[data-slider="'+key+'"]'),axis=model[pairs.includes(key)?key+'1':key];
            if(!axis)return;
            row.querySelector('input').value=axis.input;row.querySelector('output').textContent=Math.round(axis.input)+' / '+Math.round(axis.target)+'%';
            row.querySelector('.preview-range').style.setProperty('--target',axis.target+'%');
        });
        document.querySelector('[data-align]').disabled=!groups.some(g=>waiting[g]);
    }
    // These tools represent physical hardware, so their clicks don't dismiss
    // cockpit assignment mode. Ordinary page/frame clicks still do.
    document.querySelector('[data-preview-hardware]').addEventListener('click',event=>event.stopPropagation());
    document.querySelectorAll('[data-scene]').forEach(button=>button.addEventListener('click',()=>reset(button.dataset.scene)));
    document.querySelector('[data-align]').addEventListener('click',()=>{
        definitions.forEach(([id])=>{if(waiting[model[id].group])model[id].input=model[id].target;});emit();syncSliders();
    });
    document.querySelectorAll('[data-size]').forEach(button=>button.addEventListener('click',()=>{
        document.querySelector('[data-device]').toggleAttribute('data-compact',button.dataset.size==='compact');
        document.querySelectorAll('[data-size]').forEach(item=>item.setAttribute('aria-pressed',String(item===button)));
    }));
    const timer=setInterval(()=>{
        groups.forEach(group=>{
            if(!waiting[group]){stable[group]=0;return;}
            if(!axes(group).every(axis=>axis.aligned)){stable[group]=0;return;}
            if(!stable[group])stable[group]=Date.now();
            if(Date.now()-stable[group]>=350){blocked[group]=false;waiting[group]=false;stable[group]=0;say(group+' aligned. Me has control.');}
        });emit();syncSliders();
    },100);
    reset('mixed');controller.start();
    window.fscPreview={controller,commands,reset,setHardware,align(){document.querySelector('[data-align]').click();},snapshot:()=>controller.snapshot};
    if(location.protocol==='http:'&&typeof EventSource==='function') {
        const reload=new EventSource('/__preview/events');reload.addEventListener('refresh',()=>location.reload());
        window.addEventListener('unload',()=>reload.close());
    }
    window.addEventListener('unload',()=>{clearInterval(timer);controller.dispose();unmount();});
})();
