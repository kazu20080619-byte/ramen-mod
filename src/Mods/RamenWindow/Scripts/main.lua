-- RamenWindow: フルスクリーンと小さいウィンドウを、キー 1 つで切りかえる。
-- F11: フルスクリーン → ウィンドウ 1280x720 → ウィンドウ 1600x900 → フルスクリーン
-- Alt + Enter: フルスクリーン ⇄ さっきのウィンドウ
-- コンソール: ramenwindow full / window / borderless / 1280x720 / next
local C = require("ramen_common")
local MOD = "RamenWindow"

-------------------------------------------------------------------------------
-- 設定（Mods/RamenWindow/config.lua で上書きできる）
-------------------------------------------------------------------------------
local CFG = C.config(MOD, {
    sizes = { { 1280, 720 }, { 1600, 900 } },
    fullMode = nil,   -- 0 = フルスクリーン、1 = ボーダーレス。nil なら起動したときの種類
    key = "F11",
    altEnter = true,
    pad = nil,
    message = true,
})

local FULL, BORDERLESS, WINDOWED = 0, 1, 2

local function say(msg)
    if CFG.message then C.chat("画面", msg) else print("[" .. MOD .. "] " .. msg .. "\n") end
end

-------------------------------------------------------------------------------
-- GameUserSettings（ゲームの画面設定）
-------------------------------------------------------------------------------
local function gus()
    local ok, s = pcall(function()
        return StaticFindObject("/Script/Engine.Default__GameUserSettings"):GetGameUserSettings()
    end)
    if ok and s and s:IsValid() then return s end
end

local function desktop()
    local s = gus()
    if s then
        local ok, w, h = pcall(function()
            local r = s:GetDesktopResolution()
            return r.X, r.Y
        end)
        if ok and w and h and w > 0 and h > 0 then return w, h end
    end
    return 1920, 1080
end

-- いまの状態: idx = 0 ならフルスクリーン、1 以上なら sizes[idx] のウィンドウ
local idx = nil
local lastWin = 1
local startFull = nil  -- 起動したときのフルスクリーンの種類

local function readState()
    if idx then return end
    idx, startFull = 0, FULL
    local s = gus()
    if not s then return end
    local ok, mode = pcall(function() return s:GetFullscreenMode() end)
    if not ok or mode == nil then return end
    if mode ~= WINDOWED then startFull = mode return end
    startFull = BORDERLESS
    idx = #CFG.sizes  -- 知らない大きさのウィンドウなら、次はフルスクリーン
    local ok2, w, h = pcall(function()
        local r = s:GetScreenResolution()
        return r.X, r.Y
    end)
    if ok2 then
        for i, sz in ipairs(CFG.sizes) do
            if sz[1] == w and sz[2] == h then idx, lastWin = i, i end
        end
    end
end

local function fullMode()
    if CFG.fullMode == FULL or CFG.fullMode == BORDERLESS then return CFG.fullMode end
    return startFull or FULL
end

-- 画面を切りかえる。GameUserSettings がだめなら r.SetRes を使う
local function apply(mode, w, h)
    local s = gus()
    if s then
        local ok, err = pcall(function()
            s:SetScreenResolution({ X = w, Y = h })
            s:SetFullscreenMode(mode)
            s:ApplySettings(false)
            -- 確定もする（しないと、次に起動したとき 前の「確定した」画面にもどる）
            s:ConfirmVideoMode()
            s:SaveSettings()
        end)
        if ok then return true end
        print("[" .. MOD .. "] GameUserSettings failed: " .. tostring(err) .. "\n")
    end
    local suffix = (mode == WINDOWED and "w") or (mode == BORDERLESS and "wf") or "f"
    C.console(string.format("r.SetRes %dx%d%s", w, h, suffix))
    return true
end

-------------------------------------------------------------------------------
-- 切りかえ
-------------------------------------------------------------------------------
local function goFull()
    readState()
    local m = fullMode()
    local w, h = desktop()
    apply(m, w, h)
    idx = 0
    say(m == BORDERLESS and "フルスクリーン（ボーダーレス）" or "フルスクリーン")
end

local function goWindowSize(w, h)
    readState()
    local dw, dh = desktop()
    w, h = math.min(w, dw), math.min(h, dh)
    apply(WINDOWED, w, h)
    say(string.format("ウィンドウ %dx%d", w, h))
end

local function goWindow(i)
    local sz = CFG.sizes[i]
    if not sz then return end
    lastWin = i
    goWindowSize(sz[1], sz[2])
    idx = i
end

-- F11: フルスクリーン → sizes[1] → sizes[2] → … → フルスクリーン
local function cycle()
    readState()
    if idx < #CFG.sizes then goWindow(idx + 1) else goFull() end
end

-- Alt + Enter: フルスクリーン ⇄ さっきのウィンドウ
local function toggle()
    readState()
    if idx == 0 then goWindow(lastWin) else goFull() end
end

-------------------------------------------------------------------------------
-- キー・パッド・コンソール
-------------------------------------------------------------------------------
local k = Key[CFG.key]
if k then
    RegisterKeyBind(k, function() C.now(cycle, "cycle") end)
else
    print("[" .. MOD .. "] unknown key: " .. tostring(CFG.key) .. "\n")
end

if CFG.altEnter then
    RegisterKeyBind(Key.RETURN, { ModifierKey.ALT }, function() C.now(toggle, "toggle") end)
end

-- パッドの見回りはゲームのスレッドで動くので、そのまま呼ぶ
if CFG.pad and C.onPad then C.onPad(CFG.pad, toggle) end  -- パッドの仕組みがある版のときだけ

local HELP = [[
ramenwindow full        フルスクリーン
ramenwindow borderless  ボーダーレス（ウィンドウのフルスクリーン）
ramenwindow window      ウィンドウ（さっきの大きさ）
ramenwindow 1280x720    ウィンドウ（好きな大きさ）
ramenwindow next        F11 と同じ（順番に切りかえ）]]

RegisterConsoleCommandHandler("ramenwindow", function(full, params, ar)
    local cmd = (params[1] or ""):lower()
    local w, h = cmd:match("^(%d+)x(%d+)$")
    local function done(fn) C.now(fn, "console") return true end
    if cmd == "full" or cmd == "fullscreen" then return done(goFull)
    elseif cmd == "borderless" then
        return done(function()
            local dw, dh = desktop()
            apply(BORDERLESS, dw, dh)
            idx = 0
            say("フルスクリーン（ボーダーレス）")
        end)
    elseif cmd == "window" then return done(function() readState() goWindow(lastWin) end)
    elseif cmd == "next" then return done(cycle)
    elseif w then
        return done(function()
            goWindowSize(tonumber(w), tonumber(h))
            idx = #CFG.sizes  -- 次の F11 はフルスクリーン
        end)
    end
    for line in HELP:gmatch("[^\n]+") do ar:Log(line) end
    return true
end)

print("[" .. MOD .. "] loaded\n")
