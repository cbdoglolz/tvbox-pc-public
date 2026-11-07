// GPL-3.0. Runs only bundled, reviewed adapters. No runtime script download or profile access.
import fs from 'node:fs/promises';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import { lookup } from 'node:dns/promises';
import { isIP } from 'node:net';
const root = path.dirname(fileURLToPath(import.meta.url));
const input = JSON.parse(await new Promise(resolve => { let s=''; process.stdin.setEncoding('utf8'); process.stdin.on('data', d => { s+=d; if(s.length>65536) process.exit(2); }); process.stdin.on('end',()=>resolve(s)); }));
const id = process.argv[2];
if (!['4khdhub','anizone','vixsrc'].includes(id)) throw Error('Unknown bundled adapter');
const source = await fs.readFile(path.join(root,id+'.cjs'),'utf8');
const cheerioText = await fs.readFile(path.join(root,'../js/lib/cheerio.min.js'),'utf8');
const cheerio = await import('data:text/javascript;base64,'+Buffer.from(cheerioText).toString('base64'));
let count=0, total=0; const trace=[];
function note(...args) { const line=args.map(String).join(" ").replace(/https?:\/\/[^\s]+/g, "<remote-url>").slice(0,280); trace.push(line); if(trace.length>30)trace.shift(); }
function privateAddress(ip) {
 if (ip.includes(':')) { const v=ip.toLowerCase(); return v==='::1'||v==='::'||v.startsWith('fc')||v.startsWith('fd')||v.startsWith('fe80')||v.startsWith('::ffff:'); }
 const n=ip.split('.').map(Number); return n[0]===0||n[0]===10||n[0]===127||n[0]>=224||(n[0]===169&&n[1]===254)||(n[0]===172&&n[1]>=16&&n[1]<=31)||(n[0]===192&&n[1]===168);
}
async function check(url) { const u=new URL(url); if(!['http:','https:'].includes(u.protocol)||u.username||u.password) throw Error('Unsupported URL'); if(u.hostname==='localhost'||u.hostname.endsWith('.local')) throw Error('Local endpoint blocked'); const addresses=isIP(u.hostname)?[{address:u.hostname}]:await lookup(u.hostname,{all:true}); if(!addresses.length||addresses.some(x=>privateAddress(x.address))) throw Error('Local endpoint blocked'); return u; }
async function request(url, options={}) {
 let current=new URL(String(url));
 // Metadata is supplied by the app; no personal or upstream API key reaches the adapter.
 if(current.hostname==='api.themoviedb.org') return new Response(JSON.stringify({title:input.title,name:input.title,original_title:input.originalTitle,original_name:input.originalTitle,release_date:input.year+'-01-01',first_air_date:input.year+'-01-01',imdb_id:input.imdb||''}),{headers:{'Content-Type':'application/json'}});
 if(++count>100) throw Error('Request budget exceeded');
 for(let redirects=0;redirects<5;redirects++) {
  await check(current);
  const response=await fetch(current,{...options,redirect:'manual',signal:AbortSignal.timeout(12000)});
  if(response.status>=300&&response.status<400&&response.headers.get('location')) { const next=new URL(response.headers.get('location'),current); if(next.origin!==current.origin) options={...options,headers:{}}; current=next; await response.body?.cancel(); continue; }
  const reader=response.body?.getReader(); const chunks=[];let bytes=0;
  if(reader) { while(true) { const part=await reader.read(); if(part.done)break; bytes+=part.value.length;total+=part.value.length; if(bytes>4*1024*1024||total>24*1024*1024) { await reader.cancel(); throw Error('Response budget exceeded'); } chunks.push(part.value); } }
  return new Response(Buffer.concat(chunks),{status:response.status,headers:response.headers});
 }
 throw Error('Redirect limit');
}
const module={exports:{}};
const context=vm.createContext({module,exports:module.exports,require:name=>{if(name==='cheerio-without-node-native')return cheerio;throw Error('Module unavailable');},fetch:request,console:{log:note,warn:note,error:note},setTimeout,clearTimeout,URL,URLSearchParams,TextEncoder,TextDecoder,Uint8Array,atob,btoa});
new vm.Script(source,{filename:id+'.cjs'}).runInContext(context,{timeout:2000});
try { const streams=await module.exports.getStreams(String(input.tmdbId),input.kind,input.season,input.episode); process.stdout.write(JSON.stringify({streams:Array.isArray(streams)?streams.slice(0,30):[],requests:count,diagnostics:trace})); }
catch { process.stdout.write(JSON.stringify({streams:[],error:'Adapter request failed',requests:count,diagnostics:trace})); }
