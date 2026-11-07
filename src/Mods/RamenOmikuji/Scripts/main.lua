-- RamenOmikuji: お店に置くおみくじ箱。近くで E で引く。
-- 大吉〜大凶で、その日（ゲームの 1 日）の間だけ効果が続く。引き直すと上書き。
-- パソコンで「おみくじ箱」を買って模様替えで置く。向きは [ ]、大きさは - =。
local C = require("ramen_common")
local MOD = "RamenOmikuji"

-------------------------------------------------------------------------------
-- 設定（Mods/RamenOmikuji/config.lua で上書きできる）
-------------------------------------------------------------------------------
local CFG = C.config(MOD, {
    price = 5,
    share = true,        -- 大吉・大凶をほかのプレイヤーにも見せる
    -- パソコンで売る「おみくじ箱」（モップの飾りを借りる）
    shopItemId = "Men.ItemId.Decoration.Mop",
    shopRow = "Mop",
    shopPrice = 30,
    keys = { use = "E" },   -- 台の前でメニューを開くキー（UE4SS の Key の名前）
    yawStep = 5,         -- メニューで向きを 1 回に変える角度（度）
    scaleStep = 0.05,    -- メニューで大きさを 1 回に変える量
    showPrompt = true,   -- 台の前に「E：…」の案内を出す
    reach = 170,
    fx = { flash = true, shake = true, slowmo = true, presign = true, fireworks = true, sound = true },
    -- rate = 1000 回あたり
    fortunes = {
        { key = "daikichi", name = "大吉", rate = 50,  color = { 1.0, 0.82, 0.1 }, outline = { 0.45, 0.1, 0 }, level = 7,
          effects = { "sales2", "exp2", "speedSales", "tipMoney" } },
        { key = "chukichi", name = "中吉", rate = 100, color = { 1.0, 0.45, 0.35 }, outline = { 0.35, 0, 0 }, level = 6,
          effects = { "sales15", "exp15", "speed18" } },
        { key = "shokichi", name = "小吉", rate = 150, color = { 1.0, 0.65, 0.55 }, outline = { 0.3, 0.05, 0 }, level = 5,
          effects = { "sales12", "speed13", "exp12" } },
        { key = "kichi",    name = "吉",   rate = 200, color = { 0.95, 0.95, 0.8 }, outline = { 0.2, 0.15, 0 }, level = 4,
          effects = { "money", "items" } },
        { key = "suekichi", name = "末吉", rate = 200, color = { 0.8, 0.8, 0.75 }, outline = { 0.1, 0.1, 0.1 }, level = 3,
          effects = { "nothing", "coins" } },
        { key = "kyo",      name = "凶",   rate = 200, color = { 0.55, 0.6, 0.8 }, outline = { 0, 0, 0.15 }, level = 2,
          effects = { "sales08", "speed07", "tipsy", "stumble" } },
        { key = "daikyo",   name = "大凶", rate = 100, color = { 0.6, 0.25, 0.8 }, outline = { 0.05, 0, 0.1 }, level = 1,
          effects = { "thief", "poop", "drunk", "sales05" } },
    },
})

-- 効果: text = 画面のすみに出す文、sales / exp / speed = 倍率、ほかは特別な動き
local EFFECTS = {
    sales2     = { text = "売り上げ ×2", sales = 2.0 },
    exp2       = { text = "お店の経験値 ×2", exp = 2.0 },
    speedSales = { text = "足が速い ＋ 売り上げ ×1.5", speed = 1.5, sales = 1.5 },
    tipMoney   = { text = "お客さんがチップをくれる（会計ごとに +$3）＋ $30", tip = 3, money = 30 },
    sales15    = { text = "売り上げ ×1.5", sales = 1.5 },
    exp15      = { text = "お店の経験値 ×1.5", exp = 1.5 },
    speed18    = { text = "足がとても速い", speed = 1.8 },
    sales12    = { text = "売り上げ ×1.2", sales = 1.2 },
    speed13    = { text = "足が少し速い", speed = 1.3 },
    exp12      = { text = "お店の経験値 ×1.2", exp = 1.2 },
    money      = { text = "お金 +$15", money = 15 },
    items      = { text = "いい具材が 3 個", items = 3 },
    nothing    = { text = "まあまあ。特に何も起きない" },
    coins      = { text = "ときどき小銭が落ちてくる", coins = true },
    sales08    = { text = "売り上げ ×0.8", sales = 0.8 },
    speed07    = { text = "足が重い", speed = 0.7 },
    tipsy      = { text = "1 日ずっと ほろ酔い", yoi = 35 },
    stumble    = { text = "ときどき つまずく", stumble = true },
    thief      = { text = "泥棒が来る！", thief = true },
    poop       = { text = "うんこが降ってくる", poop = true },
    drunk      = { text = "1 日ずっと 酔っ払い", yoi = 80 },
    sales05    = { text = "売り上げ ×0.5", sales = 0.5 },
}

