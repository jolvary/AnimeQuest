mergeInto(LibraryManager.library, {
  AnimeQuest_ShowYouTubeTrailer: function (youtubeIdPtr) {
    var youtubeId = UTF8ToString(youtubeIdPtr || 0);
    if (!/^[A-Za-z0-9_-]{6,32}$/.test(youtubeId)) {
      return;
    }

    var existing = document.getElementById("animequest-youtube-trailer-overlay");
    if (existing && existing.parentNode) {
      existing.parentNode.removeChild(existing);
    }

    var overlay = document.createElement("div");
    overlay.id = "animequest-youtube-trailer-overlay";
    overlay.style.position = "fixed";
    overlay.style.left = "0";
    overlay.style.top = "0";
    overlay.style.right = "0";
    overlay.style.bottom = "0";
    overlay.style.zIndex = "2147483647";
    overlay.style.background = "rgba(0, 0, 0, 0.74)";
    overlay.style.display = "flex";
    overlay.style.alignItems = "center";
    overlay.style.justifyContent = "center";
    overlay.style.padding = "24px";
    overlay.style.boxSizing = "border-box";

    var frameWrap = document.createElement("div");
    frameWrap.style.position = "relative";
    frameWrap.style.width = "min(960px, 92vw)";
    frameWrap.style.aspectRatio = "16 / 9";
    frameWrap.style.background = "#000";
    frameWrap.style.boxShadow = "0 18px 50px rgba(0, 0, 0, 0.45)";

    var close = document.createElement("button");
    close.type = "button";
    close.textContent = "x";
    close.setAttribute("aria-label", "Close trailer");
    close.style.position = "absolute";
    close.style.right = "-14px";
    close.style.top = "-14px";
    close.style.width = "38px";
    close.style.height = "38px";
    close.style.border = "0";
    close.style.borderRadius = "19px";
    close.style.background = "#7a471f";
    close.style.color = "#fff";
    close.style.font = "bold 22px Arial, sans-serif";
    close.style.cursor = "pointer";
    close.style.zIndex = "1";

    var iframe = document.createElement("iframe");
    iframe.src = "https://www.youtube-nocookie.com/embed/" + encodeURIComponent(youtubeId) + "?autoplay=1&rel=0";
    iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
    iframe.allowFullscreen = true;
    iframe.style.position = "absolute";
    iframe.style.left = "0";
    iframe.style.top = "0";
    iframe.style.width = "100%";
    iframe.style.height = "100%";
    iframe.style.border = "0";

    var removeOverlay = function () {
      if (overlay.parentNode) {
        overlay.parentNode.removeChild(overlay);
      }
      document.removeEventListener("keydown", onKeyDown, true);
    };

    var onKeyDown = function (event) {
      if (event.key === "Escape") {
        removeOverlay();
      }
    };

    close.addEventListener("click", removeOverlay);
    overlay.addEventListener("click", function (event) {
      if (event.target === overlay) {
        removeOverlay();
      }
    });
    document.addEventListener("keydown", onKeyDown, true);

    frameWrap.appendChild(iframe);
    frameWrap.appendChild(close);
    overlay.appendChild(frameWrap);
    document.body.appendChild(overlay);
  }
});
