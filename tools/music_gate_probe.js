// N15 autoplay-gate probe. Drives a served WebGL build over CDP (chrome --headless=new
// --remote-debugging-port=<port>) and records: MUSICCUE/MUSICGATE console lines, every
// .ogg request/response, HTMLMediaElement.play() calls + their promise outcome, and the
// <audio> element state. Asserts nothing itself; the JSON it writes is the evidence.
//   node tools/music_gate_probe.js <cdp-port> <url> <out.json>
// Serve the build same-origin first: node tools/serve_webgl.js <build-dir> 8047
// Pass criteria (see tools/MUSIC_CUES.md §6): PRE-GESTURE gate=0 ogg=0 audio=[];
// after the BEGIN dwell MUSICGATE, then NET/RESP 206 audio/ogg, PLAYCALLS ok=true.
const port = +process.argv[2] || 9233, url = process.argv[3], out = process.argv[4] || '/tmp/n15_gate2.json';
const fs = require('fs');
(async () => {
  const tabs = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
  const page = tabs.find(t => t.type === 'page');
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let seq = 0, armed = false, loaded = false; const waiting = new Map(); const logs = []; const oggReqs = []; const t0 = Date.now();
  const stamp = () => ((Date.now() - t0) / 1000).toFixed(1);
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m.result || m.error); waiting.delete(m.id); }
    if (m.method === 'Page.loadEventFired') loaded = true;
    if (!armed) return;
    if (m.method === 'Runtime.consoleAPICalled') {
      const s = m.params.args.map(a => String(a.value ?? a.description ?? '')).join(' ');
      if (/MUSIC|GOVERNOR|PERFPROBE|Html5Music|rror/.test(s)) logs.push(`[${stamp()}] ${s.split('\n')[0].slice(0, 200)}`);
    }
    if (m.method === 'Network.requestWillBeSent' && /\.ogg/.test(m.params.request.url)) {
      oggReqs.push({ t: +stamp(), url: m.params.request.url });
      logs.push(`[${stamp()}] NET ${m.params.request.url}`);
    }
    if (m.method === 'Network.responseReceived' && /\.ogg/.test(m.params.response.url)) {
      const r = m.params.response; logs.push(`[${stamp()}] RESP ${r.status} ${r.mimeType} ${r.url}`);
    }
  };
  const send = (method, params = {}) => new Promise(res => { const id = ++seq; waiting.set(id, res); ws.send(JSON.stringify({ id, method, params })); });
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const evaluate = async expr => (await send('Runtime.evaluate', { expression: expr, returnByValue: true })).result?.value;
  const audioState = () => evaluate(`JSON.stringify((function(){var o=[];(window.__media||[]).forEach(function(a){o.push({src:a.src.split('/').slice(-2).join('/'),paused:a.paused,ended:a.ended,t:+a.currentTime.toFixed(2),rs:a.readyState,vol:a.volume})});return o})())`);
  const canvasInfo = async () => JSON.parse(await evaluate(`JSON.stringify((function(){var c=document.querySelector('canvas');var r=c.getBoundingClientRect();return {w:c.width,h:c.height,cw:r.width,ch:r.height}})())`));
  await send('Page.enable'); await send('Network.enable');
  await send('Page.addScriptToEvaluateOnNewDocument', { source: "window.__media=[];window.__playCalls=[];var _p=HTMLMediaElement.prototype.play;HTMLMediaElement.prototype.play=function(){if(window.__media.indexOf(this)<0)window.__media.push(this);var r=_p.apply(this,arguments);var self=this;window.__playCalls.push({src:this.src,t:performance.now(),ok:null});var rec=window.__playCalls[window.__playCalls.length-1];if(r&&r.then)r.then(function(){rec.ok=true},function(e){rec.ok=e.name});return r;}" });
  await send('Page.navigate', { url: 'about:blank' }); await sleep(1000);
  await send('Runtime.enable');            // console buffer is now empty
  armed = true; loaded = false;
  await send('Page.navigate', { url });
  logs.push(`[${stamp()}] navigate ${url}`);
  while (!loaded) await sleep(500);
  const bootEnd = Date.now() + 180000;
  while (Date.now() < bootEnd && !logs.some(l => l.includes('MUSICCUE'))) await sleep(2000);
  await sleep(3000);
  const preGesture = { cues: logs.filter(l => l.includes('MUSICCUE')), gate: logs.filter(l => l.includes('MUSICGATE')), ogg: oggReqs.length, audio: await audioState() };
  logs.push(`[${stamp()}] PRE-GESTURE cues=${preGesture.cues.length} gate=${preGesture.gate.length} ogg=${preGesture.ogg} audio=${preGesture.audio}`);
  // let the governor settle (no new scale line for 10 s, and at least 45 s since load)
  const settleStart = Date.now();
  for (;;) { const n = logs.filter(l => l.includes('GOVERNOR scale')).length; await sleep(10000);
    if (logs.filter(l => l.includes('GOVERNOR scale')).length === n && Date.now() - settleStart > 45000) break;
    if (Date.now() - settleStart > 150000) break; }
  const cv = await canvasInfo(); logs.push(`[${stamp()}] CANVAS ${JSON.stringify(cv)}`);
  const midGesture = { cues: logs.filter(l => l.includes('MUSICCUE')).length, gate: logs.filter(l => l.includes('MUSICGATE')).length, ogg: oggReqs.length, audio: await audioState() };
  logs.push(`[${stamp()}] PRE-GESTURE(2) cues=${midGesture.cues} gate=${midGesture.gate} ogg=${midGesture.ogg} audio=${midGesture.audio}`);
  // gesture: SPACE down/up (-> hmd_mounted on KeyUp) then click to capture mouse
  const key = async (type) => send('Input.dispatchKeyEvent', { type, key: ' ', code: 'Space', windowsVirtualKeyCode: 32, nativeVirtualKeyCode: 32, text: type==='char'?' ':undefined });
  await key('keyDown'); await key('char'); await sleep(100); await key('keyUp'); await sleep(1000);
  let px = 640, py = 360;
  await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: px, y: py });
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: px, y: py, button: 'left', clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: px, y: py, button: 'left', clickCount: 1 });
  logs.push(`[${stamp()}] GESTURE space+click sent`);
  await sleep(1000);
  const move = async (dx, dy) => { px += dx; py += dy; await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: px, y: py, movementX: dx, movementY: dy }); };
  await move(1, 0); await sleep(300); await move(-1, 0); await sleep(300);   // eat mouse_just_captured
  // Unity target: mouse_x = W/2-13.3, mouse_y = H/2+12 where W,H = current Screen size; mouse starts at 640,360.
  const scales = [1, cv.w / cv.cw];                      // DOM px -> unity px hypotheses
  let gateAt = null; let cur = { x: 0, y: 0 };
  const goto = async (ux, uy) => { await move(ux - cur.x, uy - cur.y); cur = { x: ux, y: uy }; };
  outer: for (const s of scales) {
    const tx = (cv.w / 2 - 13.3 - 640) / s, ty = -((cv.h / 2 + 12) - 360) / s;   // DOM dx, dy (DOM y down)
    logs.push(`[${stamp()}] AIM scale=${s.toFixed(3)} base dom dx=${tx.toFixed(0)} dy=${ty.toFixed(0)}`);
    const step = Math.max(1, Math.round(2 / s));
    const grid = []; for (let r = 0; r <= 4; r++) for (let i = -r; i <= r; i++) for (let j = -r; j <= r; j++) if (Math.max(Math.abs(i), Math.abs(j)) === r) grid.push([i, j]);
    for (const [i, j] of grid) {
      await goto(Math.round(tx + i * step), Math.round(ty + j * step));
      for (let k = 0; k < 3 && !gateAt; k++) { await sleep(500); if (logs.some(l => l.includes('MUSICGATE'))) gateAt = stamp(); }
      if (gateAt) { logs.push(`[${stamp()}] HIT scale=${s.toFixed(3)} grid ${i},${j}`); break outer; }
    }
  }
  await sleep(10000); logs.push(`[${stamp()}] AUDIO+10s ${await audioState()}`); await sleep(10000);
  const playCalls = await evaluate('JSON.stringify(window.__playCalls)'); logs.push(`[${stamp()}] PLAYCALLS ${playCalls}`);
  const post = { gateAt, playCalls, cues: logs.filter(l => l.includes('MUSICCUE')), gate: logs.filter(l => l.includes('MUSICGATE')), ogg: oggReqs, audio: await audioState() };
  logs.push(`[${stamp()}] POST gate=${gateAt} ogg=${oggReqs.length} audio=${post.audio}`);
  const shot = (await send('Page.captureScreenshot', { format: 'png' })).data;
  if (shot) fs.writeFileSync(out.replace(/\.json$/, '.png'), Buffer.from(shot, 'base64'));
  fs.writeFileSync(out, JSON.stringify({ url, preGesture, midGesture, post, logs }, null, 2));
  console.log(logs.join('\n')); process.exit(0);
})().catch(e => { console.error(e); process.exit(2); });
