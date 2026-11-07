-- RamenGacha: お店に置くガチャガチャ。パソコンで買って模様替えで置く。近くで E のメニューから 1 回・10 連・向き・大きさ・動かす。
-- 等級は 並・上・特上・極・神。天井あり。MOD のエモートはガチャで出るまで使えない。
-- 向きは [ ]（Shift で 1°）、大きさは - =（Shift で細かく）。
local C = require("ramen_common")
local MOD = "RamenGacha"

-------------------------------------------------------------------------------
-- 設定（Mods/RamenGacha/config.lua で上書きできる）
-------------------------------------------------------------------------------
local CFG = C.config(MOD, {
    price = 10,          -- 1 回
    price10 = 100,       -- 10 連
    ceiling = 100,       -- この回数「神」が出なければ、次で「神」確定
    share = true,        -- 極・神をほかのプレイヤーにも見せる
    reach = 170,         -- 近くとみなす距離（cm）
    -- 演出（false で止める・数字で強さ）
    fx = { flash = true, shake = true, slowmo = true, presign = true, fireworks = true, sound = true },
    -- 等級: rate = 1000 回あたり
    tiers = {
        { key = "nami",   name = "並",   rate = 550, color = { 0.85, 0.85, 0.85 }, outline = { 0.1, 0.1, 0.1 } },
        { key = "jo",     name = "上",   rate = 300, color = { 0.35, 0.7, 1.0 },   outline = { 0, 0.1, 0.35 } },
        { key = "tokujo", name = "特上", rate = 100, color = { 1.0, 0.3, 0.3 },    outline = { 0.3, 0, 0 } },
        { key = "kiwami", name = "極",   rate = 40,  color = { 1.0, 0.8, 0.1 },    outline = { 0.4, 0.15, 0 } },
        { key = "kami",   name = "神",   rate = 10,  color = { 1.0, 1.0, 1.0 },    outline = { 0.6, 0, 0.6 }, rainbow = true },
    },
    -- 景品
    cheap = { "Men.ItemId.Food.Nori", "Men.ItemId.Food.Naruto_Cut", "Men.ItemId.Food.Menma_A", "Men.ItemId.Food.Corn",
              "Men.ItemId.Food.Negi.Ao", "Men.ItemId.Food.Moyashi", "Men.ItemId.Food.Kikurage", "Men.ItemId.Food.Butter" },
    good = { "Men.ItemId.Food.BoiledEgg_A", "Men.ItemId.Food.Slice_CharSiu_Rosu", "Men.ItemId.Food.Slice_CharSiu_Bara",
             "Men.ItemId.Food.Gyoza.WellCooked", "Men.ItemId.Food.Karaage.WellCooked" },
    dish = { "Men.ItemId.Food.Crab.Fried", "Men.ItemId.Food.CharsiuBowl", "Men.ItemId.Food.Karaage.WellCooked",
             "Men.ItemId.Food.Gyoza.WellCooked" },
    rainCount = 20,      -- 極の具材の雨
    ramenCount = 8,      -- 神のラーメンの雨
    -- パソコンで売る「ガチャガチャ」（清掃中看板を借りる）
    shopItemId = "Men.ItemId.Decoration.Wetfloor",
    shopRow = "Men.ItemId.Decoration.Wetfloor",
    shopPrice = 50,
    -- キー（UE4SS の Key の名前）
    keys = { use = "E" },   -- 台の前でメニューを開くキー（UE4SS の Key の名前）
    yawStep = 5,         -- メニューで向きを 1 回に変える角度（度）
    scaleStep = 0.05,    -- メニューで大きさを 1 回に変える量
    showPrompt = true,   -- 台の前に「E：…」の案内を出す
    tenPull = true,      -- メニューに「10 連」を出す
})

-- config.lua の rates = { nami = 550, ... } で確率だけ変えられる
for _, t in ipairs(CFG.tiers) do
    if CFG.rates and CFG.rates[t.key] then t.rate = CFG.rates[t.key] end
end

-- 絵は Scripts/textures.lua に埋めこんであり、data/tex/ に書き出して使う
local TEX = C.unpackTextures(MOD, require("textures"))
local data = {}          -- ワールドごとの保存 { pity, pulls, emotes = {tag = true}, adjust = {[飾りID] = {yaw, scale}} }
local world = nil
local machine = nil      -- いま回している台 { origin, yaw, scale, parts, actor }
local busy = false
local prompt = nil

