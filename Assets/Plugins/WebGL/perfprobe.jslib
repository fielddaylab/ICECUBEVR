// PerfProbe — real-hardware measurement harness (card N1).
// Exposes PerfProbe_Env() and PerfProbe_Frame() to Unity (WebGL only).
// All output lines are tagged "PERFPROBE" and the functions never throw
// into Unity.
//
// IMPORTANT (emscripten jslib contract): a .jslib is NOT evaluated as a
// script. Emscripten parses it, takes the members of the object passed to
// mergeInto, and emits each one as a TOP-LEVEL function in the framework
// JS. Any enclosing IIFE and every closure variable it held is DISCARDED.
// Wrapping this file in `(function(){...})()` therefore produced
// `ReferenceError: safeLog is not defined` at the first Update(), which
// Unity surfaced as "PlayerLoop internal function has been called
// recursively" (the exception escaped the JS->managed boundary each frame).
//
// Shared state and helpers must live on a `$Name` pseudo-member and be
// declared as a dependency, exactly like html5audio.jslib does with
// $Html5MusicState.

var PerfProbeLibrary = {

  $PerfProbeState: {
    RING_LEN: 1024,
    DUMP_INTERVAL_MS: 10000,
    WARMUP_FRAMES: 30,
    SKIP_MS: 1000,          // deltas larger than this (hidden tab) are ignored

    ring: null,
    head: 0,
    count: 0,
    loaded: 0,
    lastDumpTime: 0,
    tier: 'UNKNOWN',
    renderer: null,

    ensureRing: function () {
      if (!this.ring) {
        this.ring = new Float64Array(this.RING_LEN);
      }
      return this.ring;
    },

    safeLog: function (line) {
      try {
        console.log(line);
      } catch (e) { /* never throw */ }
    },

    r3: function (x) { return Math.round(x * 1000) / 1000; },

    // Resolve the live GL context and canvas without ever throwing.
    resolveGl: function () {
      var gl = null;
      var canvas = null;
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
      return { gl: gl, canvas: canvas };
    }
  },

  // ---- public: environment probe ------------------------------------------
  PerfProbe_Env__deps: ['$PerfProbeState'],
  PerfProbe_Env: function () {
    var S = PerfProbeState;
    try {
      if (typeof window === 'undefined' || typeof performance === 'undefined') { return; }

      var found = S.resolveGl();
      var gl = found.gl;
      var canvas = found.canvas;
      var rendererStr = null;
      var vendor = null;
      var debugRendererInfo = true;

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
      S.tier = nextTier;
      S.renderer = rendererStr;

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
        tier: S.tier,
        devicePixelRatio: S.r3(dpr),
        canvas_width: cw,
        canvas_height: ch,
        canvas_client_width: cclw,
        canvas_client_height: cclh,
        buffer_scale_x: S.r3(bsx),
        buffer_scale_y: S.r3(bsy),
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

      S.safeLog('PERFPROBE env ' + JSON.stringify(obj));
    } catch (e) {
      try {
        S.safeLog('PERFPROBE env ' + JSON.stringify({ error: String(e && e.message ? e.message : e) }));
      } catch (e2) { /* never throw into Unity */ }
    }
  },

  // ---- public: per-frame timing ------------------------------------------
  PerfProbe_Frame__deps: ['$PerfProbeState'],
  PerfProbe_Frame: function () {
    var S = PerfProbeState;
    try {
      if (typeof window === 'undefined' || typeof performance === 'undefined') { return; }
      var ring = S.ensureRing();
      var now = performance.now();

      ring[S.head] = now;
      S.head = (S.head + 1) % S.RING_LEN;
      if (S.count < S.RING_LEN) { S.count++; }
      S.loaded++;

      if (S.lastDumpTime === 0) { S.lastDumpTime = now; }
      if (now - S.lastDumpTime < S.DUMP_INTERVAL_MS) { return; }
      S.lastDumpTime = now;

      // timestamps in ring order (oldest -> newest)
      var n = S.count;
      var ts = new Array(n);
      var start = (S.count >= S.RING_LEN) ? S.head : 0;
      for (var i = 0; i < n; i++) {
        ts[i] = ring[(start + i) % S.RING_LEN];
      }

      // warm-up: drop the first WARMUP_FRAMES frames after load
      var skipFirst = 0;
      if (S.loaded > S.WARMUP_FRAMES) {
        skipFirst = Math.min(S.WARMUP_FRAMES, n - 1);
      }

      var deltas = [];
      var skipped = 0;
      for (var j = skipFirst + 1; j < n; j++) {
        var d = ts[j] - ts[j - 1];
        if (d < 0) { d = 0; }
        if (d > S.SKIP_MS) { skipped++; continue; }
        deltas.push(d);
      }

      if (deltas.length < 2) {
        S.safeLog('PERFPROBE frame ' + JSON.stringify({
          window_s: 10,
          n: 0,
          skipped: skipped,
          tier: S.tier,
          warmup: S.loaded <= S.WARMUP_FRAMES
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

      S.safeLog('PERFPROBE frame ' + JSON.stringify({
        window_s: 10,
        n: m,
        p50_ms: Math.round(p50 * 100) / 100,
        p95_ms: Math.round(p95 * 100) / 100,
        mean_ms: Math.round(mean * 100) / 100,
        max_ms: Math.round(max * 100) / 100,
        jitter_pct: jitterPct,
        skipped: skipped,
        tier: S.tier
      }));
    } catch (e) {
      try {
        S.safeLog('PERFPROBE frame ' + JSON.stringify({ error: String(e && e.message ? e.message : e) }));
      } catch (e2) { /* never throw into Unity */ }
    }
  }
};

autoAddDeps(PerfProbeLibrary, '$PerfProbeState');
mergeInto(LibraryManager.library, PerfProbeLibrary);
