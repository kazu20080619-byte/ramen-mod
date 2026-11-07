-- RamenBenri: べんり3GX から持ってきた便利機能。F1 でべんりメニュー（↑↓ 選ぶ・←→ 変える・Enter 決定）
-- コンソールは @ か F10 で開く。「benri」と打つと使い方が出る。設定は Mods/RamenBenri/config.lua

local C = require("ramen_common")
local MOD = "RamenBenri"
local CFG = C.config(MOD, {
    menuKey = "F1",                              -- メニューを開くキー（UE4SS の Key の名前）
    ctrlKeys = true,                             -- Ctrl+F1〜F6 のキーも使う
    ctrlMoney = 10000,                           -- Ctrl+F1 で足すお金
    money = { 1000, 10000, 100000, 1000000 },    -- メニューの「お金を足す」の候補
    moneyStart = 2,                              -- 最初に選ばれている候補（何番目か）
    speeds = { 1.0, 2.0, 4.0, 8.0, 0.5 },        -- ゲームの速さの候補（1 番目がふつう）
    walkSpeeds = { 1.0, 2.0, 3.0, 0.5 },         -- 歩くはやさの候補（1 番目がふつう）
    slots = 5,                                   -- 場所を覚えられる数
    rememberPlaces = true,                       -- 覚えた場所をワールドごとに保存する（次に遊ぶときも使える）
    maxLevel = 30,                               -- 「お店のレベルにする」の上限
})

local function pc()
    local p = FindFirstOf("PlayerController")
    if p and p:IsValid() then return p end
end

local function cheat()
    local p = pc()
    if p and p.CheatManager:IsValid() then return p.CheatManager end
    print("[RamenBenri] CheatManager がまだない（お店に入ってから使ってね）\n")
end

local function pawn()
    local p = pc()
    if p and p.Pawn:IsValid() then return p.Pawn end
end

-- ログと、チャット欄（自分だけに見える）に出す
local function say(msg)
    print("[RamenBenri] " .. msg .. "\n")
    local c = FindFirstOf("MenTextChatComponent")
    if c and c:IsValid() then
        pcall(function() c:ClientReceiveMessage({ SenderName = "べんり", Body = msg, SenderId = -1 }) end)
    end
end

-- お金・お店 ---------------------------------------------------------------
local function addMoney(n)
    local c = cheat(); if not c then return end
    c:MenAddMoney(n); say(string.format("お金 +%d", n))
end

local function shopLevel(n)
    local c = cheat(); if not c then return end
    c:MenSetShopLevel(n); say("お店のレベル " .. n)
end

local function addExp(n)
    local c = cheat(); if not c then return end
    c:MenAddShopExp(n); say("お店の経験値 +" .. n)
end

local function spawnItem(id)
    local c = cheat(); if not c then return end
    c:MenSpawnItem(id); say("アイテムを出した: " .. id)
end

-- ゲームの速さ -------------------------------------------------------------
local SPEEDS = CFG.speeds
local speedIndex = 1
local function setGameSpeed(v)
    local c = cheat(); if not c then return end
    c:Slomo(v); say("ゲームの速さ x" .. v)
end
local function nextGameSpeed()
    speedIndex = speedIndex % #SPEEDS + 1
    setGameSpeed(SPEEDS[speedIndex])
end

-- 歩くはやさ ---------------------------------------------------------------
-- 主人公は Mover で動くので、MenPlayerCharacter の WalkMoveSpeed / SprintMoveSpeed を変える
local WALK = CFG.walkSpeeds
local walkIndex = 1
local base = nil
local function setWalk(mult)
    local p = pawn(); if not p then return end
    if not base then base = { walk = p.WalkMoveSpeed, sprint = p.SprintMoveSpeed } end
    p.WalkMoveSpeed = base.walk * mult
    p.SprintMoveSpeed = base.sprint * mult
    say("歩くはやさ x" .. mult)
end
local function nextWalk()
    walkIndex = walkIndex % #WALK + 1
    setWalk(WALK[walkIndex])
end

