mergeInto(LibraryManager.library, {
  $AnimeQuestWatchBridge: {
    container: null,
    frame: null,
    video: null,
    provider: "",
    pendingSeconds: 0,
    pendingPlaying: false,
    ready: false,

    load: function (url, provider, sourceId) {
      this.ensureContainer();
      this.provider = provider || "external";
      this.pendingSeconds = 0;
      this.pendingPlaying = false;
      this.ready = this.provider === "direct";
      this.clearPlayer();

      if (this.provider === "youtube") {
        this.frame = document.createElement("iframe");
        this.frame.id = "animequest-watch-youtube";
        this.frame.src = "https://www.youtube.com/embed/" + encodeURIComponent(sourceId) +
          "?enablejsapi=1&playsinline=1&origin=" + encodeURIComponent(window.location.origin);
        this.frame.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
        this.frame.allowFullscreen = true;
        this.decoratePlayer(this.frame);
        this.container.appendChild(this.frame);
        window.setTimeout(this.flushPending.bind(this), 700);
        return;
      }

      if (this.provider === "vimeo") {
        this.frame = document.createElement("iframe");
        this.frame.id = "animequest-watch-vimeo";
        this.frame.src = "https://player.vimeo.com/video/" + encodeURIComponent(sourceId) + "?api=1&playsinline=1";
        this.frame.allow = "autoplay; fullscreen; picture-in-picture";
        this.frame.allowFullscreen = true;
        this.decoratePlayer(this.frame);
        this.container.appendChild(this.frame);
        window.setTimeout(this.flushPending.bind(this), 700);
        return;
      }

      if (this.provider === "direct") {
        this.video = document.createElement("video");
        this.video.src = url;
        this.video.controls = true;
        this.video.playsInline = true;
        this.video.preload = "metadata";
        this.video.style.background = "#000";
        this.decoratePlayer(this.video);
        this.container.appendChild(this.video);
        this.ready = true;
      }
    },

    ensureContainer: function () {
      if (this.container) {
        this.container.style.display = "block";
        return;
      }

      var root = document.createElement("div");
      root.id = "animequest-watch-overlay";
      root.style.position = "fixed";
      root.style.right = "24px";
      root.style.bottom = "24px";
      root.style.width = "min(720px, calc(100vw - 48px))";
      root.style.aspectRatio = "16 / 9";
      root.style.background = "#080604";
      root.style.border = "2px solid rgba(255,255,255,0.75)";
      root.style.boxShadow = "0 18px 48px rgba(0,0,0,0.45)";
      root.style.zIndex = "2147483000";
      root.style.overflow = "hidden";

      var close = document.createElement("button");
      close.type = "button";
      close.textContent = "x";
      close.setAttribute("aria-label", "Close watch player");
      close.style.position = "absolute";
      close.style.top = "8px";
      close.style.right = "8px";
      close.style.width = "32px";
      close.style.height = "32px";
      close.style.border = "0";
      close.style.background = "rgba(0,0,0,0.65)";
      close.style.color = "#fff";
      close.style.font = "700 18px sans-serif";
      close.style.cursor = "pointer";
      close.style.zIndex = "2";
      close.onclick = this.close.bind(this);
      root.appendChild(close);

      document.body.appendChild(root);
      this.container = root;
    },

    decoratePlayer: function (element) {
      element.style.position = "absolute";
      element.style.left = "0";
      element.style.top = "0";
      element.style.width = "100%";
      element.style.height = "100%";
      element.style.border = "0";
    },

    clearPlayer: function () {
      if (!this.container) return;

      if (this.frame) {
        this.frame.remove();
        this.frame = null;
      }

      if (this.video) {
        this.video.pause();
        this.video.remove();
        this.video = null;
      }
    },

    close: function () {
      this.clearPlayer();
      if (this.container) {
        this.container.remove();
        this.container = null;
      }
      this.provider = "";
      this.ready = false;
    },

    play: function () {
      this.pendingPlaying = true;
      if (this.video) {
        var promise = this.video.play();
        if (promise && promise.catch) promise.catch(function () {});
        return;
      }

      this.postProviderCommand("play");
    },

    pause: function () {
      this.pendingPlaying = false;
      if (this.video) {
        this.video.pause();
        return;
      }

      this.postProviderCommand("pause");
    },

    seek: function (seconds) {
      this.pendingSeconds = Math.max(0, Number(seconds) || 0);
      if (this.video) {
        this.video.currentTime = this.pendingSeconds;
        return;
      }

      this.postProviderCommand("seek", this.pendingSeconds);
    },

    flushPending: function () {
      this.ready = true;
      this.seek(this.pendingSeconds);
      if (this.pendingPlaying) this.play();
    },

    postProviderCommand: function (command, seconds) {
      if (!this.frame || !this.frame.contentWindow) return;

      if (this.provider === "youtube") {
        var func = command === "play" ? "playVideo" : command === "pause" ? "pauseVideo" : "seekTo";
        var args = command === "seek" ? [Math.max(0, Number(seconds) || 0), true] : [];
        this.frame.contentWindow.postMessage(JSON.stringify({ event: "command", func: func, args: args }), "*");
        return;
      }

      if (this.provider === "vimeo") {
        var method = command === "play" ? "play" : command === "pause" ? "pause" : "setCurrentTime";
        var value = command === "seek" ? Math.max(0, Number(seconds) || 0) : undefined;
        this.frame.contentWindow.postMessage(JSON.stringify({ method: method, value: value }), "*");
      }
    }
  },

  AnimeQuestWatch_IsSupported__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_IsSupported: function () {
    return typeof document !== "undefined" ? 1 : 0;
  },

  AnimeQuestWatch_Load__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_Load: function (urlPtr, providerPtr, sourceIdPtr) {
    var url = UTF8ToString(urlPtr || 0);
    var provider = UTF8ToString(providerPtr || 0);
    var sourceId = UTF8ToString(sourceIdPtr || 0);
    AnimeQuestWatchBridge.load(url, provider, sourceId);
  },

  AnimeQuestWatch_Play__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_Play: function () {
    AnimeQuestWatchBridge.play();
  },

  AnimeQuestWatch_Pause__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_Pause: function () {
    AnimeQuestWatchBridge.pause();
  },

  AnimeQuestWatch_Seek__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_Seek: function (seconds) {
    AnimeQuestWatchBridge.seek(seconds || 0);
  },

  AnimeQuestWatch_Close__deps: ["$AnimeQuestWatchBridge"],
  AnimeQuestWatch_Close: function () {
    AnimeQuestWatchBridge.close();
  }
});
