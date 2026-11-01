using System;

namespace SmartMemReduct
{
    /// <summary>
    /// Self-contained companion page served by the desktop app over the local
    /// network. No CDN, no external fonts, no analytics — tiny and offline.
    /// </summary>
    internal static class MobileApp
    {
        public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
<meta name="theme-color" content="#070b10">
<title>Smart Skii-mer Remote</title>
<style>
:root{--bg:#070b10;--panel:#0e1620;--ink:#e7f1eb;--muted:#7a939c;--mint:#6ee7b7;--amber:#f5c26b;--rose:#fb7185;--line:#1f2d3a}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:radial-gradient(600px 400px at 80% -10%,rgba(110,231,183,.10),transparent 60%),var(--bg);color:var(--ink);font:15px/1.45 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;min-height:100vh;display:flex;flex-direction:column}
header{display:flex;align-items:center;gap:10px;padding:16px 18px;border-bottom:1px solid var(--line)}
.mark{width:34px;height:34px;border-radius:11px;background:#0a1016;display:grid;place-items:center;color:var(--mint);font-size:18px;border:1px solid var(--line)}
h1{font-size:16px;margin:0;font-weight:600;letter-spacing:-.01em}
.sub{color:var(--muted);font-size:11px;letter-spacing:.14em;text-transform:uppercase}
main{flex:1;padding:16px 18px 28px;display:flex;flex-direction:column;gap:14px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:16px;padding:16px}
.gauges{display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;text-align:center}
.g b{display:block;font-size:24px;font-weight:600;letter-spacing:-.02em}
.g span{font-size:10px;letter-spacing:.12em;text-transform:uppercase;color:var(--muted)}
.row{display:flex;justify-content:space-between;font-size:13px;color:var(--muted);margin-top:8px}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}
button{border:1px solid var(--line);background:#0b141c;color:var(--ink);border-radius:14px;padding:16px 12px;font-size:14px;font-weight:600;font-family:inherit;min-height:58px}
button:active{transform:scale(.97);background:#12202b}
button.primary{background:#122a1f;border-color:#26503a;color:var(--mint)}
button.warn{background:#2a2318;border-color:#4d3f22;color:var(--amber)}
button.on{outline:2px solid var(--mint);outline-offset:-2px}
.chips{display:flex;gap:8px;flex-wrap:wrap}
.chip{border:1px solid var(--line);border-radius:999px;padding:9px 14px;font-size:12px;color:var(--muted);background:#0b141c}
.chip.on{background:#122a1f;border-color:#26503a;color:var(--mint)}
input{width:100%;background:#0b141c;border:1px solid var(--line);border-radius:12px;color:var(--ink);padding:16px;font-size:20px;text-align:center;letter-spacing:.3em;font-family:inherit}
.hide{display:none}
.note{color:var(--muted);font-size:12px;text-align:center}
#toast{position:fixed;left:50%;bottom:20px;transform:translateX(-50%) translateY(80px);background:#122a1f;color:var(--mint);border:1px solid #26503a;padding:10px 16px;border-radius:12px;font-size:13px;transition:.25s;opacity:0}
#toast.show{transform:translateX(-50%);opacity:1}
</style>
</head>
<body>
<header>
  <div class="mark">◆</div>
  <div><h1>Smart Skii-mer</h1><div class="sub">Remote control</div></div>
</header>
<main>
  <section id="pairView" class="card">
    <p class="note">Enter the 4-digit code shown on your PC.</p>
    <input id="code" inputmode="numeric" maxlength="4" placeholder="0000" autocomplete="one-time-code">
    <div style="height:12px"></div>
    <button class="primary" style="width:100%" id="pairBtn">Connect</button>
    <p class="note" id="pairErr"></p>
  </section>

  <section id="ctrlView" class="hide" style="display:flex;flex-direction:column;gap:14px">
    <div class="card">
      <div class="gauges">
        <div class="g"><b id="ram">–</b><span>RAM</span></div>
        <div class="g"><b id="cpu">–</b><span>CPU</span></div>
        <div class="g"><b id="mode">–</b><span>Mode</span></div>
      </div>
      <div class="row"><span id="app">–</span><span id="at">–</span></div>
    </div>

    <div class="card">
      <div class="sub" style="margin-bottom:10px">Quick actions</div>
      <div class="grid">
        <button class="primary" data-cmd="quick_clean">Quick clean</button>
        <button class="primary" data-cmd="free_background">Free memory</button>
        <button class="warn" data-cmd="deep_clean">Deep clean</button>
        <button id="refresh">Refresh</button>
      </div>
    </div>

    <div class="card">
      <div class="sub" style="margin-bottom:10px">Mode</div>
      <div class="chips" id="modes">
        <button class="chip" data-mode="mode_auto">Auto</button>
        <button class="chip" data-mode="mode_game">Gaming</button>
        <button class="chip" data-mode="mode_stream">Streaming</button>
        <button class="chip" data-mode="mode_work">Work</button>
        <button class="chip" data-mode="mode_eco">Eco</button>
        <button class="chip" data-mode="mode_balance">Balanced</button>
      </div>
    </div>
    <p class="note">Protected: the app in use is never throttled.</p>
  </section>
</main>
<div id="toast"></div>
<script>
(function(){
  var token = localStorage.getItem('sk_token') || '';
  var $ = function(id){ return document.getElementById(id); };
  function toast(msg){ var t=$('toast'); t.textContent=msg; t.classList.add('show'); setTimeout(function(){t.classList.remove('show');},1800); }
  function show(view){ $('pairView').classList.toggle('hide', view!=='pair'); $('ctrlView').classList.toggle('hide', view!=='ctrl'); }
  function api(path, opts){
    opts = opts || {};
    opts.headers = Object.assign({'Content-Type':'application/json'}, token?{'Authorization':'Bearer '+token}:{}, opts.headers||{});
    return fetch(path, opts).then(function(r){
      if (r.status === 401) { token=''; localStorage.removeItem('sk_token'); show('pair'); throw new Error('auth'); }
      return r.json();
    });
  }
  function render(s){
    $('ram').textContent = s.ram + '%';
    $('cpu').textContent = s.cpu + '%';
    $('mode').textContent = s.mode;
    $('app').textContent = s.app;
    $('at').textContent = s.time;
    Array.prototype.forEach.call(document.querySelectorAll('#modes .chip'), function(b){
      b.classList.toggle('on', b.getAttribute('data-mode') === s.modeCmd);
    });
  }
  function poll(){
    if (!token) return;
    api('/status').then(render).catch(function(){});
  }
  $('pairBtn').onclick = function(){
    var code = ($('code').value || '').trim();
    if (code.length !== 4) { $('pairErr').textContent = 'Enter 4 digits'; return; }
    $('pairErr').textContent = 'Connecting…';
    api('/pair', {method:'POST', body: JSON.stringify({code:code})}).then(function(d){
      token = d.token; localStorage.setItem('sk_token', token);
      $('pairErr').textContent = ''; show('ctrl'); poll();
    }).catch(function(){ $('pairErr').textContent = 'Wrong code or too many attempts.'; });
  };
  document.addEventListener('click', function(e){
    var b = e.target.closest('button');
    if (!b) return;
    if (b.id === 'refresh') { poll(); return; }
    var cmd = b.getAttribute('data-cmd') || b.getAttribute('data-mode');
    if (!cmd) return;
    api('/command', {method:'POST', body: JSON.stringify({cmd:cmd})}).then(function(){
      toast(b.textContent + ' sent'); setTimeout(poll, 700);
    }).catch(function(){});
  });
  show(token ? 'ctrl' : 'pair');
  if (token) poll();
  setInterval(poll, 3000);
})();
</script>
</body>
</html>
""";
    }
}