local GOOD_ITEMS = { "Men.ItemId.Food.BoiledEgg_A", "Men.ItemId.Food.Slice_CharSiu_Rosu", "Men.ItemId.Food.Gyoza.WellCooked",
                     "Men.ItemId.Food.Karaage.WellCooked", "Men.ItemId.Food.Crab.Fried" }

-- config.lua の rates = { daikichi = 50, ... } で確率だけ変えられる
for _, f in ipairs(CFG.fortunes) do
    if CFG.rates and CFG.rates[f.key] then f.rate = CFG.rates[f.key] end
end

-- 絵は Scripts/textures.lua に埋めこんであり、data/tex/ に書き出して使う
local TEX = C.unpackTextures(MOD, require("textures"))
local data = {}      -- { fortune = {key, effect, day}, adjust = {[飾りID] = {yaw, scale}} }
local world = nil
local box = nil      -- いま引いている箱
local busy = false
local prompt = nil
local hud = nil
local baseSpeed = nil

local function say(msg) C.chat("おみくじ", msg) end
local function save() C.saveData(MOD, world, data) end

local function fortuneByKey(k)
    for _, f in ipairs(CFG.fortunes) do if f.key == k then return f end end
end

local function today()
    local gs = C.gameState()
    local ok, d = pcall(function() return gs:GetDisplayDayNumber() end)
    return ok and d or 0
end

-------------------------------------------------------------------------------
-- 箱の見た目
-------------------------------------------------------------------------------
local function build(origin, yaw, scale)
    local o = { origin = origin, yaw = yaw, texDir = TEX, scale = scale }
    local dark = { 0.18, 0.11, 0.07 }
    C.part(o, { shape = "cube", at = { 0, 0, 40 }, size = { 50, 50, 80 }, color = dark })
    C.part(o, { shape = "cube", at = { 0, 0, 81.5 }, size = { 46, 46, 3 }, tex = "wood_plain.png" })
    C.part(o, { shape = "cube", at = { 0, 0, 104 }, size = { 40, 40, 42 }, tex = "wood_plain.png" })
    C.part(o, { shape = "cube", at = { 0, 0, 126.5 }, size = { 42, 42, 3 }, tex = "wood_plain.png" })
    C.part(o, { shape = "cyl", at = { 0, 0, 128.1 }, size = { 16, 16, 0.4 }, color = { 0.02, 0.02, 0.02 } })
    C.part(o, { shape = "cube", at = { 20.3, 0, 104 }, size = { 0.6, 36, 40 }, tex = "omikuji_front.png" })
    return o
end


-------------------------------------------------------------------------------
-- 効果
-------------------------------------------------------------------------------
local function current()
    local f = data.fortune
    if not f or f.day ~= today() then return nil end
    return EFFECTS[f.effect], fortuneByKey(f.key)
end

local function showHud()
    local e, f = current()
    if not e then C.hideText(hud); hud = nil; return end
    if not hud then hud = C.newText() end
    C.styleText(hud, "今日の運勢：" .. f.name .. "（" .. e.text .. "）", f.color, 18, f.outline, 2)
    C.showText(hud, 0.5, 0.06, 1.0, 400)
end

local function applySpeed()
    local p = C.pawn()
    if not p then return end
    if not baseSpeed then baseSpeed = { walk = p.WalkMoveSpeed, sprint = p.SprintMoveSpeed } end
    local e = current()
    local m = (e and e.speed) or 1
    p.WalkMoveSpeed = baseSpeed.walk * m
    p.SprintMoveSpeed = baseSpeed.sprint * m
end