-- 飛ぶ・すりぬけ -----------------------------------------------------------
local moveMode = 0  -- 0=ふつう 1=飛ぶ 2=すりぬけ
local function mover()
    local p = pawn(); if not p then return end
    local m = p.CharacterMoverComponent
    if m and m:IsValid() then return p, m end
end
local function applyMoveMode()
    local p, m = mover(); if not m then return end
    if moveMode == 0 then
        p:SetActorEnableCollision(true)
        m:QueueNextMode(FName("Falling"), true)
        say("ふつうに歩く")
    else
        m:QueueNextMode(FName("Flying"), true)
        p:SetActorEnableCollision(moveMode == 1)
        say(moveMode == 1 and "飛ぶ（ジャンプで上・しゃがみで下）" or "飛んで壁をすりぬける")
    end
end
local function nextMoveMode()
    moveMode = (moveMode + 1) % 3
    applyMoveMode()
end

-- 場所の記憶・ワープ -------------------------------------------------------
-- rememberPlaces なら、ワールドごとに Mods/RamenBenri/data/<ワールド>.lua に残す
local saved, savedWorld = {}, nil
local function places()
    if not CFG.rememberPlaces then return saved end
    local w = C.worldId()
    if w and w ~= savedWorld then
        savedWorld = w
        local d = C.loadData(MOD, w)
        saved = d.places or {}
    end
    return saved
end
local function savePos(slot)
    local p = pawn(); if not p then return end
    local l = p:K2_GetActorLocation()
    local list = places()
    list[slot] = { X = l.X, Y = l.Y, Z = l.Z }
    if CFG.rememberPlaces and savedWorld then C.saveData(MOD, savedWorld, { places = list }) end
    say(string.format("場所 %d を覚えた (%.0f, %.0f, %.0f)", slot, l.X, l.Y, l.Z))
end
local function loadPos(slot)
    local p = pawn(); if not p then return end
    local s = places()[slot]
    if not s then say("場所 " .. slot .. " はまだ覚えていない") return end
    p:K2_SetActorLocation(s, false, {}, true)
    say("場所 " .. slot .. " へワープ")
end

-- キー（Ctrl を押しながら） -----------------------------------------------
if CFG.ctrlKeys then
    local CTRL = { ModifierKey.CONTROL }
    C.key(Key.F1, CTRL, function() addMoney(CFG.ctrlMoney) end, "ctrl+f1")
    C.key(Key.F2, CTRL, nextGameSpeed, "ctrl+f2")
    C.key(Key.F3, CTRL, nextWalk, "ctrl+f3")
    C.key(Key.F4, CTRL, nextMoveMode, "ctrl+f4")
    C.key(Key.F5, CTRL, function() savePos(1) end, "ctrl+f5")
    C.key(Key.F6, CTRL, function() loadPos(1) end, "ctrl+f6")
end