local function say(msg) C.chat("ガチャ", msg) end
local function save() C.saveData(MOD, world, data) end

-------------------------------------------------------------------------------
-- 機械の見た目
-------------------------------------------------------------------------------
local function build(origin, yaw, scale)
    local o = { origin = origin, yaw = yaw, texDir = TEX, scale = scale }
    local white = { 0.92, 0.92, 0.9 }
    local glass = "/Game/Men/Environment/Kitchen/Material/MI_Men_Cup_TransLucent_A2.MI_Men_Cup_TransLucent_A2"
    local colors = { { 0.9, 0.15, 0.2 }, { 0.15, 0.5, 0.95 }, { 1, 0.8, 0.1 }, { 0.3, 0.85, 0.35 }, { 0.75, 0.3, 0.9 }, { 1, 0.5, 0.75 }, { 0.2, 0.85, 0.85 } }
    local rnd = 7
    local function r() rnd = (rnd * 1103515245 + 12345) % 2147483648; return rnd / 2147483648 end
    C.part(o, { shape = "cube", at = { 0, 0, 6 }, size = { 38, 44, 12 }, color = white })
    local z = 12
    for _ = 1, 2 do
        C.part(o, { shape = "cube", at = { 0, 0, z + 15 }, size = { 36, 42, 30 }, color = white })
        C.part(o, { shape = "cube", at = { 18.3, 0, z + 15 }, size = { 0.6, 40, 28 }, tex = "gacha_front.png" })
        C.part(o, { shape = "cyl", at = { 20.3, 8.3, z + 13.6 }, size = { 12, 12, 3.5 }, rot = { Pitch = 90 }, color = { 0.98, 0.98, 0.98 } })
        C.part(o, { shape = "cube", at = { 22.2, 8.3, z + 13.6 }, size = { 1.5, 2.2, 11 }, color = { 0.85, 0.85, 0.85 } })
        z = z + 30
        C.part(o, { shape = "cube", at = { 0, 0, z + 1 }, size = { 36, 42, 2 }, color = white })
        for i = 1, 14 do
            local cx, cy = (r() - 0.5) * 22, (r() - 0.5) * 30
            local cz = z + 6 + ((i - 1) // 5) * 7 + r() * 2
            C.part(o, { shape = "sphere", at = { cx, cy, cz }, size = { 8, 8, 8 }, color = colors[(i % #colors) + 1], nocol = true })
        end
        C.part(o, { shape = "cube", at = { 0, 0, z + 15 }, size = { 34, 40, 28 }, mat = glass, nocol = true })
        z = z + 30
    end
    C.part(o, { shape = "cube", at = { 0, 0, z + 2 }, size = { 38, 44, 4 }, color = white })
    C.part(o, { shape = "cube", at = { 0, 0, z + 12 }, size = { 4, 44, 14 }, tex = "gacha_sign.png" })
    return o
end


-------------------------------------------------------------------------------
-- エモート（RamenEmotes が足したもの）の持ちもの
-------------------------------------------------------------------------------
local function modEmotes()
    local list = {}
    local ca = FindFirstOf("MenEmoteCatalogDataAsset")
    if not ca or not ca:IsValid() then return list end
    for i = 1, #ca.Entries do
        local e = ca.Entries[i]
        local tag = e.EmoteTag.TagName:ToString()
        if tag:find("^Men%.Emote%.Mod%.") then list[#list + 1] = { tag = tag, name = e.DisplayName:ToString() } end
    end
    return list
end

local function syncEmotes()
    data.emotes = data.emotes or {}
    local owned = {}
    for tag in pairs(data.emotes) do owned[#owned + 1] = tag end
    C.console("ramenemote sync " .. (#owned > 0 and table.concat(owned, ",") or "-"))
end

local function newEmote()
    local left = {}
    for _, e in ipairs(modEmotes()) do
        if not (data.emotes or {})[e.tag] then left[#left + 1] = e end
    end
    if #left == 0 then return nil end
    local e = left[math.random(#left)]
    data.emotes = data.emotes or {}
    data.emotes[e.tag] = true
    syncEmotes()
    return e
end

-------------------------------------------------------------------------------
-- 景品を出す
-------------------------------------------------------------------------------
local function pick(list) return list[math.random(#list)] end

local function spout()
    if machine then local k = machine.scale or 1; return C.offset(machine, 28 * k, 0, 30 * k) end
    local l = C.pawn():K2_GetActorLocation()
    return { X = l.X, Y = l.Y, Z = l.Z + 50 }
end

-- 完成したラーメン: 空のラーメン器を出して、開発用の関数で中身を入れる。冷めないようにする
local function makeRamen(a)
    pcall(function()
        -- たれ（醤油・塩・味噌・担々）、スープ（ふつう・鶏・豚・牛）、こってり、たれの量、麺の太さ、かたさ
        a:ForceDebugCompleteRamen(math.random(0, 3), math.random(0, 3), math.random() < 0.5, 1, math.random(0, 2), 1)
        local t = a.TemperatureComponent
        t.CoolingRate = 0
        t:SetTemperature(90)
    end)
end

local RAMEN = "RAMEN"  -- give() に渡すと、完成したラーメンを出す

local function give(itemId, strong)
    local at = spout()
    local y = machine and math.rad(machine.yaw) or 0
    local s = strong and 1 or 0.4
    local imp = {
        X = (math.cos(y) * 250 + (math.random() - 0.5) * 300) * s,
        Y = (math.sin(y) * 250 + (math.random() - 0.5) * 300) * s,
        Z = (250 + math.random() * 250) * s,
    }
    if itemId == RAMEN then
        local a = C.spawnItem("Men.ItemId.Cookware.Donburi", at, imp)
        if a then makeRamen(a) end
        return a
    end
    local a = C.spawnItem(itemId, at, imp)
    if not a then pcall(function() C.localPC().CheatManager:MenSpawnItem(itemId) end) end
    return a
end

local forced = nil  -- ためし用: 「gacha test kami」で次の 1 回の等級を決める

-- 1 回ぶんの中身を決める
local function rollTier()
    data.pity = (data.pity or 0) + 1
    if data.pity >= CFG.ceiling then return CFG.tiers[5], true end
    if forced then
        for _, t in ipairs(CFG.tiers) do
            if t.key == forced then forced = nil; return t, false end
        end
    end
    local total = 0
    for _, t in ipairs(CFG.tiers) do total = total + t.rate end
    local r = math.random() * total
    for _, t in ipairs(CFG.tiers) do
        r = r - t.rate
        if r < 0 then return t, false end
    end
    return CFG.tiers[1], false
end

-- 景品を渡して、説明の文を返す
local function award(t, quiet)
    if t.key == "kami" then data.pity = 0 end
    if t.key == "nami" then
        local a, b = pick(CFG.cheap), pick(CFG.cheap)
        give(a); give(b)
        return "具材 2 個"
    elseif t.key == "jo" then
        give(pick(CFG.good)); give(pick(CFG.cheap)); give(pick(CFG.cheap))
        return "いい具材 ＋ 具材 2 個"
    elseif t.key == "tokujo" then
        local e = newEmote()
        if e then return "エモート「" .. e.name .. "」を手に入れた！" end
        give(pick(CFG.dish)); give(pick(CFG.dish))
        return "料理 2 品"
    elseif t.key == "kiwami" then
        for i = 1, CFG.rainCount do
            C.later(i * 0.08, function() give(i % 3 == 0 and pick(CFG.good) or pick(CFG.cheap), true) end)
        end
        return "具材の雨！ " .. CFG.rainCount .. " 個"
    else
        for i = 1, CFG.ramenCount do
            C.later(i * 0.15, function() give(RAMEN, true) end)
        end
        local e = newEmote()
        return "ラーメンの雨！ " .. CFG.ramenCount .. " 杯（冷めない）" .. (e and ("＋ エモート「" .. e.name .. "」") or "")
    end
end

-------------------------------------------------------------------------------
-- 演出
-------------------------------------------------------------------------------
local function tierIndex(t)
    for i, x in ipairs(CFG.tiers) do if x == t then return i end end
    return 1
end

local function present(t, desc, fromName)
    local i = tierIndex(t)
    local style = { color = t.color, outline = t.outline, scale = 1.6 + i * 0.35, hold = 1.6 + i * 0.4 }
    local title = (fromName and (fromName .. " の ") or "") .. t.name
    C.banner(title, desc, style)
    local at = spout()
    if CFG.fx.sound then
        if i >= 3 then C.sound("win", at); C.sound("money", at) elseif i == 1 then C.sound("lose", at) else C.sound("win", at) end
    end
    if CFG.fx.flash and i >= 2 then C.flash(t.color, 0.08 + i * 0.06, 0.25 + i * 0.1) end
    if CFG.fx.shake and i >= 3 then C.shake(0.4 + (i - 3) * 0.5, 0.4 + (i - 3) * 0.4) end
    if CFG.fx.fireworks and i >= 2 then
        local n = (i - 1) * 2
        for k = 1, n do
            C.later(k * 0.12, function()
                local p = { X = at.X + (math.random() - 0.5) * 200, Y = at.Y + (math.random() - 0.5) * 200, Z = at.Z + 220 + math.random() * 80 }
                C.vfx("win", p, 0.35 + i * 0.05)
            end)
        end
    end
    if t.rainbow and CFG.fx.flash then
        local cols = { { 1, 0.2, 0.2 }, { 1, 0.7, 0.1 }, { 0.3, 1, 0.3 }, { 0.2, 0.6, 1 }, { 0.7, 0.3, 1 } }
        for k, col in ipairs(cols) do C.later(0.25 * k, function() C.flash(col, 0.45, 0.25) end) end
    end
    if CFG.fx.slowmo and i >= 4 then C.slowmo(0.35, 1.5) end
end

-- 前触れ: いいものほど出やすい。たまにハズレでも出る（あおり）
local function presign(best)
    if not CFG.fx.presign then return 0 end
    local i = tierIndex(best)
    local show = (i >= 4) or (i == 3 and math.random() < 0.5) or (i <= 2 and math.random() < 0.06)
    if not show then return 0 end
    local col = (i >= 5) and { 1, 0.2, 1 } or { 1, 0.75, 0.1 }
    C.sound("zawa")
    C.flash(col, 0.35, 0.6)
    if CFG.fx.shake then C.shake(0.25, 0.8) end
    local t = C.newText()
    C.styleText(t, (i >= 5) and "！？" or "…！", col, 44, { 0, 0, 0 }, 4)
    C.showText(t, 0.5, 0.38, 3.5, 950)
    C.later(1.0, function() C.hideText(t) end)
    return 1.1
end

-------------------------------------------------------------------------------
-- 回す
-------------------------------------------------------------------------------
local function shareResult(t, desc)
    if not CFG.share then return end
    if t.key == "kiwami" or t.key == "kami" then
        C.broadcast("［ガチャ］" .. C.myName() .. " が【" .. t.name .. "】を引いた！ " .. desc)
    end
end

local function pull(n)
    if busy or not world then return end
    local price = (n == 10) and CFG.price10 or CFG.price
    if not C.spend(price) then
        say(string.format("お金が足りない…（%d 回 $%d）", n, price))
        return
    end
    busy = true
    data.pulls = (data.pulls or 0) + n
    if CFG.fx.sound then C.sound("dice", spout()) end

    local results = {}
    local best = nil
    for _ = 1, n do
        local t = rollTier()
        results[#results + 1] = t
        if not best or tierIndex(t) > tierIndex(best) then best = t end
    end
    save()

    local delay = 0.6 + presign(best)
    if n == 1 then
        C.later(delay, function()
            local desc = award(results[1])
            present(results[1], desc)
            say(string.format("【%s】 %s（天井まで あと %d 回）", results[1].name, desc, CFG.ceiling - (data.pity or 0)))
            shareResult(results[1], desc)
            save()
            C.later(1.5, function() busy = false end)
        end)
    else
        -- 10 連: 一番いいもの以外を速く見せて、最後に一番いいもの
        local order = {}
        local bestIdx
        for i, t in ipairs(results) do if t == best and not bestIdx then bestIdx = i else order[#order + 1] = t end end
        local names = {}
        for k, t in ipairs(order) do
            C.later(delay + (k - 1) * 0.28, function()
                award(t, true)
                names[#names + 1] = t.name
                local small = C.newText()
                C.styleText(small, t.name, t.color, 40, t.outline, 3)
                C.showText(small, 0.2 + ((k - 1) % 9) * 0.075, 0.68, 1.4, 800)
                C.later(2.2 + (#order - k) * 0.28, function() C.hideText(small) end)
                if CFG.fx.sound then C.sound(tierIndex(t) >= 2 and "money" or "dice", spout()) end
            end)
        end
        C.later(delay + #order * 0.28 + 0.5, function()
            local desc = award(best)
            present(best, desc)
            say(string.format("10 連: %s ／ 一番は【%s】 %s（天井まで あと %d 回）", table.concat(names, "・"), best.name, desc,
                CFG.ceiling - (data.pity or 0)))
            shareResult(best, desc)
            save()
            C.later(2.0, function() busy = false end)
        end)
    end
end

-------------------------------------------------------------------------------
-- 置き方: パソコンで「ガチャガチャ」（清掃中看板を借りたもの）を買って、模様替えで置く
-------------------------------------------------------------------------------
local machines = C.placeables({
    itemId = CFG.shopItemId,
    supplyRow = CFG.shopRow,
    shopName = "ガチャガチャ",
    price = CFG.shopPrice,
    keys = CFG.keys,
    reach = CFG.reach,
    yawStep = CFG.yawStep,
    scaleStep = CFG.scaleStep,
    showPrompt = CFG.showPrompt,
    build = function(origin, yaw, scale) return build(origin, yaw, scale) end,
    adjust = function()
        data.adjust = data.adjust or {}
        return data.adjust
    end,
    save = function() save() end,
    prompt = function(obj)
        return string.format("E：ガチャ（天井まで あと %d 回）", CFG.ceiling - (data.pity or 0))
    end,
    useLabel = string.format("1 回まわす（$%d）", CFG.price),
    shiftUse = CFG.tenPull,
    shiftLabel = string.format("10 連（$%d）", CFG.price10),
    use = function(obj, shift)
        if busy then return end
        machine = obj
        pull(shift and 10 or 1)
    end,
})

-------------------------------------------------------------------------------
-- お店に入ったら、そのワールドの保存を読む
-------------------------------------------------------------------------------
local lastPawn = nil
C.loop(1000, function()
    local p = C.pawn()
    if not p then return end
    if p:GetAddress() == lastPawn then return end
    local w = C.worldId()
    if not w then return end
    lastPawn = p:GetAddress()
    world = w
    data = C.loadData(MOD, world)
    machine = nil
    machines.clear()
    C.later(3.0, function() syncEmotes() end)
end, "tick")

-- ほかの人のすごい結果
C.onChat(function(sender, body, id)
    if not CFG.share or id == -1 then return end
    if sender == C.myName() then return end
    local name, tier = body:match("^［ガチャ］(.-) が【(.-)】を引いた！")
    if not name then return end
    for _, t in ipairs(CFG.tiers) do
        if t.name == tier then
            C.now(function() present(t, body:match("を引いた！ (.*)$") or "", name) end)
        end
    end
end)

-------------------------------------------------------------------------------
-- コンソール: gacha info / pull / pull10 / turn <度> / size <数> / test <等級>
-------------------------------------------------------------------------------
RegisterConsoleCommandHandler("gacha", function(full, params, ar)
    local cmd = params[1] or "info"
    C.now(function()
        if not world then say("お店に入ってから使ってね") return end
        if cmd == "pull" or cmd == "pull10" then
            machine = machines.facing() or machine
            pull(cmd == "pull10" and 10 or 1)
        elseif (cmd == "turn" or cmd == "size") and tonumber(params[2]) then
            -- 近くで向いている台の向き（度）・大きさ（足す数）を変える
            local n = tonumber(params[2])
            machines.adjust(cmd == "turn" and n or 0, cmd == "size" and n or 0)
        elseif cmd == "test" and params[2] then
            forced = params[2]
            say("ためし: 次の 1 回は " .. params[2])
        else
            local owned, all = 0, #modEmotes()
            for _ in pairs(data.emotes or {}) do owned = owned + 1 end
            local n = 0
            for _ in pairs(machines.objs) do n = n + 1 end
            say(string.format("回した回数 %d ／ 天井まで あと %d 回 ／ エモート %d / %d ／ 置いてある台 %d",
                data.pulls or 0, CFG.ceiling - (data.pity or 0), owned, all, n))
        end
    end)
    return true
end)

math.randomseed(os.time())
print("[RamenGacha] loaded\n")