-- 引いたときに 1 回だけ起きること
local function applyOnce(e)
    if e.money then C.addMoney(e.money) end
    if e.items then
        for i = 1, e.items do
            C.later(i * 0.2, function()
                local l = C.pawn():K2_GetActorLocation()
                C.spawnItem(GOOD_ITEMS[math.random(#GOOD_ITEMS)], { X = l.X, Y = l.Y, Z = l.Z + 120 }, { X = 0, Y = 0, Z = 50 })
            end)
        end
    end
    if e.thief and C.isHost() then pcall(function() C.localPC().CheatManager:MenSpawnThief() end) end
    if e.yoi then C.console("yoi " .. e.yoi) end
end

-- 売り上げ・チップ: お客さんが払ったときに差を足す（ホストだけ）
RegisterHook("/Script/RealRamenSimulator.MenGameState:MulticastCheckoutPaymentAtLocation", function(self, amount, loc, isSale)
    local a, sale = amount:get(), isSale:get()
    if not sale or a <= 0 then return end
    C.now(function()
        if not C.isHost() then return end
        local e = current()
        if not e then return end
        local extra = 0
        if e.sales then extra = extra + a * (e.sales - 1) end
        if e.tip then extra = extra + e.tip end
        if extra ~= 0 then
            C.gameState():ApplyMoneyDelta(extra)
            print(string.format("[RamenOmikuji] 会計 $%.1f に %+.1f\n", a, extra))
        end
    end)
end)

-- 経験値: 会計の経験値が入る前に量を見て、あとで差を足す（ホストだけ）
RegisterHook("/Script/RealRamenSimulator.MenGameState:CommitPendingCheckoutXp", function(self)
    local gs = self:get()
    local e = current()
    if not e or not e.exp then return end
    local ok, pending = pcall(function() return gs.PendingCheckoutXp end)
    if not ok or pending <= 0 then return end
    local extra = math.floor(pending * (e.exp - 1) + 0.5)
    if extra ~= 0 then
        C.later(0.1, function() if C.isHost() then pcall(function() C.gameState():DebugAddShopExp(extra) end) end end)
    end
end)

-- 1 日続く効果（ときどき起きること）
local tickN = 0
C.loop(1000, function()
    if not world then return end
    tickN = tickN + 1
    local f = data.fortune
    if f and f.day ~= today() then
        data.fortune = nil
        save()
        say("運勢の効果が切れた。また引いてね")
        applySpeed(); showHud()
        return
    end
    local e = current()
    if not e then return end
    local p = C.pawn()
    if not p then return end
    if e.coins and tickN % 45 == 0 then
        C.addMoney(math.random(1, 3))
        C.sound("money")
        say("小銭が落ちていた！")
    end
    if e.stumble and math.random() < 1 / 60 then
        pcall(function()
            local m = C.load("/Game/Men/Animation/AnimationMontage/Player/AM_Men_041_FrontSmallDamage.AM_Men_041_FrontSmallDamage")
            p.CharacterMeshComponent:GetAnimInstance():Montage_Play(m, 1.0, 0, 0.0, true)
        end)
        say("つまずいた！")
    end
    if e.poop and tickN % 30 == 0 then
        local l = p:K2_GetActorLocation()
        for i = 1, 3 do
            C.spawnItem("Men.ItemId.Food.Poop", { X = l.X + (math.random() - 0.5) * 150, Y = l.Y + (math.random() - 0.5) * 150, Z = l.Z + 250 + i * 30 })
        end
    end
    if e.yoi and tickN % 20 == 0 then C.console("yoi " .. e.yoi) end
end, "tick")

-------------------------------------------------------------------------------
-- 演出
-------------------------------------------------------------------------------
local function present(f, e, fromName)
    local lv = f.level
    local strong = (lv >= 6) or (lv <= 1)
    C.banner((fromName and (fromName .. " は ") or "") .. f.name, e.text, {
        color = f.color, outline = f.outline, scale = strong and 3.0 or 2.2, hold = strong and 3.2 or 2.4 })
    local at = box and C.offset(box, 30 * (box.scale or 1), 0, 110 * (box.scale or 1)) or C.pawn():K2_GetActorLocation()
    if CFG.fx.sound then
        if lv >= 4 then C.sound("win", at) else C.sound("lose", at) end
        if lv == 7 then C.sound("money", at) end
        if lv == 1 then C.sound("zawa", at) end
    end
    if CFG.fx.flash then C.flash(f.color, strong and 0.35 or 0.12, strong and 0.7 or 0.35) end
    if CFG.fx.shake and strong then C.shake(lv == 1 and 1.2 or 0.7, 0.8) end
    if CFG.fx.fireworks then
        local n = (lv == 7) and 8 or ((lv >= 5) and 3 or 0)
        for k = 1, n do
            C.later(k * 0.15, function()
                C.vfx("win", { X = at.X + (math.random() - 0.5) * 220, Y = at.Y + (math.random() - 0.5) * 220, Z = at.Z + 220 + math.random() * 80 }, 0.5)
            end)
        end
        if lv <= 2 then C.vfx("lose", at, 0.6) end
    end
    if CFG.fx.slowmo and strong then C.slowmo(0.4, 1.2) end
end

local forcedEffect = nil  -- ためし用: 「omikuji test sales2」で次の効果を決める

local function draw()
    if busy or not world then return end
    if not C.spend(CFG.price) then
        say(string.format("お金が足りない…（1 回 $%d）", CFG.price))
        return
    end
    busy = true
    -- 運勢と効果を決める
    local total = 0
    for _, f in ipairs(CFG.fortunes) do total = total + f.rate end
    local r, f = math.random() * total, CFG.fortunes[#CFG.fortunes]
    for _, x in ipairs(CFG.fortunes) do
        r = r - x.rate
        if r < 0 then f = x break end
    end
    local key = f.effects[math.random(#f.effects)]
    if forcedEffect then
        -- ためし用: 決めた効果と、その効果が入っている運勢にする
        for _, x in ipairs(CFG.fortunes) do
            for _, k in ipairs(x.effects) do if k == forcedEffect then f, key = x, k end end
        end
        forcedEffect = nil
    end
    local e = EFFECTS[key]

    -- 箱をふる音、前触れ
    if CFG.fx.sound then C.sound("dice", box and C.offset(box, 0, 0, 110 * (box.scale or 1))) end
    local wait = 1.0
    if CFG.fx.presign and (f.level == 7 or f.level == 1 or math.random() < 0.08) then
        C.later(0.6, function()
            C.flash(f.level == 1 and { 0.4, 0.1, 0.6 } or { 1, 0.8, 0.2 }, 0.3, 0.6)
            C.sound("zawa")
        end)
        wait = 1.8
    end
    C.later(wait, function()
        data.fortune = { key = f.key, effect = key, day = today() }
        save()
        present(f, e)
        applyOnce(e)
        applySpeed()
        showHud()
        say(string.format("【%s】 %s（今日いっぱい）", f.name, e.text))
        if CFG.share and (f.level == 7 or f.level == 1) then
            C.broadcast("［おみくじ］" .. C.myName() .. " は【" .. f.name .. "】！ " .. e.text)
        end
        C.later(1.5, function() busy = false end)
    end)
end

-------------------------------------------------------------------------------
-- 置き方: パソコンで「おみくじ箱」（モップの飾りを借りたもの）を買って、模様替えで置く
-------------------------------------------------------------------------------
local boxes = C.placeables({
    itemId = CFG.shopItemId,
    supplyRow = CFG.shopRow,
    shopName = "おみくじ箱",
    price = CFG.shopPrice,
    keys = CFG.keys,
    reach = CFG.reach,
    yawStep = CFG.yawStep,
    scaleStep = CFG.scaleStep,
    showPrompt = CFG.showPrompt,
    promptY = 0.75,
    build = function(origin, yaw, scale) return build(origin, yaw, scale) end,
    adjust = function()
        data.adjust = data.adjust or {}
        return data.adjust
    end,
    save = function() save() end,
    prompt = function(obj) return "E：おみくじ" end,
    useLabel = string.format("おみくじを引く（$%d）", CFG.price),
    use = function(obj)
        if busy then return end
        box = obj
        draw()
    end,
})

-------------------------------------------------------------------------------
-- お店に入ったら、そのワールドの保存を読む
-------------------------------------------------------------------------------
local lastPawn = nil
C.loop(1000, function()
    local p = C.pawn()
    if not p or p:GetAddress() == lastPawn then return end
    local w = C.worldId()
    if not w then return end
    lastPawn = p:GetAddress()
    world = w
    data = C.loadData(MOD, world)
    box = nil
    boxes.clear()
    baseSpeed = nil
    C.later(3.0, function()
        applySpeed()
        showHud()
    end)
end, "tick")

C.onChat(function(sender, body, id)
    if not CFG.share or id == -1 or sender == C.myName() then return end
    local name, fname, text = body:match("^［おみくじ］(.-) は【(.-)】！ (.*)$")
    if not name then return end
    for _, f in ipairs(CFG.fortunes) do
        if f.name == fname then C.now(function() present(f, { text = text }, name) end) end
    end
end)

-------------------------------------------------------------------------------
-- コンソール: omikuji draw / info / turn <度> / size <数> / test <効果>
-------------------------------------------------------------------------------
RegisterConsoleCommandHandler("omikuji", function(full, params, ar)
    local cmd = params[1] or "info"
    C.now(function()
        if not world then say("お店に入ってから使ってね") return end
        if cmd == "draw" then
            box = boxes.facing() or box
            draw()
        elseif (cmd == "turn" or cmd == "size") and tonumber(params[2]) then
            local n = tonumber(params[2])
            boxes.adjust(cmd == "turn" and n or 0, cmd == "size" and n or 0)
        elseif cmd == "test" and params[2] then
            forcedEffect = params[2]
            say("ためし: 次の効果は " .. params[2])
        else
            local e, f = current()
            say(e and ("今日の運勢：" .. f.name .. "（" .. e.text .. "）") or "今日はまだ引いていない")
        end
    end)
    return true
end)

math.randomseed(os.time() + 17)
print("[RamenOmikuji] loaded\n")
