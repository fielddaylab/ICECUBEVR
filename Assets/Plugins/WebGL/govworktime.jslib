// N6 — main-loop work time for the pacing governor.
//
// Deliberately separate from perfprobe.jslib (N1, measurement harness) and
// html5audio.jslib (N14): this one is on the governor's decision path and must
// stay tiny and allocation-free.
//
// GovWorkTime() returns the duration in milliseconds of the *previous*
// main-loop tick, i.e. performance.now() sampled at loop-tick entry and exit.
// The sampling itself lives in the WebGL template's requestAnimationFrame
// wrapper (window.icecubeWorkMs), because that is the only place that brackets
// Unity's whole tick -- including the render submit that happens after the last
// C# Update -- without patching generated emscripten code.
//
// If the shim is missing (e.g. the build was made against the stock Default
// template) a local fallback wrapper is installed here on first call so the
// governor still gets real numbers instead of silently flatlining at 0. The
// fallback does not half-rate anything; it only measures. A one-time console
// warning names the missing template so a mis-built index.html is obvious.
//
// Contract: never throws into Unity, returns 0 when no sample is available yet
// (first frames, hidden tab, no rAF at all).
//
// NOTE on style: emscripten serialises each library function with toString()
// and pastes it into the generated JS, so these functions cannot close over
// file-level variables. All state is kept on window.__icecubeGov instead, and
// helpers are $-prefixed library members pulled in through __deps; emscripten
// emits them WITHOUT the leading underscore (GovState, not _GovState).
mergeInto(LibraryManager.library, {

  $GovState: function () {
    if (typeof window === 'undefined') { return null; }
    var s = window.__icecubeGov;
    if (!s) {
      s = { warned: false, fallbackInstalled: false, fallbackWorkMs: 0 };
      window.__icecubeGov = s;
    }
    return s;
  },

  $GovInstallFallback__deps: ['$GovState'],
  $GovInstallFallback: function () {
    var s = GovState();
    if (!s || s.fallbackInstalled) { return s; }
    s.fallbackInstalled = true;
    try {
      if (typeof window.requestAnimationFrame !== 'function') { return s; }
      var nativeRaf = window.requestAnimationFrame.bind(window);
      var now = (window.performance && window.performance.now)
        ? function () { return window.performance.now(); }
        : function () { return Date.now(); };
      window.requestAnimationFrame = function (cb) {
        if (typeof cb !== 'function') { return 0; }
        return nativeRaf(function (ts) {
          var t0 = now();
          try { cb(ts); } finally {
            var w = now() - t0;
            if (w >= 0 && isFinite(w)) { s.fallbackWorkMs = w; }
          }
        });
      };
    } catch (e) { /* keep the zero-valued fallback */ }
    return s;
  },

  GovWorkTime__deps: ['$GovState', '$GovInstallFallback'],
  GovWorkTime: function () {
    try {
      if (typeof window === 'undefined') { return 0; }

      if (typeof window.icecubeWorkMs === 'function') {
        var v = window.icecubeWorkMs();
        if (typeof v !== 'number' || !isFinite(v) || v < 0) { return 0; }
        return v;
      }

      var s = GovState();
      if (!s) { return 0; }
      if (!s.warned) {
        s.warned = true;
        try {
          console.warn('[icecube] GOV rAF shim missing from index.html ' +
                       '(expected WebGLTemplates/Icecube); using jslib fallback timer, ' +
                       'frame pacing is NOT locked to 30');
        } catch (e) { /* ignore */ }
      }
      GovInstallFallback();
      if (typeof document !== 'undefined' && document.hidden) { return 0; }
      var w = s.fallbackWorkMs;
      return (typeof w === 'number' && isFinite(w) && w >= 0) ? w : 0;
    } catch (e) {
      return 0;
    }
  },

  // 1 when the template shim is the thing providing the samples.
  GovShimPresent: function () {
    try {
      return (typeof window !== 'undefined' &&
              window.icecubeRaf &&
              typeof window.icecubeWorkMs === 'function') ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  GovHalfRate: function () {
    try {
      return (typeof window !== 'undefined' && window.icecubeRaf && window.icecubeRaf.halfRate) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }
});
