(function () {
  var canvas = document.getElementById("unity-canvas");
  var button = document.getElementById("unity-fullscreen-button");

  function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement);
  }

  function updateButton() {
    button.hidden = isFullscreen() || !(canvas.requestFullscreen || canvas.webkitRequestFullscreen);
  }

  // Called by the in-game Play button through the WebGL plugin. Browser
  // activation from that click allows the request without a second overlay.
  window.enterGameFullscreen = function () {
    if (isFullscreen()) return;
    var request = canvas.requestFullscreen || canvas.webkitRequestFullscreen;
    if (!request) return;
    try {
      Promise.resolve(request.call(canvas)).catch(function () {
        // Keep the explicit button available if the browser declines the request.
        updateButton();
      });
    } catch (error) {
      updateButton();
    }
    canvas.focus();
  };

  button.addEventListener("click", window.enterGameFullscreen);
  document.addEventListener("fullscreenchange", updateButton);
  document.addEventListener("webkitfullscreenchange", updateButton);
  updateButton();
})();