-- べんりメニュー（3GX のように、F1 で開いて ↑↓ で選び、←→ で変えて、Enter で決定） --
local MONEY = CFG.money
local menu = { open = false, index = 1, widget = nil, money = math.max(1, math.min(#MONEY, CFG.moneyStart)), level = nil, slot = 1 }

-- メニューを開いているあいだ、ゲームの操作（歩く・しゃがむなど）を止める
local IMC = "/Game/Men/Input/IMC_Men_Default.IMC_Men_Default"
local function gameInput(on)
    pcall(function()
        local sub = FindFirstOf("EnhancedInputLocalPlayerSubsystem")
        local imc = StaticFindObject(IMC)
        local has = sub:HasMappingContext(imc, {})
        if on and not has then sub:AddMappingContext(imc, 0, {}) end
        if not on and has then sub:RemoveMappingContext(imc, {}) end
    end)
end

local function gs() return FindFirstOf("MenGameState") end
local function curLevel()
    local ok, n = pcall(function() return gs():GetShopLevel() end)
    return ok and n or 0
end
local MOVE_NAMES = { [0] = "ふつう", [1] = "飛ぶ", [2] = "壁すりぬけ" }

-- 項目: text() = 表示、left/right = ←→、enter = Enter
local ITEMS = {
    { text = function() return string.format("お金を足す　◀ $%d ▶", MONEY[menu.money]) end,
      left = function() menu.money = math.max(1, menu.money - 1) end,
      right = function() menu.money = math.min(#MONEY, menu.money + 1) end,
      enter = function() addMoney(MONEY[menu.money]) end },
    { text = function() return string.format("ゲームの速さ　◀ x%s ▶", SPEEDS[speedIndex]) end,
      left = function() speedIndex = (speedIndex - 2) % #SPEEDS + 1; setGameSpeed(SPEEDS[speedIndex]) end,
      right = function() speedIndex = speedIndex % #SPEEDS + 1; setGameSpeed(SPEEDS[speedIndex]) end,
      enter = function() speedIndex = 1; setGameSpeed(1.0) end },
    { text = function() return string.format("歩くはやさ　◀ x%s ▶", WALK[walkIndex]) end,
      left = function() walkIndex = (walkIndex - 2) % #WALK + 1; setWalk(WALK[walkIndex]) end,
      right = function() walkIndex = walkIndex % #WALK + 1; setWalk(WALK[walkIndex]) end,
      enter = function() walkIndex = 1; setWalk(1.0) end },
    { text = function() return "動き方　◀ " .. MOVE_NAMES[moveMode] .. " ▶" end,
      left = function() moveMode = (moveMode + 2) % 3; applyMoveMode() end,
      right = function() moveMode = (moveMode + 1) % 3; applyMoveMode() end,
      enter = function() moveMode = 0; applyMoveMode() end },
    { text = function() return string.format("いまの場所を覚える　◀ %d 番 ▶", menu.slot) end,
      left = function() menu.slot = math.max(1, menu.slot - 1) end,
      right = function() menu.slot = math.min(CFG.slots, menu.slot + 1) end,
      enter = function() savePos(menu.slot) end },
    { text = function() return string.format("覚えた場所へワープ　◀ %d 番 ▶%s", menu.slot, places()[menu.slot] and "" or "（まだない）") end,
      left = function() menu.slot = math.max(1, menu.slot - 1) end,
      right = function() menu.slot = math.min(CFG.slots, menu.slot + 1) end,
      enter = function() loadPos(menu.slot) end },
    { text = function() return string.format("お店のレベルにする　◀ %d ▶（いま %d）", menu.level or curLevel(), curLevel()) end,
      left = function() menu.level = math.max(0, (menu.level or curLevel()) - 1) end,
      right = function() menu.level = math.min(CFG.maxLevel, (menu.level or curLevel()) + 1) end,
      enter = function() shopLevel(menu.level or curLevel()) end },
    { text = function() return "お店の経験値 +100" end,
      enter = function() addExp(100) end },
    { text = function() return "閉じる" end,
      enter = function() menu.close() end },
}

local function render()
    if not menu.open then return end
    local lines = { "■ べんりメニュー", "" }
    for i, it in ipairs(ITEMS) do
        lines[#lines + 1] = (i == menu.index and "▶ " or "　 ") .. it.text()
    end
    lines[#lines + 1] = ""
    lines[#lines + 1] = "↑↓ 選ぶ　←→ 変える　Enter 決定　" .. CFG.menuKey .. " 閉じる"
    if not menu.widget or not menu.widget.widget:IsValid() then menu.widget = C.newText() end
    C.styleText(menu.widget, table.concat(lines, "\n"), { 1, 0.92, 0.75 }, 20, { 0.15, 0.05, 0 }, 3)
    C.showText(menu.widget, 0.24, 0.45, 1.25, 950)
    -- 左はしをそろえて、画面の大きさに合わせる（ウィンドウの小さい画面でも切れないように）
    pcall(function()
        local w = menu.widget.widget
        local vs = StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary"):GetViewportSize(FindFirstOf("PlayerController"))
        local s = 1.25 * math.min(1, vs.Y / 1080)
        w:SetAlignmentInViewport({ X = 0, Y = 0.5 })
        w:SetPositionInViewport({ X = vs.X * 0.03, Y = vs.Y * 0.45 }, true)
        local pivotOk = pcall(function() w:SetRenderTransformPivot({ X = 0, Y = 0.5 }) end)  -- 左はしを中心に大きくする（真ん中だと左にはみ出す）
        if not pivotOk then s = math.min(s, 1) end
        w:SetRenderScale({ X = s, Y = s })
    end)
end

function menu.close()
    menu.open = false
    C.hideText(menu.widget)
    menu.widget = nil
    gameInput(true)
end

local function toggle()
    if menu.open then menu.close() return end
    if not pawn() then return end  -- お店の中だけ
    menu.open = true
    menu.level = nil
    gameInput(false)  -- 開いているあいだ、歩いたり しゃがんだりしない
    render()
end

-- メニューのキー（C.key: キーの呼び出しは別のスレッドなので、ゲームのスレッドに渡すだけ）
local function onKey(k, fn)
    C.key(k, nil, function()
        if not menu.open then return end
        fn()
        render()
    end, "menu key")
end

onKey(Key.UP_ARROW, function() menu.index = (menu.index - 2) % #ITEMS + 1 end)
onKey(Key.DOWN_ARROW, function() menu.index = menu.index % #ITEMS + 1 end)
onKey(Key.LEFT_ARROW, function() local it = ITEMS[menu.index]; if it.left then it.left() end end)
onKey(Key.RIGHT_ARROW, function() local it = ITEMS[menu.index]; if it.right then it.right() end end)
onKey(Key.RETURN, function() ITEMS[menu.index].enter() end)

-- タイトルにもどったときなどは閉じる（ゲームの操作を もどす）
C.loop(1000, function()
    if menu.open and not pawn() then menu.close() end
end, "menu close")

local MENU_KEY = Key[CFG.menuKey] or Key.F1  -- メニューを開くキー（config.lua の menuKey）
if not Key[CFG.menuKey] then print("[RamenBenri] unknown menuKey: " .. tostring(CFG.menuKey) .. "\n") end
C.key(MENU_KEY, nil, toggle, "menu")

-- コンソール ---------------------------------------------------------------
local HELP = [[
benri money <数>      お金を足す（例: benri money 50000）
benri speed <倍率>    ゲームの速さ（例: benri speed 4 / benri speed 1）
benri walk <倍率>     歩くはやさ（例: benri walk 2）
benri fly / ghost / walk  飛ぶ・すりぬけ・ふつう
benri save <番号>     いまの場所を覚える
benri warp <番号>     覚えた場所へワープ
benri level <数>      お店のレベル
benri exp <数>        お店の経験値を足す
benri item <ID>       アイテムを出す
メニュー: F1（↑↓ ←→ Enter）　キー: Ctrl+F1 お金+1万 / F2 ゲームの速さ / F3 歩くはやさ / F4 飛ぶ・すりぬけ / F5 場所を覚える / F6 ワープ]]

RegisterConsoleCommandHandler("benri", function(full, params, ar)
    local cmd, arg = params[1], params[2]
    local n = tonumber(arg)
    local function done(fn) ExecuteInGameThread(fn) return true end
    if cmd == "money" and n then return done(function() addMoney(n) end)
    elseif cmd == "speed" and n then return done(function() setGameSpeed(n) end)
    elseif cmd == "walk" and n then return done(function() setWalk(n) end)
    elseif cmd == "walk" then return done(function() moveMode = 0; applyMoveMode() end)
    elseif cmd == "fly" then return done(function() moveMode = 1; applyMoveMode() end)
    elseif cmd == "ghost" then return done(function() moveMode = 2; applyMoveMode() end)
    elseif cmd == "save" then return done(function() savePos(n or 1) end)
    elseif cmd == "warp" then return done(function() loadPos(n or 1) end)
    elseif cmd == "level" and n then return done(function() shopLevel(n) end)
    elseif cmd == "exp" and n then return done(function() addExp(n) end)
    elseif cmd == "menu" then return done(toggle)
    elseif cmd == "item" and arg then return done(function() spawnItem(arg) end)
    end
    for line in HELP:gmatch("[^\n]+") do ar:Log(line) end
    return true
end)

-- お店を出入りしたら、歩くはやさの基準を取り直す
RegisterHook("/Script/Engine.PlayerController:ClientRestart", function()
    base = nil; walkIndex = 1; moveMode = 0
end)

say("loaded")
