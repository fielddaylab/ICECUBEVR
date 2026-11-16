// Same-origin static server for the Unity WebGL build: pre-gzipped .unityweb get
// Content-Encoding: gzip, streamed .ogg get audio/ogg + Range support.
const http = require('http'), fs = require('fs'), path = require('path');
const root = process.argv[2], port = +process.argv[3] || 8047;
const mime = { '.html':'text/html', '.js':'application/javascript', '.json':'application/json',
  '.css':'text/css', '.png':'image/png', '.ogg':'audio/ogg', '.ico':'image/x-icon' };
http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]);
  if (p.endsWith('/')) p += 'index.html';
  const file = path.join(root, p);
  if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); return res.end(); }
  const ext = path.extname(file), size = fs.statSync(file).size;
  const h = { 'Content-Type': mime[ext] || 'application/octet-stream', 'Accept-Ranges': 'bytes' };
  if (ext === '.unityweb') h['Content-Encoding'] = 'gzip';
  const range = req.headers.range;
  if (range && ext === '.ogg') {
    const m = /bytes=(\d*)-(\d*)/.exec(range);
    const start = m[1] ? +m[1] : 0, end = m[2] ? +m[2] : size - 1;
    h['Content-Range'] = `bytes ${start}-${end}/${size}`; h['Content-Length'] = end - start + 1;
    res.writeHead(206, h); return fs.createReadStream(file, { start, end }).pipe(res);
  }
  h['Content-Length'] = size; res.writeHead(200, h); fs.createReadStream(file).pipe(res);
}).listen(port, '127.0.0.1', () => console.log('serving', root, 'on', port));
