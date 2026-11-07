// html5audio.jslib -- stream music through a browser <audio> element instead of
// letting Unity 2019 WebGL decode every music clip into float PCM up front.
//
// Exposed to C# via [DllImport("__Internal")] in Html5Music.cs:
//   Music_SetBasePath(char* url)
//   Music_Play(char* name, int loop, float volume)
//   Music_Stop(int fadeMs)
//   Music_SetVolume(float v)
//   Music_IsPlaying() -> int
//   Music_GetTime() -> float
//   Music_SetTime(float seconds)
//
// Browser autoplay policies require the first play() to happen inside (or after)
// a user gesture. If play() is rejected with NotAllowedError we park the request
// in state.pendingPlay and install one-shot gesture listeners that retry it.

var Html5MusicLibrary = {

  $Html5MusicState: {
    basePath: '',
    tracks: {},            // name -> HTMLAudioElement
    current: null,         // name of the track we last asked to play
    masterVolume: 1.0,
    trackVolume: 1.0,      // per-track volume last requested by C#
    pendingPlay: null,     // { name, loop, volume } queued until a user gesture
    fadeTimer: null,
    gestureListenerInstalled: false,
    gestureEvents: ['pointerdown', 'mousedown', 'touchstart', 'keydown'],

    clamp01: function (v) {
      if (!(v >= 0)) return 0;   // also catches NaN
      if (v > 1) return 1;
      return v;
    },

    element: function (name) {
      if (this.tracks[name]) return this.tracks[name];
      var a = new Audio();
      a.crossOrigin = 'anonymous';
      a.preload = 'auto';
      a.loop = false;
      a.src = this.basePath + name + '.ogg';
      this.tracks[name] = a;
      return a;
    },

    cancelFade: function () {
      if (this.fadeTimer !== null) {
        clearInterval(this.fadeTimer);
        this.fadeTimer = null;
      }
    },

    // Hard stop: no fade, rewind, forget any queued play for that track.
    halt: function (name) {
      var a = this.tracks[name];
      if (!a) return;
      try {
        a.pause();
        a.currentTime = 0;
      } catch (e) { /* element may not be seekable yet */ }
    },

    stopAllExcept: function (keep) {
      for (var n in this.tracks) {
        if (n !== keep) this.halt(n);
      }
    },

    installGestureListener: function () {
      if (this.gestureListenerInstalled) return;
      if (typeof document === 'undefined' || !document.addEventListener) return;
      this.gestureListenerInstalled = true;
      var self = this;
      var onGesture = function () {
        for (var i = 0; i < self.gestureEvents.length; i++) {
          document.removeEventListener(self.gestureEvents[i], onGesture, true);
        }
        self.gestureListenerInstalled = false;
        var queued = self.pendingPlay;
        self.pendingPlay = null;
        if (queued) self.start(queued.name, queued.loop, queued.volume);
      };
      for (var i = 0; i < this.gestureEvents.length; i++) {
        document.addEventListener(this.gestureEvents[i], onGesture, true);
      }
    },

    start: function (name, loop, volume) {
      this.cancelFade();
      this.stopAllExcept(name);

      var a = this.element(name);
      this.current = name;
      this.trackVolume = this.clamp01(volume);
      a.loop = !!loop;
      a.volume = this.clamp01(this.trackVolume * this.masterVolume);
      try {
        a.currentTime = 0;
      } catch (e) { /* not seekable yet; it starts at 0 anyway */ }

      var self = this;
      var p;
      try {
        p = a.play();
      } catch (e) {
        self.pendingPlay = { name: name, loop: !!loop, volume: self.trackVolume };
        self.installGestureListener();
        return;
      }
      if (p && typeof p.then === 'function') {
        p.then(function () {
          if (self.pendingPlay && self.pendingPlay.name === name) self.pendingPlay = null;
        }, function (err) {
          // NotAllowedError == autoplay blocked; retry after the next gesture.
          // Anything else (NotSupportedError, AbortError) is not retryable.
          var blocked = !err || err.name === 'NotAllowedError';
          if (blocked) {
            self.pendingPlay = { name: name, loop: !!loop, volume: self.trackVolume };
            self.installGestureListener();
          } else if (typeof console !== 'undefined' && console.warn) {
            console.warn('[Html5Music] play failed for ' + name + ': ' + err.name);
          }
        });
      }
    },

    applyVolume: function () {
      if (!this.current) return;
      var a = this.tracks[this.current];
      if (a) a.volume = this.clamp01(this.trackVolume * this.masterVolume);
    }
  },

  Music_SetBasePath__deps: ['$Html5MusicState'],
  Music_SetBasePath: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (url && url.charAt(url.length - 1) !== '/') url += '/';
    Html5MusicState.basePath = url;
  },

  Music_Play__deps: ['$Html5MusicState'],
  Music_Play: function (namePtr, loop, volume) {
    var name = UTF8ToString(namePtr);
    if (!name) return;
    Html5MusicState.start(name, loop, volume);
  },

  Music_Stop__deps: ['$Html5MusicState'],
  Music_Stop: function (fadeMs) {
    var state = Html5MusicState;
    state.pendingPlay = null;
    state.cancelFade();

    var name = state.current;
    state.current = null;
    if (!name) return;
    var a = state.tracks[name];
    if (!a) return;

    if (!(fadeMs > 0)) {
      state.halt(name);
      return;
    }

    var step = 50;
    var from = a.volume;
    var elapsed = 0;
    state.fadeTimer = setInterval(function () {
      elapsed += step;
      var k = 1 - (elapsed / fadeMs);
      if (k <= 0) {
        state.cancelFade();
        state.halt(name);
        a.volume = from;   // restore for the next Play
        return;
      }
      a.volume = state.clamp01(from * k);
    }, step);
  },

  Music_SetVolume__deps: ['$Html5MusicState'],
  Music_SetVolume: function (v) {
    Html5MusicState.masterVolume = Html5MusicState.clamp01(v);
    Html5MusicState.applyVolume();
  },

  Music_IsPlaying__deps: ['$Html5MusicState'],
  Music_IsPlaying: function () {
    var state = Html5MusicState;
    if (state.pendingPlay) return 1;   // queued behind the autoplay gate
    if (!state.current) return 0;
    var a = state.tracks[state.current];
    if (!a) return 0;
    return (!a.paused && !a.ended) ? 1 : 0;
  },

  Music_GetTime__deps: ['$Html5MusicState'],
  Music_GetTime: function () {
    var state = Html5MusicState;
    if (!state.current) return 0;
    var a = state.tracks[state.current];
    if (!a || !(a.currentTime >= 0)) return 0;
    return a.currentTime;
  },

  Music_SetTime__deps: ['$Html5MusicState'],
  Music_SetTime: function (seconds) {
    var state = Html5MusicState;
    if (!state.current) return;
    var a = state.tracks[state.current];
    if (!a) return;
    try {
      a.currentTime = (seconds >= 0) ? seconds : 0;
    } catch (e) { /* not seekable yet */ }
  }
};

autoAddDeps(Html5MusicLibrary, '$Html5MusicState');
mergeInto(LibraryManager.library, Html5MusicLibrary);
