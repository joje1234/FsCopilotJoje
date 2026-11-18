// Inspect actual SDK-produced WASM, not the desktop test binary. MSFS's native
// recompilation has documented problems with frames of 65536 bytes or more.
const assert=require('node:assert/strict'),fs=require('node:fs');
const file=process.argv[2];assert(file,'Pass the SDK-built .wasm path');
const bytes=fs.readFileSync(file);new WebAssembly.Module(bytes);
let p=8;
function uint(){let value=0,shift=0,byte;do{assert(p<bytes.length);byte=bytes[p++];value+=(byte&127)*2**shift;shift+=7;assert(shift<=35);}while(byte&128);return value;}
function sint(){let value=0,shift=0,byte;do{assert(p<bytes.length);byte=bytes[p++];value|=(byte&127)<<shift;shift+=7;assert(shift<=35);}while(byte&128);return shift<32&&(byte&64)?value|(~0<<shift):value;}
function string(){const length=uint(),text=bytes.subarray(p,p+length).toString();p+=length;return text;}
function limits(){const flags=uint();uint();if(flags&1)uint();}
const sections={};while(p<bytes.length){const id=bytes[p++],length=uint(),end=p+length;assert(end<=bytes.length);sections[id]=[p,end];p=end;}
assert(sections[2]&&sections[7]&&sections[10],'Missing WASM module sections');
p=sections[2][0];let imports=0;const imported=uint();
for(let i=0;i<imported;i++){string();string();switch(bytes[p++]){case 0:uint();++imports;break;case 1:++p;limits();break;case 2:limits();break;case 3:p+=2;break;default:throw Error('Unsupported WASM import kind');}}
p=sections[7][0];const exported=uint(),names=new Map();
for(let i=0;i<exported;i++){const name=string(),kind=bytes[p++],index=uint();if(kind===0){const list=names.get(index)||[];list.push(name);names.set(index,list);}}
function frame(end){
 let locals=uint();while(locals--){uint();++p;}
 if(bytes[p++]!==0x23)return null;const stack=uint();let allocation=null,subtracted=false;
 // Clang's optimized and unoptimized stack prologues use these operations.
 for(let op=0;op<16&&p<end;op++)switch(bytes[p++]){
  case 0x20:case 0x21:case 0x22:uint();break;
  case 0x41:if(allocation!==null)return null;allocation=sint();break;
  case 0x6b:subtracted=true;break;
  case 0x24:return uint()===stack&&subtracted&&allocation>0?allocation:null;
  default:return null;
 }
 return null;
}
p=sections[10][0];const functions=uint(),frames=[];
for(let i=0;i<functions;i++){const size=uint(),end=p+size;assert(end<=sections[10][1]);const allocation=frame(end);if(allocation!==null)frames.push({names:names.get(imports+i)||['function '+(imports+i)],bytes:allocation});p=end;}
const initialize=frames.find(row=>row.names.includes('module_init'));assert(initialize,'Cannot inspect module_init stack prologue');
const oversized=frames.filter(row=>row.bytes>=65536);
assert.equal(oversized.length,0,'MSFS stack frame exceeds 64 KiB: '+JSON.stringify(oversized));
console.log('PASS compiled WASM stack budget: module_init '+initialize.bytes+' bytes; largest frame '+Math.max(...frames.map(row=>row.bytes))+' bytes. No simulator access.');
