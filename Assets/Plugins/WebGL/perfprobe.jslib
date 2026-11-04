// PerfProbe — real-hardware measurement harness (card N1).
// Exposes PerfProbe_Env() and PerfProbe_Frame() to Unity (WebGL only).
// All output lines are tagged "PERFPROBE" and the functions never throw
// into Unity.
(function () {
  'use strict';

  // Module-scoped state: survives repeated Env calls, ring buffer lives here.
  var RING_LEN = 1024;
  var DUMP_INTERVAL_MS = 10000;
  var WARMUP_FRAMES = 30;
  var SKIP_MS = 1000; // deltas larger than this (hidden tab) are ignored
  var ring = null;
  var head = 0;
  var count = 0;
  var loaded = 0;
  var lastDumpTime = 0;
  var tier = 'UNKNOWN';
  var renderer = null;

  function state() {
    if (!ring) {
      ring = new Float64Array(RING_LEN);
    }
    return { ring: ring, head: head, count: count, loaded: loaded };
  }

  function safeLog(line) {
    try {
      console.log(line);
    } catch (e) { /* never throw */ }
  }

  // ---- public: environment probe ------------------------------------------
  function PerfProbe_Env() {
    try {
      if (typeof window === 'undefined' || typeof performance === 'undefined') { return; }
      var gl = null;
      var canvas = null;
      var rendererStr = null;
      var vendor = null;
      var debugRendererInfo = true;

      try {
        if (typeof GLctx !== 'undefined' && GLctx) { gl = GLctx; }
        if (!gl && typeof Module !== 'undefined' && Module.ctx) { gl = Module.ctx; }
        if (typeof Module !== 'undefined' && Module.canvas) {
          canvas = Module.canvas;
          if (!gl) {
            try { gl = canvas.getContext('webgl2') || canvas.getContext('webgl'); } catch (e) { gl = null; }
          }
        }
        if (!canvas && typeof document !== 'undefined') {
          canvas = document.querySelector('#unity-canvas') || document.querySelector('canvas');
          if (canvas && !gl) {
            try { gl = canvas.getContext('webgl2') || canvas.getContext('webgl'); } catch (e) { gl = null; }
          }
        }
      } catch (e) { /* ignore */ }

      var ext = null;
      if (gl) {
        try { ext = gl.getExtension('WEBGL_debug_renderer_info'); } catch (e) { ext = null; }
        if (ext) {
          try {
            rendererStr = gl.getParameter(ext.UNMASKED_RENDERER_WEBGL);
            vendor = gl.getParameter(ext.UNMASKED_VENDOR_WEBGL);
          } catch (e) { /* ignore */ }
        } else {
          // Kill-criterion fallback: no debug renderer info extension.
          debugRendererInfo = false;
          try { rendererStr = gl.getParameter(gl.RENDERER); } catch (e) { rendererStr = null; }
        }
      }

      var nextTier = 'UNKNOWN';
      if (/swiftshader|llvmpipe|softpipe|software|mesa offscreen/i.test(rendererStr || '')) {
        nextTier = 'SOFTWARE';
      } else if (rendererStr) {
        nextTier = 'HARDWARE';
      }
      tier = nextTier;
      renderer = rendererStr;

      var dpr = 1;
      try { dpr = (window.devicePixelRatio !== undefined) ? window.devicePixelRatio : 1; } catch (e) { dpr = 1; }

      var cw = 0, ch = 0, cclw = 0, cclh = 0;
      if (canvas) {
        cw = canvas.width | 0;
        ch = canvas.height | 0;
        cclw = canvas.clientWidth | 0;
        cclh = canvas.clientHeight | 0;
      }

      var bsx = (cclw > 0) ? cw / cclw : 0;
      var bsy = (cclh > 0) ? ch / cclh : 0;
      function r3(x) { return Math.round(x * 1000) / 1000; }

      var s3tc = false, etc1 = false, astc = false, webglVersion = 0;
      if (gl) {
        try {
          webglVersion = (typeof WebGL2RenderingContext !== 'undefined' && gl instanceof WebGL2RenderingContext) ? 2 : 1;
        } catch (e) { webglVersion = 1; }
        try { s3tc = !!(gl.getExtension('WEBGL_compressed_texture_s3tc') || gl.getExtension('WEBKIT_WEBGL_compressed_texture_s3tc')); } catch (e) {}
        try { etc1 = !!(gl.getExtension('WEBGL_compressed_texture_etc1') || gl.getExtension('WEBKIT_WEBGL_compressed_texture_etc1')); } catch (e) {}
        try { astc = !!gl.getExtension('WEBGL_compressed_texture_astc'); } catch (e) {}
      }

      var obj = {
        renderer: rendererStr,
        vendor: vendor,
        debug_renderer_info: debugRendererInfo,
        tier: tier,
        devicePixelRatio: r3(dpr),
        canvas_width: cw,
        canvas_height: ch,
        canvas_client_width: cclw,
        canvas_client_height: cclh,
        buffer_scale_x: r3(bsx),
        buffer_scale_y: r3(bsy),
        one_to_one: (cclw > 0 && cclh > 0)
          ? (Math.abs(bsx - dpr) < 0.02 && Math.abs(bsy - dpr) < 0.02)
          : false,
        window_inner_width: (window.innerWidth | 0),
        window_inner_height: (window.innerHeight | 0),
        webgl_version: webglVersion,
        s3tc: s3tc,
        etc1: etc1,
        astc: astc,
        userAgent: (typeof navigator !== 'undefined') ? navigator.userAgent : null
      };

      if (!debugRendererInfo) {
        obj.note = 'WEBGL_debug_renderer_info unavailable; paste chrome://gpu GL_RENDERER manually';
      }

      safeLog('PERFPROBE env ' + JSON.stringify(obj));
    } catch (e) {
      safeLog('PERFPROBE env ' + JSON.stringify({ error: String(e && e.message ? e.message : e) }));
    }
  }

  // ---- public: per-frame timing ------------------------------------------
  function PerfProbe_Frame() {
    try {
      if (typeof window === 'undefined' || typeof performance === 'undefined') { return; }
      var st = state();
      var now = performance.now();

      ring[head] = now;
      head = (head + 1) % RING_LEN;
      if (count < RING_LEN) { count++; }
      loaded++;

      if (lastDumpTime === 0) { lastDumpTime = now; }
      if (now - lastDumpTime < DUMP_INTERVAL_MS) { return; }
      lastDumpTime = now;

      // timestamps in ring order (oldest -> newest)
      var n = count;
      var ts = new Array(n);
      var start = (count >= RING_LEN) ? head : 0;
      for (var i = 0; i < n; i++) {
        ts[i] = ring[(start + i) % RING_LEN];
      }

      // warm-up: drop the first WARMUP_FRAMES frames after load
      var skipFirst = 0;
      if (loaded > WARMUP_FRAMES) {
        skipFirst = Math.min(WARMUP_FRAMES, n - 1);
      }

      var deltas = [];
      var skipped = 0;
      for (var j = skipFirst + 1; j < n; j++) {
        var d = ts[j] - ts[j - 1];
        if (d < 0) { d = 0; }
        if (d > SKIP_MS) { skipped++; continue; }
        deltas.push(d);
      }

      if (deltas.length < 2) {
        safeLog('PERFPROBE frame ' + JSON.stringify({
          window_s: 10,
          n: 0,
          skipped: skipped,
          tier: tier,
          warmup: loaded <= WARMUP_FRAMES
        }));
        return;
      }

      deltas.sort(function (a, b) { return a - b; });
      var m = deltas.length;
      var sum = 0;
      for (var k = 0; k < m; k++) { sum += deltas[k]; }
      var mean = sum / m;
      var varSum = 0;
      for (var k2 = 0; k2 < m; k2++) { varSum += (deltas[k2] - mean) * (deltas[k2] - mean); }
      var stddev = Math.sqrt(varSum / m);
      var p50 = deltas[Math.floor(0.5 * m)];
      var p95 = deltas[Math.min(m - 1, Math.floor(0.95 * m))];
      var max = deltas[m - 1];
      var jitterPct = (mean > 0) ? Math.round((stddev / mean) * 10000) / 100 : 0;

      safeLog('PERFPROBE frame ' + JSON.stringify({
        window_s: 10,
        n: m,
        p50_ms: Math.round(p50 * 100) / 100,
        p95_ms: Math.round(p95 * 100) / 100,
        mean_ms: Math.round(mean * 100) / 100,
        max_ms: Math.round(max * 100) / 100,
        jitter_pct: jitterPct,
        skipped: skipped,
        tier: tier
      }));
    } catch (e) {
      safeLog('PERFPROBE frame ' + JSON.stringify({ error: String(e && e.message ? e.message : e) }));
    }
  }

  // Expose to Unity (WebGL emscripten LibraryManager).
  mergeInto(LibraryManager.library, {
    PerfProbe_Env: PerfProbe_Env,
    PerfProbe_Frame: PerfProbe_Frame
  });
})();
