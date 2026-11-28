mergeInto(LibraryManager.library, {
  RequestGameFullscreen: function () {
    if (typeof window.enterGameFullscreen === "function") {
      window.enterGameFullscreen();
    }
  }
});
