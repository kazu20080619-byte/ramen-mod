// ラーメンMODランチャーの画面。C# とは chrome.webview でやりとりする
(function () {
  'use strict';

  // ---------------------------------------------------------------------------
  // C# とのやりとり
  // ---------------------------------------------------------------------------
  var seq = 0;
  var pending = {};
  var hasBridge = !!(window.chrome && window.chrome.webview);

  function call(cmd, args) {
    return new Promise(function (resolve, reject) {
      if (!hasBridge) { reject(new Error('ランチャーの外では動きません')); return; }
      var id = ++seq;
      pending[id] = { resolve: resolve, reject: reject };
      window.chrome.webview.postMessage({ id: id, cmd: cmd, args: args || {} });
    });
  }

  // ドロップされたファイルもいっしょに送る（C# にはファイルの場所だけがわたる）
  function callFiles(cmd, args, files) {
    return new Promise(function (resolve, reject) {
      if (!hasBridge || !window.chrome.webview.postMessageWithAdditionalObjects) { reject(new Error('このランチャーではドロップが使えません')); return; }
      var id = ++seq;
      pending[id] = { resolve: resolve, reject: reject };
      window.chrome.webview.postMessageWithAdditionalObjects({ id: id, cmd: cmd, args: args || {} }, files);
    });
  }

  if (hasBridge) {
    window.chrome.webview.addEventListener('message', function (e) {
      var m = e.data;
      if (!m) return;
      if (m.id && pending[m.id]) {
        var p = pending[m.id];
        delete pending[m.id];
        if (m.ok) p.resolve(m.result); else p.reject(new Error(m.error || 'うまくいきませんでした'));
        return;
      }
      if (m.evt === 'log') addLog(m.text);
      else if (m.evt === 'state') refresh();
      else if (m.evt === 'update') showUpdate(m.info);
    });
  }

  // ---------------------------------------------------------------------------
  // 小さな道具
  // ---------------------------------------------------------------------------
  function $(id) { return document.getElementById(id); }
  function el(tag, attrs, kids) {
    var n = document.createElement(tag);
    if (attrs) Object.keys(attrs).forEach(function (k) {
      if (k === 'text') n.textContent = attrs[k];
      else if (k === 'class') n.className = attrs[k];
      else if (k.slice(0, 2) === 'on') n.addEventListener(k.slice(2), attrs[k]);
      else if (attrs[k] !== null && attrs[k] !== undefined && attrs[k] !== false) n.setAttribute(k, attrs[k] === true ? '' : attrs[k]);
    });
    (kids || []).forEach(function (c) { if (c) n.appendChild(typeof c === 'string' ? document.createTextNode(c) : c); });
    return n;
  }

  var toastTimer;
  function toast(text, isError) {
    var t = $('toast');
    t.textContent = text;
    t.className = 'toast' + (isError ? ' error' : '');
    t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.hidden = true; }, isError ? 7000 : 3500);
  }

  function addLog(text) {
    var log = $('log');
    log.textContent += text + '\n';
    log.scrollTop = log.scrollHeight;
  }

  // ボタンを押している間は、もう一度押せないようにする
  function run(btn, promise, okText) {
    if (btn) btn.disabled = true;
    return promise.then(function (r) {
      if (okText) toast(okText);
      return r;
    }, function (err) {
      toast(err.message, true);
      throw err;
    }).finally(function () {
      if (btn) btn.disabled = false;
      updateButtons();
    });
  }

  // ---------------------------------------------------------------------------
  // 画面の切りかえ
  // ---------------------------------------------------------------------------
  var screens = ['home', 'mods', 'settings', 'records', 'diag'];
  var current = 'home';

  function show(name) {
    if (screens.indexOf(name) < 0) name = 'home';
    current = name;
    screens.forEach(function (s) { $('screen-' + s).hidden = s !== name; });
    document.querySelectorAll('.nav-list a').forEach(function (a) {
      if (a.getAttribute('data-screen') === name) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
    document.querySelector('main').scrollTop = 0;
    if (name === 'settings') loadConfig(cfgMod);
    if (name === 'records') loadRecords();
    if (name === 'diag') loadDiag();
    if (location.hash !== '#' + name) history.replaceState(null, '', '#' + name);
  }

  document.addEventListener('click', function (e) {
    var go = e.target.closest('[data-go], .nav-list a');
    if (!go) return;
    e.preventDefault();
    show(go.getAttribute('data-go') || go.getAttribute('data-screen'));
  });

  // ---------------------------------------------------------------------------
  // いまの状態
  // ---------------------------------------------------------------------------
  var state = null;

  // MOD の見た目（漢字 1 字・だれに効くか・キー）は、各 MOD の ramen-launcher.json から state で来る
  var MP = { 'Host Only': 'ホストだけ', 'Client Only': '自分だけ', 'All Players': 'みんな', 'Singleplayer Only': 'ひとり用' };

  function refresh() {
    return call('state').then(function (s) { state = s; render(); return s; }, function (err) { toast(err.message, true); });
  }

  function render() {
    var s = state;
    if (!s) return;
    $('brand-ver').textContent = 'ランチャー v' + s.launcherVersion;
    $('game-path').textContent = s.gameFound ? shortPath(s.gameDir) : '見つかりません';
    var f = $('game-found');
    f.textContent = s.gameFound ? (s.running ? '起動中' : '見つかりました') : '「診断」で選んでね';
    f.className = 'game-found' + (s.gameFound ? '' : ' no');

    // ホーム
    var on = s.mods.filter(function (m) { return m.on; }).length;
    $('stat-mods').textContent = on + ' / ' + s.mods.length + ' つ ON';
    var mine = s.mods.filter(function (m) { return !m.builtin; }).length;
    $('hero-sub').textContent = s.mods.length + ' つの MOD があります。' +
      s.mods.filter(function (m) { return m.builtin; }).map(function (m) { return m.name.replace(/（.*）/, ''); }).join('・') +
      (mine ? '。足した MOD が ' + mine + ' つ。' : '。');
    $('running-warn').hidden = !s.running;
    var r = s.record;
    if (r) {
      $('stat-gacha').textContent = r.pulls + ' 回・天井まで ' + Math.max(0, r.ceiling - r.pity);
      $('stat-fortune').textContent = r.fortune || 'まだ';
      $('stat-effect').textContent = r.effect || (r.fortune ? '' : 'おみくじを引いてね');
    } else {
      $('stat-gacha').textContent = 'まだ回していません';
      $('stat-fortune').textContent = 'まだ';
      $('stat-effect').textContent = 'おみくじを引いてね';
    }

    // 診断
    $('diag-path').textContent = s.gameFound ? s.gameDir : 'ゲームが見つかりません';
    $('diag-state').textContent = !s.gameFound ? '' :
      'UE4SS: ' + (s.ue4ss ? '入っています' : 'まだ入っていません') + '　／　' + (s.vanilla ? 'いまはバニラ（MOD なし）' : 'MOD あり') + (s.running ? '　／　ゲーム起動中' : '');
    $('diag-vanilla').checked = !!s.vanilla;
    $('diag-versions').textContent = 'ランチャー v' + s.launcherVersion + '　／　MOD v' + s.modsVersion;

    if (!modsTouched) sel = {};
    s.mods.forEach(function (m) { if (!modsTouched) sel[m.folder] = m.on; });
    renderMods();
    updateButtons();
  }

  function shortPath(p) {
    var i = p.indexOf('steamapps\\common\\');
    if (i >= 0) return 'Steam / ' + p.slice(i + 17).split('\\')[0];
    return p;
  }

  function updateButtons() {
    var s = state;
    var ok = !!(s && s.gameFound && !s.running && !s.busy);
    ['play-mods', 'play-vanilla', 'mods-apply', 'mods-none', 'diag-uninstall', 'diag-reinstall', 'rec-save'].forEach(function (id) {
      if ($(id)) $(id).disabled = !ok;
    });
    $('diag-vanilla').disabled = !ok;
  }

  // ---------------------------------------------------------------------------
  // ホーム
  // ---------------------------------------------------------------------------
  $('play-mods').addEventListener('click', function () {
    run(this, call('launch', { withMods: true }), 'MOD ありで起動します');
  });
  $('play-vanilla').addEventListener('click', function () {
    run(this, call('launch', { withMods: false }), 'MOD なしで起動します');
  });

  function showUpdate(info) {
    if (!info || !info.available) return;
    $('update-title').textContent = '新しい版 ' + info.tag + ' があります';
    $('update-banner').hidden = false;
  }
  $('update-later').addEventListener('click', function () { $('update-banner').hidden = true; });
  $('update-go').addEventListener('click', function () {
    var b = this;
    toast('ダウンロードしています…');
    run(b, call('runUpdate')).then(function (r) {
      $('update-banner').hidden = true;
      toast(r.message + (r.restart ? '\nランチャーを起動しなおします。' : ''));
      refresh();
    });
  });

  // ---------------------------------------------------------------------------
  // MOD
  // ---------------------------------------------------------------------------
  var sel = {};
  var modsTouched = false;
  var lastChecks = [];

  function selected() { return Object.keys(sel).filter(function (k) { return sel[k]; }); }

  function renderMods() {
    if (!state) return;
    var box = $('mod-cards');
    box.innerHTML = '';
    state.mods.forEach(function (m) {
      var cb = el('input', { type: 'checkbox', 'aria-label': m.name + ' を使う' });
      cb.checked = !!sel[m.folder];
      cb.addEventListener('change', function () { sel[m.folder] = cb.checked; modsTouched = true; card.classList.toggle('off', !cb.checked); markPreset(); runCheck(); });
      var who = m.who || MP[m.multiplayer] || '';
      var meta = 'v' + (m.version || '?') + '　キー: ' + (m.keys.length ? m.keys.join('・') : 'なし');
      if (m.deps.length) meta += '　要るもの: ' + m.deps.join('・');
      var actions = [el('label', { class: 'use' }, [cb, el('span', { text: '使う' })])];
      if (!m.builtin) actions.push(el('button', { type: 'button', class: 'link-btn', text: '外す', onclick: function () { removeMod(m, this); } }));
      var card = el('div', { class: 'mod' + (sel[m.folder] ? '' : ' off') }, [
        el('div', { class: 'mod-mark', 'aria-hidden': 'true', text: m.mark || '・' }),
        el('div', { class: 'mod-body' }, [
          el('div', { class: 'mod-title' }, [
            el('strong', { text: m.name }),
            who ? el('span', { class: 'pill mp', text: who }) : null,
            m.builtin ? null : el('span', { class: 'pill mine', text: '足した MOD' })]),
          m.desc ? el('span', { class: 'mod-desc', text: m.desc }) : null,
          el('span', { class: 'mod-meta', text: meta }),
          m.problems.length ? el('span', { class: 'mod-warn', text: m.problems.join(' / ') }) : null
        ]),
        el('div', { class: 'mod-actions' }, actions)
      ]);
      box.appendChild(card);
    });
    renderPresets();
    runCheck();
  }

  // まとめ選び: 組みこみ（おもしろ・バランス崩壊・全部入り）＋ 自分で保存した選び方
  function presetList() {
    var list = [
      { name: 'おもしろ', mods: state.presets.fun },
      { name: 'バランス崩壊', mods: state.presets.benri },
      { name: '全部入り', mods: state.presets.all }
    ];
    state.profiles.forEach(function (p) { list.push({ name: p.name, mods: p.mods, mine: true }); });
    return list;
  }

  function same(mods) {
    var set = {};
    mods.forEach(function (f) { set[f] = true; });
    return state.mods.every(function (m) { return !!sel[m.folder] === !!set[m.folder]; });
  }

  function renderPresets() {
    var box = $('mod-presets');
    box.innerHTML = '';
    presetList().forEach(function (p) {
      var b = el('button', { type: 'button', class: 'chip', 'data-name': p.name, title: p.mods.length + ' こ',
        onclick: function () {
          state.mods.forEach(function (m) { sel[m.folder] = p.mods.indexOf(m.folder) >= 0; });
          modsTouched = true;
          renderMods();
        } }, [p.name]);
      if (p.mine) {
        b.appendChild(el('span', { class: 'chip-x', role: 'button', 'aria-label': p.name + ' を消す', text: '×',
          onclick: function (e) {
            e.stopPropagation();
            if (!confirm('「' + p.name + '」を消しますか？')) return;
            run(null, call('deleteProfile', { name: p.name })).then(function (s) { state = s; renderPresets(); });
          } }));
      }
      box.appendChild(b);
    });
    box.appendChild(el('button', { type: 'button', class: 'chip save', text: '＋ いまの選び方を保存',
      onclick: function () {
        var name = prompt('この選び方の名前（例: ふだん用・配信用）', '');
        if (!name) return;
        run(null, call('saveProfile', { name: name, selected: selected() }), '「' + name + '」を保存しました').then(function (s) { state = s; renderPresets(); });
      } }));
    markPreset();
  }

  function markPreset() {
    if (!state) return;
    var list = presetList();
    document.querySelectorAll('#mod-presets [data-name]').forEach(function (b) {
      var p = list.filter(function (x) { return x.name === b.getAttribute('data-name'); })[0];
      b.setAttribute('aria-pressed', p && same(p.mods) ? 'true' : 'false');
    });
  }

  // 依存・版・ぶつかりのチェック（選び方を変えるたび）
  var checkTimer;
  function runCheck() {
    clearTimeout(checkTimer);
    checkTimer = setTimeout(function () {
      call('check', { selected: selected() }).then(function (items) {
        lastChecks = items;
        var box = $('mod-checks');
        box.innerHTML = '';
        var icon = { error: '✕', warn: '！', info: '・' };
        items.forEach(function (c) { box.appendChild(el('div', { class: 'check ' + c.level }, [el('b', { text: icon[c.level] || '・' }), c.text])); });
      }, function () { });
    }, 150);
  }

  function applyMods(btn, list, msg) {
    var errors = lastChecks.filter(function (c) { return c.level === 'error'; });
    var force = false;
    if (errors.length) {
      if (!confirm('このままだと、うまく動かないかもしれません:\n\n' + errors.map(function (c) { return '・' + c.text; }).join('\n') + '\n\nそれでも入れますか？')) return Promise.resolve();
      force = true;
    }
    return run(btn, call('apply', { selected: list, force: force }), msg).then(function (s) {
      modsTouched = false;
      state = s;
      render();
    });
  }

  $('mods-apply').addEventListener('click', function () {
    applyMods(this, selected(), 'ゲームに入れました。「ホーム」の「MOD ありで遊ぶ」で遊べます。');
  });
  $('mods-none').addEventListener('click', function () {
    if (!confirm('MOD を全部オフにしますか？（記録と設定は残ります）')) return;
    state.mods.forEach(function (m) { sel[m.folder] = false; });
    lastChecks = [];
    applyMods(this, [], 'MOD を全部オフにしました');
  });

  // ほかの人の MOD を足す・外す
  function afterImport(s) {
    var errs = s.importErrors || [];
    state = s;
    modsTouched = true;
    s.mods.forEach(function (m) { if (!(m.folder in sel)) sel[m.folder] = false; });
    render();
    if (errs.length) toast('足せなかったもの:\n' + errs.join('\n'), true);
    else toast('MOD を足しました。「使う」にチェックして「ゲームに入れる」を押してね。');
  }

  $('mod-import').addEventListener('click', function () {
    run(this, call('importMod', {})).then(afterImport);
  });

  function removeMod(m, btn) {
    if (!confirm(m.name + ' をランチャーから外しますか？\n（ゲームに入っていたら、消さずに ramenmod_backup へ移します）')) return;
    run(btn, call('removeMod', { id: m.id }), m.name + ' を外しました').then(function (s) { delete sel[m.folder]; state = s; render(); });
  }

  // ドラッグ＆ドロップ（zip を画面のどこにはなしても足す）
  var dragDepth = 0;
  function hasFiles(e) { return e.dataTransfer && Array.prototype.indexOf.call(e.dataTransfer.types, 'Files') >= 0; }
  window.addEventListener('dragenter', function (e) { if (!hasFiles(e)) return; e.preventDefault(); dragDepth++; $('drop-overlay').hidden = false; });
  window.addEventListener('dragover', function (e) { if (hasFiles(e)) e.preventDefault(); });
  window.addEventListener('dragleave', function () { if (--dragDepth <= 0) { dragDepth = 0; $('drop-overlay').hidden = true; } });
  window.addEventListener('drop', function (e) {
    e.preventDefault();
    dragDepth = 0;
    $('drop-overlay').hidden = true;
    var files = Array.prototype.filter.call(e.dataTransfer.files, function (f) { return /\.zip$/i.test(f.name); });
    if (!files.length) { toast('MOD の zip をドロップしてね', true); return; }
    show('mods');
    toast('たしかめています…');
    run(null, callFiles('importMod', {}, files)).then(afterImport);
  });

  // ---------------------------------------------------------------------------
  // MOD の設定
  // ---------------------------------------------------------------------------
  // タブは「設定の項目がある MOD」（ramen-launcher.json の config）から作る
  var cfgMod = null;
  var cfgData = null;
  var cfgInputs = {};

  function cfgMods() { return state ? state.mods.filter(function (m) { return m.hasConfig; }) : []; }

  function renderTabs() {
    var box = $('cfg-tabs');
    box.innerHTML = '';
    cfgMods().forEach(function (m) {
      box.appendChild(el('button', { type: 'button', class: 'tab', role: 'tab', 'aria-selected': m.folder === cfgMod ? 'true' : 'false',
        text: m.name.replace(/（.*）/, ''), onclick: function () { loadConfig(m.folder); } }));
    });
  }

  function loadConfig(mod) {
    var list = cfgMods();
    cfgMod = mod || cfgMod || (list[0] && list[0].folder);
    if (!list.some(function (m) { return m.folder === cfgMod; })) cfgMod = list[0] && list[0].folder;
    renderTabs();
    if (!cfgMod) { $('cfg-sections').innerHTML = ''; return Promise.resolve(); }
    return call('getConfig', { mod: cfgMod }).then(renderConfig, function (err) { toast(err.message, true); });
  }

  function control(f, inFx) {
    var get, node;
    var label = f.label.replace(/^演出: /, '');
    if (f.kind === 'Bool') {
      var cb = el('input', { type: 'checkbox' });
      cb.checked = f.value === 'true';
      get = function () { return cb.checked ? 'true' : 'false'; };
      node = inFx ? el('label', { class: 'fx' }, [cb, label]) : el('label', { class: 'field' }, [label, cb]);
    } else if (f.kind === 'Int' && f.max - f.min <= 100) {
      var rg = el('input', { type: 'range', min: f.min, max: f.max, step: 1, value: f.value });
      var txt = el('span', { text: label + '：' + f.value });
      rg.addEventListener('input', function () { txt.textContent = label + '：' + rg.value; });
      get = function () { return String(rg.value); };
      node = el('label', { class: 'field-col' }, [txt, rg]);
    } else if (f.kind === 'Int' || f.kind === 'Number') {
      var num = el('input', { type: 'number', min: f.min, max: f.max < 1e9 ? f.max : null, step: f.kind === 'Int' ? 1 : (f.max <= 1 ? 0.01 : 0.1), value: f.value });
      get = function () { return num.value; };
      var dollar = /\$/.test(label);
      node = el('label', { class: 'field' }, [label.replace(/（\$）|、\$）/, function (m) { return m === '（$）' ? '' : '）'; }),
        dollar ? el('span', { class: 'unit' }, ['$', num]) : num]);
    } else if (f.kind === 'Key') {
      var key = el('input', { type: 'text', class: 'key', value: f.value, maxlength: 30, spellcheck: 'false' });
      get = function () { return key.value; };
      node = el('label', { class: 'field' }, [label, key]);
    } else if (f.kind === 'Choice') {
      var s = el('select');
      f.choices.forEach(function (c, i) {
        var o = el('option', { value: c, text: f.choiceLabels[i] });
        if (c === f.value) o.selected = true;
        s.appendChild(o);
      });
      get = function () { return s.value; };
      node = el('label', { class: 'field' }, [label, s]);
    } else {
      var t = el('input', { type: 'text', class: 'wide', value: f.value, spellcheck: 'false' });
      get = function () { return t.value; };
      var hint = f.kind === 'SizeList' ? '横x縦 を , で区切る（例: 1280x720, 1600x900）' : ', で区切る（例: ' + f.value.split(',').slice(0, 2).join(',') + '）';
      node = el('div', { class: 'field-col' }, [el('label', { class: 'field' }, [label, t]), el('span', { class: 'hint', text: hint })]);
    }
    cfgInputs[f.name] = { field: f, get: get };
    return node;
  }

  function renderConfig(data) {
    cfgData = data;
    cfgInputs = {};
    // 項目の group ごとに 1 枚のカード（並びは ramen-launcher.json のとおり）。chip の項目は小さいチェックで並べる
    var groups = [];
    var byGroup = {};
    data.fields.forEach(function (f) {
      var g = f.group || '設定';
      if (!byGroup[g]) { byGroup[g] = []; groups.push(g); }
      byGroup[g].push(f);
    });
    var box = $('cfg-sections');
    box.innerHTML = '';
    groups.forEach(function (g) {
      var sec = el('section', { class: 'card' }, [el('h2', { class: 'h2', text: g })]);
      var chips = byGroup[g].filter(function (f) { return f.chip; });
      if (chips.length) {
        var grid = el('div', { class: 'fx-grid' });
        chips.forEach(function (f) { grid.appendChild(control(f, true)); });
        sec.appendChild(grid);
      }
      byGroup[g].filter(function (f) { return !f.chip; }).forEach(function (f) { sec.appendChild(control(f, false)); });
      box.appendChild(sec);
    });
    $('cfg-from').textContent = data.from === 'game' ? 'ゲームの設定を表示しています' : data.from === 'default' ? 'はじめの設定です（まだ変えていません）' : '';
  }

  $('cfg-save').addEventListener('click', function () {
    if (!cfgData) return;
    var values = {};
    var n = 0;
    Object.keys(cfgInputs).forEach(function (k) {
      var v = String(cfgInputs[k].get()).trim();
      if (v !== cfgInputs[k].field.value) { values[k] = v; n++; }
    });
    if (n === 0) { toast('変えたところがありません'); return; }
    run(this, call('setConfig', { mod: cfgMod, values: values }), '保存しました。ゲームを起動しなおすと反映されます。').then(renderConfig);
  });
  $('cfg-reset').addEventListener('click', function () {
    if (!confirm('この MOD の設定を、はじめの設定にもどしますか？（いまのものは config.lua.bak に残します）')) return;
    run(this, call('resetConfig', { mod: cfgMod }), 'はじめの設定にもどしました').then(renderConfig);
  });

  // ---------------------------------------------------------------------------
  // 記録
  // ---------------------------------------------------------------------------
  var rec = null;
  var recEmotes = {};

  function loadRecords() {
    return call('records').then(function (r) {
      rec = r;
      var s = $('rec-world');
      var keep = s.value;
      s.innerHTML = '';
      r.worlds.forEach(function (w) { s.appendChild(el('option', { value: w.id, text: w.id.slice(0, 8) + '（' + w.lastWrite + '）' })); });
      if (keep && r.worlds.some(function (w) { return w.id === keep; })) s.value = keep;
      var fs = $('rec-fortune-in');
      fs.innerHTML = '';
      fs.appendChild(el('option', { value: '', text: '（なし）' }));
      r.fortunes.forEach(function (f) { fs.appendChild(el('option', { value: f.key, text: f.name })); });
      $('rec-empty').hidden = r.worlds.length > 0;
      $('rec-body').hidden = r.worlds.length === 0;
      showWorld();
    }, function (err) { toast(err.message, true); });
  }

  function world() {
    var id = $('rec-world').value;
    return rec && rec.worlds.filter(function (w) { return w.id === id; })[0];
  }

  function fillEffects(key, effect) {
    var es = $('rec-effect-in');
    es.innerHTML = '';
    var f = rec.fortunes.filter(function (x) { return x.key === key; })[0];
    (f ? f.effects : []).forEach(function (e) { es.appendChild(el('option', { value: e.key, text: e.name })); });
    if (effect) es.value = effect;
  }

  function showWorld() {
    var w = world();
    if (!w) return;
    var left = Math.max(0, rec.ceiling - w.pity);
    $('rec-pulls').textContent = w.pulls;
    $('rec-left').textContent = left;
    $('rec-bar').setAttribute('aria-valuemax', rec.ceiling);
    $('rec-bar').setAttribute('aria-valuenow', w.pity);
    $('rec-bar-fill').style.width = Math.min(100, Math.round(w.pity * 100 / Math.max(1, rec.ceiling))) + '%';
    var f = rec.fortunes.filter(function (x) { return x.key === w.fortuneKey; })[0];
    var e = f && f.effects.filter(function (x) { return x.key === w.fortuneEffect; })[0];
    $('rec-fortune-label').textContent = f ? '今日の運勢（' + w.fortuneDay + ' 日目）' : '今日の運勢';
    $('rec-fortune').textContent = f ? f.name : 'まだ';
    $('rec-effect').textContent = e ? e.name : '';

    recEmotes = {};
    w.emotes.forEach(function (t) { recEmotes[t] = true; });
    var box = $('rec-emotes');
    box.innerHTML = '';
    rec.emotes.forEach(function (em) {
      var cb = el('input', { type: 'checkbox' });
      cb.checked = !!recEmotes[em.tag];
      var lab = el('label', { class: 'emote' + (cb.checked ? ' got' : '') }, [cb, em.name]);
      cb.addEventListener('change', function () { recEmotes[em.tag] = cb.checked; lab.classList.toggle('got', cb.checked); countEmotes(); });
      box.appendChild(lab);
    });
    countEmotes();

    $('rec-pulls-in').value = w.pulls;
    $('rec-pity-in').value = w.pity;
    $('rec-fortune-in').value = w.fortuneKey || '';
    fillEffects(w.fortuneKey, w.fortuneEffect);
    $('rec-day-in').value = w.fortuneDay > 0 ? w.fortuneDay : 0;
  }

  function countEmotes() {
    var n = rec.emotes.filter(function (e) { return recEmotes[e.tag]; }).length;
    $('e-title').textContent = '手に入れたエモート　' + n + ' / ' + rec.emotes.length;
  }

  $('rec-world').addEventListener('change', showWorld);
  $('rec-fortune-in').addEventListener('change', function () { fillEffects(this.value, null); });
  $('rec-save').addEventListener('click', function () {
    var w = world();
    if (!w) return;
    var args = {
      id: w.id,
      pulls: parseInt($('rec-pulls-in').value, 10) || 0,
      pity: parseInt($('rec-pity-in').value, 10) || 0,
      emotes: Object.keys(recEmotes).filter(function (k) { return recEmotes[k]; }),
      fortuneKey: $('rec-fortune-in').value,
      fortuneEffect: $('rec-effect-in').value,
      fortuneDay: parseInt($('rec-day-in').value, 10) || 0
    };
    run(this, call('saveRecord', args), '保存しました。次にゲームを起動すると反映されます。').then(function (r) {
      rec = r;
      showWorld();
      refresh();
    });
  });

  // ---------------------------------------------------------------------------
  // 診断
  // ---------------------------------------------------------------------------
  function loadDiag() {
    return call('diag').then(function (t) { $('diag-text').textContent = t; }, function (err) { $('diag-text').textContent = err.message; });
  }
  $('diag-reload').addEventListener('click', loadDiag);
  $('diag-setpath').addEventListener('click', function () {
    run(this, call('setGamePath')).then(function (s) { state = s; render(); loadDiag(); });
  });
  document.querySelectorAll('[data-folder]').forEach(function (b) {
    b.addEventListener('click', function () { run(b, call('openFolder', { kind: b.getAttribute('data-folder') })); });
  });
  $('diag-vanilla').addEventListener('change', function () {
    var on = this.checked;
    run(this, call('vanilla', { on: on }), on ? 'バニラ（MOD なし）にしました' : 'MOD ありにもどしました').then(function (s) { state = s; render(); }, function () { refresh(); });
  });
  $('diag-reinstall').addEventListener('click', function () {
    var selected = state.mods.filter(function (m) { return m.on; }).map(function (m) { return m.folder; });
    run(this, call('apply', { selected: selected, replaceUe4ss: true }), 'UE4SS も入れなおしました').then(function (s) { state = s; render(); });
  });
  $('diag-uninstall').addEventListener('click', function () {
    if (!confirm('UE4SS と MOD を全部外しますか？\n（消さずに ramenmod_backup フォルダへ移します）')) return;
    run(this, call('uninstall'), '全部外しました').then(function (s) { state = s; modsTouched = false; render(); });
  });
  $('diag-update').addEventListener('click', function () {
    run(this, call('checkUpdate')).then(function (r) {
      if (r.available) { showUpdate(r); show('home'); }
      else toast('いちばん新しい版です（' + r.tag + '）');
    });
  });

  // ---------------------------------------------------------------------------
  // はじめ
  // ---------------------------------------------------------------------------
  window.app = { call: call, show: show, refresh: refresh };
  renderTabs();
  refresh().then(function () { show((location.hash || '#home').slice(1)); });
})();
