(function () {
  var canvas = document.getElementById("unity-canvas");
  var button = document.getElementById("unity-fullscreen-button");
  var player = null;

  function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement);
  }

  function updateButton() {
    button.hidden = isFullscreen() || player === null
      || !(canvas.requestFullscreen || canvas.webkitRequestFullscreen);
  }

  window.setGameFullscreenPlayer = function (unityInstance) {
    player = unityInstance;
    updateButton();
  };

  // Called by the in-game Play button through the WebGL plugin. Browser
  // activation from that click allows the request without a second overlay.
  window.enterGameFullscreen = function () {
    if (isFullscreen() || player === null) return;
    var request = canvas.requestFullscreen || canvas.webkitRequestFullscreen;
    if (!request) return;
    try {
      // Canvas CSS stays bounded to the viewport; Unity follows its DOM size.
      // Catch permission rejection so declining fullscreen cannot stop the game.
      Promise.resolve(request.call(canvas)).catch(updateButton);
    } catch (error) {
      updateButton();
    }
    canvas.focus();
  };

  button.addEventListener("click", window.enterGameFullscreen);
  document.addEventListener("fullscreenchange", updateButton);
  document.addEventListener("webkitfullscreenchange", updateButton);
  document.addEventListener("fullscreenerror", updateButton);
  updateButton();
})();
