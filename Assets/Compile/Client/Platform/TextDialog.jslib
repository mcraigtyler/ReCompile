mergeInto(LibraryManager.library, {
  // A page-level dialog for pasting text or choosing a text file, since the game can't read the clipboard
  // itself. CompileTextDialogState says when it's done: 0 open, 1 confirmed, 2 cancelled.
  CompileOpenTextDialog: function (titlePtr, hintPtr) {
    var title = UTF8ToString(titlePtr);
    var hint = UTF8ToString(hintPtr);
    window.compileTextDialog = { state: 0, text: "" };
    var overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;z-index:1000;background:rgba(10,6,20,0.78);display:flex;align-items:center;justify-content:center;font-family:sans-serif";
    var box = document.createElement("div");
    box.style.cssText = "background:#1d1630;color:#eee;border:2px solid #8a5cff;padding:20px;width:min(720px,92vw);box-sizing:border-box;display:flex;flex-direction:column;gap:12px";
    var heading = document.createElement("div");
    heading.textContent = title;
    heading.style.cssText = "font-size:22px;font-weight:bold";
    var note = document.createElement("div");
    note.textContent = hint;
    note.style.cssText = "font-size:14px;color:#bbb";
    var area = document.createElement("textarea");
    area.style.cssText = "width:100%;height:40vh;box-sizing:border-box;background:#0f0b1a;color:#eee;border:1px solid #555;font-family:monospace;font-size:13px";
    var file = document.createElement("input");
    file.type = "file";
    file.accept = ".txt,text/plain";
    file.onchange = function () {
      if (!file.files || !file.files[0]) return;
      var reader = new FileReader();
      reader.onload = function () { area.value = reader.result; };
      reader.readAsText(file.files[0]);
    };
    var row = document.createElement("div");
    row.style.cssText = "display:flex;gap:12px;justify-content:flex-end";
    function button(label, primary, onClick) {
      var b = document.createElement("button");
      b.textContent = label;
      b.style.cssText = "font-size:16px;padding:8px 18px;cursor:pointer;border:1px solid #8a5cff;color:#fff;background:" + (primary ? "#6b3df0" : "transparent");
      b.onclick = onClick;
      row.appendChild(b);
    }
    function close(state) {
      window.compileTextDialog.state = state;
      window.compileTextDialog.text = area.value;
      document.body.removeChild(overlay);
    }
    button("Cancel", false, function () { close(2); });
    button("Load", true, function () { close(1); });
    box.appendChild(heading);
    box.appendChild(note);
    box.appendChild(area);
    box.appendChild(file);
    box.appendChild(row);
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    area.focus();
  },

  CompileTextDialogState: function () {
    return window.compileTextDialog ? window.compileTextDialog.state : 2;
  },

  CompileTextDialogText: function () {
    var text = window.compileTextDialog ? window.compileTextDialog.text : "";
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  }
});
