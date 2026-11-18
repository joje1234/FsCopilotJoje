// Local static UI preview with live reload. No simulator or external endpoints.
import http from 'node:http';
import { readFile, watch } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const files=new Map([
    ['/preview.html','text/html'],['/preview.css','text/css'],['/preview.js','text/javascript'],
    ['/src/controls.css','text/css'],['/src/controller.js','text/javascript'],['/src/view.js','text/javascript']
]);
const clients=new Set();
let refresh;
for(const file of files.keys())watch(resolve(root,file.slice(1)),()=>{
    clearTimeout(refresh);refresh=setTimeout(()=>{for(const client of clients)client.write('event: refresh\ndata: changed\n\n');},150);
});
const server=http.createServer((req,res)=>{
    const path=new URL(req.url,'http://127.0.0.1').pathname;
    if(req.method!=='GET'){res.writeHead(405);res.end();return;}
    if(path==='/'){res.writeHead(302,{Location:'/preview.html'});res.end();return;}
    if(path==='/__preview/events'){
        res.writeHead(200,{'Content-Type':'text/event-stream','Cache-Control':'no-store','Connection':'keep-alive'});
        res.write(': preview connected\n\n');clients.add(res);req.on('close',()=>clients.delete(res));return;
    }
    if(!files.has(path)){res.writeHead(404);res.end('Not found');return;}
    readFile(resolve(root,path.slice(1)),(error,content)=>{
        if(error){res.writeHead(500);res.end('Preview file unavailable');return;}
        res.writeHead(200,{'Content-Type':files.get(path)+'; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(content);
    });
});
const arg=process.argv.find(value=>value.startsWith('--port='));
const port=arg?Number(arg.slice(7)):4173;
if(!Number.isInteger(port)||port<0||port>65535)throw new Error('Invalid preview port.');
server.listen(port,'127.0.0.1',()=>console.log('FS Copilot UI preview: http://127.0.0.1:'+server.address().port+'/preview.html'));
server.on('error',error=>{console.error(error.message);process.exit(1);});
process.on('SIGINT',()=>process.exit(0));
