-- ramen_common: ラーメン趣味レーター MOD の共通の部品
-- 置き物（基本の形の組み立て）・どデカ表示・演出・保存・チャット・ほかの人への合図
local C = {}

C.R = "/Script/RealRamenSimulator."

-------------------------------------------------------------------------------
-- 基本
-------------------------------------------------------------------------------
function C.load(path)
    local o = StaticFindObject(path)
    if o and o:IsValid() then return o end
    o = LoadAsset(path)
    if o and o:IsValid() then return o end
end

function C.localPC()
    for _, p in ipairs(FindAllOf("PlayerController") or {}) do
        if p:IsValid() and p:IsLocalController() then return p end
    end
end

function C.pawn()
    local pc = C.localPC()
    if pc and pc.Pawn:IsValid() then return pc.Pawn end
end

function C.gameState()
    local gs = FindFirstOf("MenGameState")
    if gs and gs:IsValid() then return gs end
end

-- このPCがホスト（お金やお店を動かせる側）か
function C.isHost()
    local gs = C.gameState()
    if not gs then return false end
    local ok, r = pcall(function() return gs:HasAuthority() end)
    return ok and r
end

function C.playerCount()
    local gs = C.gameState()
    local ok, n = pcall(function() return gs:GetActivePlayerCount() end)
    return ok and n or 1
end

-- エラーが起きたらログに残して、ほかを止めない
function C.safe(fn, where)
    return function(...)
        local ok, err = pcall(fn, ...)
        if not ok then print("[ramen_common] error in " .. (where or "?") .. ": " .. tostring(err) .. "\n") end
    end
end

-------------------------------------------------------------------------------
-- 待つ・くり返すは、ゲームのスレッドで動かす。
-- UE4SS の LoopAsync / ExecuteWithDelay は、MOD の Lua を別のスレッドで動かす。同じ MOD の中で
-- ゲームのスレッドの Lua（フック・ExecuteInGameThread の中身）と同時に動くと Lua のメモリがこわれて、
-- 「Ref was not function」が出たり、値がまざったり、ゲームが落ちたりする。
-- 古い UE4SS で …InGameThread… がないときだけ、前のやり方にする。
-------------------------------------------------------------------------------
C.GAME_THREAD = type(LoopInGameThreadWithDelay) == "function"
    and type(ExecuteInGameThreadWithDelay) == "function" and type(CancelDelayedAction) == "function"

function C.later(sec, fn)
    local f = C.safe(fn, "later")
    local ms = math.max(1, math.floor(sec * 1000))
    if C.GAME_THREAD then
        ExecuteInGameThreadWithDelay(ms, f)
    else
        ExecuteWithDelay(ms, function() ExecuteInGameThread(f) end)
    end
end

-- ms ごとに fn を呼ぶ（ゲームのスレッドで）。fn が true を返したら止まる
function C.loop(ms, fn, where)
    local stopped, handle = false, nil
    if C.GAME_THREAD then
        handle = LoopInGameThreadWithDelay(ms, function()
            if stopped then return end
            local ok, r = pcall(fn)
            if not ok then
                print("[ramen_common] error in " .. (where or "loop") .. ": " .. tostring(r) .. "\n")
            elseif r then
                stopped = true
                if handle then CancelDelayedAction(handle) end
            end
        end)
        return
    end
    local f = C.safe(function() if not stopped and fn() then stopped = true end end, where or "loop")
    LoopAsync(ms, function()
        if stopped then return true end
        ExecuteInGameThread(f)
        return false
    end)
end

function C.now(fn, where)
    ExecuteInGameThread(C.safe(fn, where or "now"))
end

-- キー: UE4SS はキーの呼び出しも別のスレッドで動かすので、そこでは ゲームのスレッドに渡すだけにする
-- （関数は先に作っておいて、押したときに Lua で新しい物を作らない）
function C.key(k, mods, fn, where)
    local f = C.safe(fn, where or "key")
    local cb = function() ExecuteInGameThread(f) end
    if mods then RegisterKeyBind(k, mods, cb) else RegisterKeyBind(k, cb) end
end

function C.console(cmd)
    local pc = C.localPC()
    if pc then
        StaticFindObject("/Script/Engine.Default__KismetSystemLibrary"):ExecuteConsoleCommand(pc, cmd, pc)
    end
end

-- Mods フォルダの場所（shared の場所から求める）
local MODS = (function()
    for p in package.path:gmatch("[^;]+") do
        local m = p:match("^(.*[/\\]Mods)[/\\]shared[/\\]%?%.lua$")
        if m then return (m:gsub("\\", "/")) .. "/" end
    end
    return "./Mods/"
end)()
function C.modDir(name) return MODS .. name .. "/" end

-------------------------------------------------------------------------------
-- 設定ファイル（Mods/<名前>/config.lua。なければ defaults のまま）
-------------------------------------------------------------------------------
local function merge(dst, src)
    for k, v in pairs(src) do
        if type(v) == "table" and type(dst[k]) == "table" and #v == 0 then merge(dst[k], v) else dst[k] = v end
    end
    return dst
end

function C.config(modName, defaults)
    local path = C.modDir(modName) .. "config.lua"
    local f = io.open(path, "r")
    if f then
        local src = f:read("a"); f:close()
        local chunk, err = load(src, "config", "t", {})
        if chunk then
            local ok, t = pcall(chunk)
            if ok and type(t) == "table" then merge(defaults, t)
            else print("[" .. modName .. "] config.lua を読めなかったので、はじめの設定で動きます: " .. tostring(t) .. "\n") end
        else
            print("[" .. modName .. "] config.lua に書きまちがいがあるので、はじめの設定で動きます: " .. tostring(err) .. "\n")
        end
    end
    return defaults
end

-------------------------------------------------------------------------------
-- ワールドごとの保存（Mods/<名前>/data/<ワールドID>.lua）
-------------------------------------------------------------------------------
function C.worldId()
    local s = FindFirstOf("MenWorldSaveSubsystem")
    if not s or not s:IsValid() then return nil end
    local ok, id = pcall(function() return s:GetActiveSlotId() end)
    if not ok or not id then return nil end
    if id.A == 0 and id.B == 0 and id.C == 0 and id.D == 0 then return nil end
    return string.format("%08x-%08x-%08x-%08x", id.A & 0xffffffff, id.B & 0xffffffff, id.C & 0xffffffff, id.D & 0xffffffff)
end

local function ser(v, ind)
    ind = ind or ""
    local t = type(v)
    if t == "table" then
        local out = { "{\n" }
        for k, x in pairs(v) do
            local key = type(k) == "string" and string.format("[%q]", k) or ("[" .. tostring(k) .. "]")
            out[#out + 1] = ind .. "  " .. key .. " = " .. ser(x, ind .. "  ") .. ",\n"
        end
        out[#out + 1] = ind .. "}"
        return table.concat(out)
    elseif t == "string" then
        return string.format("%q", v)
    else
        return tostring(v)
    end
end

function C.loadData(modName, world)
    if not world then return {} end
    local f = io.open(C.modDir(modName) .. "data/" .. world .. ".lua", "r")
    if not f then return {} end
    local src = f:read("a"); f:close()
    local chunk = load("return " .. src, "data", "t", {})
    local ok, t = pcall(chunk or function() return {} end)
    return (ok and type(t) == "table") and t or {}
end

function C.saveData(modName, world, data)
    if not world then return end
    local path = C.modDir(modName) .. "data/" .. world .. ".lua"
    local f = io.open(path, "w")
    if not f then
        -- data フォルダがないときだけ作る（毎回作ると、黒い窓が一瞬出るため）
        os.execute('mkdir "' .. (C.modDir(modName) .. "data"):gsub("/", "\\") .. '" 2>nul')
        f = io.open(path, "w")
    end
    if f then f:write(ser(data)); f:close() end
end

-------------------------------------------------------------------------------
-- チャット欄（自分だけに見える）と、みんなへの合図
-------------------------------------------------------------------------------
function C.chat(sender, body)
    print("[" .. sender .. "] " .. body .. "\n")
    local c = FindFirstOf("MenTextChatComponent")
    if c and c:IsValid() then
        pcall(function() c:ClientReceiveMessage({ SenderName = sender, Body = body, SenderId = -1 }) end)
    end
end

-- みんなに送る（ふつうのチャットとして届く。同じ MOD の人の画面には、どデカ表示も出る）
function C.broadcast(body)
    local pc = C.localPC()
    if not pc then return end
    for _, c in ipairs(FindAllOf("MenTextChatComponent") or {}) do
        local ok, o = pcall(function() return c:GetOwner() end)
        if ok and o and o:IsValid() and o:GetAddress() == pc:GetAddress() then
            pcall(function() c:ServerSendMessage(body, math.random(1, 2000000000)) end)
            return
        end
    end
end

-- 届いたチャットを見る。fn(senderName, body, senderId)
function C.onChat(fn)
    RegisterHook("/Script/RealRamenSimulator.MenTextChatComponent:ClientReceiveMessage", function(self, msg)
        local m = msg:get()
        local ok, s, b, id = pcall(function() return m.SenderName:ToString(), m.Body:ToString(), m.SenderId end)
        if ok then fn(s, b, id) end
    end)
end

function C.myName()
    local pc = C.localPC()
    local ok, n = pcall(function() return pc.PlayerState:GetPlayerName():ToString() end)
    return ok and n or "だれか"
end

-------------------------------------------------------------------------------
-- お金（ホストだけが動かせる）
-------------------------------------------------------------------------------
function C.money()
    local gs = C.gameState()
    if gs then return gs:GetMoney() end
    return 0
end

function C.spend(amount)
    local gs = C.gameState()
    if not gs then return false end
    if C.isHost() then return gs:TrySpendMoney(amount) end
    -- ゲスト: チートで減らす（効かないことがある）
    local pc = C.localPC()
    if gs:GetMoney() < amount then return false end
    pcall(function() pc.CheatManager:MenAddMoney(-amount) end)
    return true
end

function C.addMoney(amount)
    local gs = C.gameState()
    if not gs then return end
    if C.isHost() then gs:ApplyMoneyDelta(amount)
    else pcall(function() C.localPC().CheatManager:MenAddMoney(amount) end) end
end

-------------------------------------------------------------------------------
-- 置き物（基本の形の組み立て）
-------------------------------------------------------------------------------
local SHAPES = {
    cube = "/Engine/BasicShapes/Cube.Cube",
    cyl = "/Engine/BasicShapes/Cylinder.Cylinder",
    sphere = "/Engine/BasicShapes/Sphere.Sphere",
}
local BASIC_MAT = "/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"
local TEX_MAT = "/Game/Men/Environment/Tableware/RamenWoodBox/Material/MI_Men_RamenWoodBox.MI_Men_RamenWoodBox"
local texCache = {}

local function quat(pitch, yaw, roll)
    return StaticFindObject("/Script/Engine.Default__KismetMathLibrary"):Conv_RotatorToQuaternion(
        { Pitch = pitch or 0, Yaw = yaw or 0, Roll = roll or 0 })
end

-- 絵を Lua の中の文字（base64）から、ファイルに戻す。
-- ランチャー用の zip には .png を入れられないので、Scripts/textures.lua に埋めこんである。
local B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
local function b64decode(s)
    local map = {}
    for i = 1, #B64 do map[B64:sub(i, i)] = i - 1 end
    local out, bits, n = {}, 0, 0
    for c in s:gmatch("[%w%+/]") do
        bits = (bits << 6) | map[c]
        n = n + 6
        if n >= 8 then
            n = n - 8
            out[#out + 1] = string.char((bits >> n) & 0xff)
        end
    end
    return table.concat(out)
end

-- textures = { ["名前.png"] = "base64" }。Mods/<名前>/data/tex/ に書き出して、その場所を返す
function C.unpackTextures(modName, textures)
    local dir = C.modDir(modName) .. "data/tex/"
    for name, b64 in pairs(textures or {}) do
        local f = io.open(dir .. name, "rb")
        if f then
            f:close()
        else
            os.execute('mkdir "' .. dir:gsub("/", "\\") .. '" 2>nul')
            f = io.open(dir .. name, "wb")
            if f then f:write(b64decode(b64)); f:close() end
        end
    end
    return dir
end

function C.texture(file)
    if not texCache[file] or not texCache[file]:IsValid() then
        texCache[file] = StaticFindObject("/Script/Engine.Default__KismetRenderingLibrary"):ImportFileAsTexture2D(C.localPC(), file)
    end
    return texCache[file]
end

local function spawnMesh(meshPath, loc, rot, scale)
    local GS = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    local tf = { Rotation = quat(rot.Pitch, rot.Yaw, rot.Roll), Translation = loc, Scale3D = scale }
    local a = GS:BeginDeferredActorSpawnFromClass(C.pawn(), StaticFindObject("/Script/Engine.StaticMeshActor"), tf, 1, nil, 0)
    GS:FinishSpawningActor(a, tf, 0)
    local c = a.StaticMeshComponent
    c:SetMobility(2)
    c:SetStaticMesh(C.load(meshPath))
    return a, c
end

-- obj = { origin = {X,Y,Z}, yaw, texDir }。p = { shape, at = {前, 右, 上}, size = {奥行, 幅, 高さ} (cm),
--   rot, color = {R,G,B} / tex = "ファイル名" / mat = "材質のパス", nocol = true（ぶつからない） }
function C.part(obj, p)
    local y = math.rad(obj.yaw)
    local f = { X = math.cos(y), Y = math.sin(y) }
    local r = { X = -math.sin(y), Y = math.cos(y) }
    -- obj.scale で全体を小さく・大きくできる（キットのミニ模型など）
    local k = obj.scale or 1
    local at = { p.at[1] * k, p.at[2] * k, p.at[3] * k }
    p = setmetatable({ size = { p.size[1] * k, p.size[2] * k, p.size[3] * k }, nocol = p.nocol or obj.nocol }, { __index = p })
    local loc = {
        X = obj.origin.X + f.X * at[1] + r.X * at[2],
        Y = obj.origin.Y + f.Y * at[1] + r.Y * at[2],
        Z = obj.origin.Z + at[3],
    }
    local rot = { Pitch = (p.rot and p.rot.Pitch or 0), Yaw = obj.yaw + (p.rot and p.rot.Yaw or 0), Roll = (p.rot and p.rot.Roll or 0) }
    local s = p.size
    local a, c = spawnMesh(SHAPES[p.shape], loc, rot, { X = s[1] / 100, Y = s[2] / 100, Z = s[3] / 100 })
    if p.mat then
        c:SetMaterial(0, C.load(p.mat))
    elseif p.tex then
        local m = c:CreateDynamicMaterialInstance(0, C.load(TEX_MAT), FName("None"))
        m:SetTextureParameterValue(FName("Tex_Alvedo"), C.texture(obj.texDir .. p.tex))
    else
        local m = c:CreateDynamicMaterialInstance(0, C.load(BASIC_MAT), FName("None"))
        local col = p.color or { 1, 1, 1 }
        m:SetVectorParameterValue(FName("Color"), { R = col[1], G = col[2], B = col[3], A = 1 })
    end
    c:SetCollisionEnabled(p.nocol and 0 or 3)
    obj.parts = obj.parts or {}
    obj.parts[#obj.parts + 1] = a
    return a
end

function C.destroy(obj)
    for _, a in ipairs(obj and obj.parts or {}) do pcall(function() a:K2_DestroyActor() end) end
    if obj then obj.parts = {} end
end

-- 物の前・右・上の場所
function C.offset(obj, fwd, side, up)
    local y = math.rad(obj.yaw)
    return {
        X = obj.origin.X + math.cos(y) * fwd - math.sin(y) * side,
        Y = obj.origin.Y + math.sin(y) * fwd + math.cos(y) * side,
        Z = obj.origin.Z + up,
    }
end

-- 主人公の足もとに、主人公の方を向けて置くための場所と向き
function C.placeHere(dist)
    local p = C.pawn()
    local l = p:K2_GetActorLocation()
    local f = p:GetActorForwardVector()
    local origin = { X = l.X + f.X * dist, Y = l.Y + f.Y * dist, Z = l.Z - 96 }
    -- 床の高さを下に向けて調べる
    local KSL = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
    local ok, hit = pcall(function()
        local outHit = {}
        local got = KSL:LineTraceSingle(p, { X = origin.X, Y = origin.Y, Z = l.Z + 50 }, { X = origin.X, Y = origin.Y, Z = l.Z - 400 },
            0, false, {}, 0, outHit, true, { R = 0, G = 0, B = 0, A = 0 }, { R = 0, G = 0, B = 0, A = 0 }, 0)
        return got and outHit
    end)
    if ok and hit and hit.ImpactPoint then origin.Z = hit.ImpactPoint.Z end
    return origin, p:K2_GetActorRotation().Yaw + 180
end

-- 主人公が物の近くで、物の方を向いているか
function C.isFacing(obj, maxDist)
    local p = C.pawn()
    if not p or not obj or not obj.origin then return false end
    local l = p:K2_GetActorLocation()
    local dx, dy = obj.origin.X - l.X, obj.origin.Y - l.Y
    local d = math.sqrt(dx * dx + dy * dy)
    if d > (maxDist or 170) then return false end
    if d < 60 then return true end
    local pc = C.localPC()
    local yaw = math.rad(pc:GetControlRotation().Yaw)
    local dot = (math.cos(yaw) * dx + math.sin(yaw) * dy) / d
    return dot > 0.55
end

-- 場所 loc の下の床の高さを調べる（見つからなければ loc.Z のまま）
function C.floorAt(loc)
    local out = { X = loc.X, Y = loc.Y, Z = loc.Z }
    local KSL = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
    pcall(function()
        local hit = {}
        local got = KSL:LineTraceSingle(C.pawn(), { X = loc.X, Y = loc.Y, Z = loc.Z + 30 }, { X = loc.X, Y = loc.Y, Z = loc.Z - 300 },
            0, false, {}, 0, hit, true, { R = 0, G = 0, B = 0, A = 0 }, { R = 0, G = 0, B = 0, A = 0 }, 0)
        if got and hit.ImpactPoint then out.Z = hit.ImpactPoint.Z end
    end)
    return out
end

-- loc に置いたとき、主人公の方を向く向き
function C.yawToPlayer(loc)
    local l = C.pawn():K2_GetActorLocation()
    return math.deg(math.atan(l.Y - loc.Y, l.X - loc.X))
end

-------------------------------------------------------------------------------
-- 飾りの置き物: パソコンで売っている飾り（清掃中看板など）を借りて、
-- 名前と値段を変えて売る。置かれた飾りの見た目を隠して、MOD の模型をかぶせる。
-- 置く・動かす・片づけるはゲームの模様替えのまま。向きと大きさは MOD のキーで変える。
-------------------------------------------------------------------------------
local KEYS = {}  -- 設定の文字 → Key
local function keyOf(name)
    if KEYS[name] == nil then KEYS[name] = Key[name] or false end
    return KEYS[name] or nil
end

-- パソコンの品物の名前・値段と、アイテムの名前を書きかえる
function C.renameShopItem(supplyRow, itemId, name, price)
    pcall(function()
        local dt = StaticFindObject(C.R:gsub("%.$", "") .. ".Default__MenStatics"):GetSupplyOrderDataTable()
        local r = dt:FindRow(supplyRow)
        if r then
            if r.DisplayName:ToString() ~= name then r.DisplayName = FText(name) end
            r.Price = price
            r.UnlockLevel = 0
        end
    end)
    pcall(function()
        local idt = StaticFindObject("/Game/Men/MasterData/Item/DT_Men_ItemData.DT_Men_ItemData")
        if idt and idt:IsValid() then
            idt:ForEachRow(function(_, row)
                if row.ItemId.TagName:ToString() == itemId and row.DisplayName:ToString() ~= name then row.DisplayName = FText(name) end
            end)
        end
    end)
end

local function persistentId(a)
    local ok, id = pcall(function()
        local g = a.SaveIdentityComponent.PersistentId
        return string.format("%08x%08x%08x%08x", g.A & 0xffffffff, g.B & 0xffffffff, g.C & 0xffffffff, g.D & 0xffffffff)
    end)
    return ok and id or tostring(a:GetAddress())
end

-- opts = {
--   itemId, supplyRow, shopName, price,          -- 借りる飾りと、売るときの名前・値段
--   build = function(origin, yaw, scale) → obj,  -- 模型を作る
--   adjust = function() → { [id] = {yaw, scale} }, save = function(),  -- 向き・大きさの保存先
--   prompt = function(obj) → 文,                 -- 近くにいるときの案内
--   use = function(obj, shift),                  -- 使うキー
--   keys = { use = "E", rotL = "OEM_FOUR", rotR = "OEM_SIX", small = "OEM_MINUS", big = "OEM_PLUS" },
--   reach = 170, promptY = 0.78,
-- }
function C.placeables(opts)
    local M = { objs = {}, opts = opts }
    local keys = opts.keys or {}
    local prompt = nil

    local function dress(a, id)
        local adj = opts.adjust()[id] or {}
        local l = a:K2_GetActorLocation()
        local yaw = a:K2_GetActorRotation().Yaw + (adj.yaw or 0)
        local scale = adj.scale or 1
        pcall(function() a.MeshComponent:SetVisibility(false, false) end)
        local obj = opts.build({ X = l.X, Y = l.Y, Z = l.Z }, yaw, scale)
        for _, part in ipairs(obj.parts or {}) do
            pcall(function() part:K2_AttachToActor(a, FName("None"), 1, 1, 1, false) end)
        end
        obj.actor, obj.id, obj.scale = a, id, scale
        obj.adjYaw, obj.adjScale = adj.yaw or 0, scale
        return obj
    end

    -- 飾りの場所が動いたら、模型の基準の場所も合わせる（模型は飾りにくっついて動く）
    local function refreshOrigin(obj)
        local l = obj.actor:K2_GetActorLocation()
        obj.origin = { X = l.X, Y = l.Y, Z = l.Z }
        obj.yaw = obj.actor:K2_GetActorRotation().Yaw + obj.adjYaw
    end

    function M.scan()
        local seen = {}
        for _, a in ipairs(FindAllOf("MenBuildableActor") or {}) do
            if a:IsValid() then
                local ok, iid = pcall(function() return a:GetItemId().TagName:ToString() end)
                if ok and iid == opts.itemId then
                    local id = persistentId(a)
                    seen[id] = true
                    local obj = M.objs[id]
                    if obj and obj.actor:GetAddress() ~= a:GetAddress() then C.destroy(obj); obj = nil end
                    if not obj then M.objs[id] = dress(a, id) else refreshOrigin(obj) end
                end
            end
        end
        for id, obj in pairs(M.objs) do
            if not seen[id] then C.destroy(obj); M.objs[id] = nil end
        end
    end

    function M.clear()
        for id, obj in pairs(M.objs) do C.destroy(obj) end
        M.objs = {}
    end

    -- 近くで向いている物（いちばん近いもの）
    function M.facing()
        local best, bestD = nil, 1e9
        local p = C.pawn()
        if not p then return nil end
        local l = p:K2_GetActorLocation()
        for _, obj in pairs(M.objs) do
            local reach = (opts.reach or 170) * math.max(1, obj.scale)
            if obj.actor:IsValid() and C.isFacing(obj, reach) then
                local d = (obj.origin.X - l.X) ^ 2 + (obj.origin.Y - l.Y) ^ 2
                if d < bestD then best, bestD = obj, d end
            end
        end
        return best
    end

    -- 向き・大きさ（いまの値）
    local function adjOf(obj)
        local adj = opts.adjust()[obj.id] or {}
        local yaw = math.floor((adj.yaw or 0) + 0.5) % 360
        if yaw > 180 then yaw = yaw - 360 end
        return yaw, adj.scale or 1
    end

    local function adjustObj(obj, dYaw, dScale, quiet)
        local all = opts.adjust()
        local adj = all[obj.id] or { yaw = 0, scale = 1 }
        adj.yaw = ((adj.yaw or 0) + dYaw) % 360
        adj.scale = math.max(0.3, math.min(3.0, (adj.scale or 1) + dScale))
        adj.scale = math.floor(adj.scale * 100 + 0.5) / 100
        all[obj.id] = adj
        opts.save()
        C.destroy(obj)
        M.objs[obj.id] = dress(obj.actor, obj.id)
        if not quiet then
            local yaw, scale = adjOf(obj)
            C.chat(opts.shopName, string.format("向き %+d°　大きさ ×%.2f", yaw, scale))
        end
    end

    local function adjustNear(dYaw, dScale)
        local obj = M.facing()
        if obj then adjustObj(obj, dYaw, dScale) end
    end

    local function bind(name, mods, fn)
        local k = keyOf(name)
        if not k then print("[ramen_common] unknown key: " .. tostring(name) .. "\n") return end
        C.key(k, mods, fn, "key " .. name)
    end

    M.adjust = adjustNear  -- コンソールからも使う（gacha turn 15 など）

    ---------------------------------------------------------------------------
    -- 届いた箱: ゲームが模様替えをさせてくれないとき（チュートリアル中・営業中など）は、
    -- MOD が箱の前で E で開けて、その場に置く
    ---------------------------------------------------------------------------
    local function buildModeAllowed()
        local ok, r = pcall(function()
            local pc = C.localPC()
            local cs = pc:K2_GetComponentsByClass(StaticFindObject(C.R .. "MenBuildModeComponent"))
            return cs[1]:get():CanEnterBuildMode()
        end)
        return ok and r
    end

    function M.facingBox()
        for _, b in ipairs(FindAllOf("MenSupplyBoxActor") or {}) do
            if b:IsValid() then
                local ok, id = pcall(function() return b.BuildItemId.TagName:ToString() end)
                if ok and id == opts.itemId then
                    local l = b:K2_GetActorLocation()
                    if C.isFacing({ origin = l }, 160) then return b end
                end
            end
        end
    end

    -- 置く場所: 主人公の前（床の高さ）。向きは主人公の方
    local function frontSpot(dist)
        local p = C.pawn()
        local l = p:K2_GetActorLocation()
        local f = p:GetActorForwardVector()
        local at = C.floorAt({ X = l.X + f.X * dist, Y = l.Y + f.Y * dist, Z = l.Z })
        return at, p:K2_GetActorRotation().Yaw + 180
    end

    local function quatYaw(yaw)
        return StaticFindObject("/Script/Engine.Default__KismetMathLibrary"):Conv_RotatorToQuaternion({ Pitch = 0, Yaw = yaw, Roll = 0 })
    end

    function M.openBox(b)
        if not C.isHost() then
            C.chat(opts.shopName, "箱を MOD で開けられるのはホストだけです")
            return
        end
        local l = b:K2_GetActorLocation()
        local at = C.floorAt(l)
        local yaw = C.yawToPlayer(at)
        local sub = FindFirstOf("MenItemSpawnSubsystem")
        local tf = { Rotation = quatYaw(yaw), Translation = at, Scale3D = { X = 1, Y = 1, Z = 1 } }
        local ok, a = pcall(function() return sub:SpawnBuildableItem({ TagName = FName(opts.itemId) }, tf) end)
        if not ok or not a or not a:IsValid() then
            C.chat(opts.shopName, "箱を開けられませんでした")
            return
        end
        local n = 1
        pcall(function() n = b.BoxedItemCount end)
        if n and n > 1 then
            pcall(function() b.BoxedItemCount = n - 1 end)
        else
            pcall(function() b:K2_DestroyActor() end)
        end
        C.sound("money", at)
        C.chat(opts.shopName, opts.shopName .. "を置いた！ 向き・大きさ・場所は、前で E のメニューから")
        M.scan()
    end

    -- 置いた台を、主人公の前に動かす
    function M.moveHere(obj)
        local at, yaw = frontSpot(130)
        local adj = opts.adjust()[obj.id] or {}
        local ok = pcall(function()
            obj.actor:K2_SetActorLocationAndRotation(at, { Pitch = 0, Yaw = yaw - (adj.yaw or 0), Roll = 0 }, false, {}, true)
        end)
        if ok then
            C.destroy(obj)
            M.objs[obj.id] = nil
            M.scan()
            C.chat(opts.shopName, "ここに動かした")
        end
    end

    ---------------------------------------------------------------------------
    -- 台のメニュー: 台の前で E で開く。↑↓ 選ぶ　←→ 変える　Enter 決定　E 閉じる
    -- 開いているあいだは、ゲームの操作を止めて、キャラが動かないようにする。
    -- （パッドでの操作は、ボタンや操作の記録を何度も読むとゲームが落ちたので、いまはない）
    ---------------------------------------------------------------------------
    local menu = { open = false, id = nil, index = 1, widget = nil }
    local IMC_GAME = "/Game/Men/Input/IMC_Men_Default.IMC_Men_Default"
    local function gameInput(on)
        pcall(function()
            local sub = FindFirstOf("EnhancedInputLocalPlayerSubsystem")
            local imc = StaticFindObject(IMC_GAME)
            local has = sub:HasMappingContext(imc, {})
            if on and not has then sub:AddMappingContext(imc, 0, {}) end
            if not on and has then sub:RemoveMappingContext(imc, {}) end
        end)
    end

    local function menuObj() return menu.open and M.objs[menu.id] end

    local function closeMenu()
        if not menu.open then return end
        menu.open = false
        C.hideText(menu.widget)
        menu.widget = nil
        gameInput(true)
    end

    -- 項目: text() と、←→ の left/right、決定の enter
    local function items()
        local list = {}
        local function use(shift)
            return function()
                local o = menuObj()
                closeMenu()
                if o then opts.use(o, shift) end
            end
        end
        list[#list + 1] = { text = function() return opts.useLabel or "使う" end, enter = use(false) }
        if opts.shiftUse then
            list[#list + 1] = { text = function() return opts.shiftLabel or "10 回" end, enter = use(true) }
        end
        list[#list + 1] = {
            text = function() local o = menuObj(); local yaw = o and adjOf(o) or 0; return string.format("向き　◀ %+d° ▶", yaw) end,
            left = function() local o = menuObj(); if o then adjustObj(o, -(opts.yawStep or 5), 0, true) end end,
            right = function() local o = menuObj(); if o then adjustObj(o, opts.yawStep or 5, 0, true) end end,
            enter = function() local o = menuObj(); if o then local yaw = adjOf(o); adjustObj(o, -yaw, 0, true) end end }
        list[#list + 1] = {
            text = function() local o = menuObj(); local _, s = 0, 1; if o then _, s = adjOf(o) end; return string.format("大きさ　◀ ×%.2f ▶", s) end,
            left = function() local o = menuObj(); if o then adjustObj(o, 0, -(opts.scaleStep or 0.05), true) end end,
            right = function() local o = menuObj(); if o then adjustObj(o, 0, opts.scaleStep or 0.05, true) end end,
            enter = function() local o = menuObj(); if o then local _, s = adjOf(o); adjustObj(o, 0, 1 - s, true) end end }
        list[#list + 1] = { text = function() return "ここ（自分の前）に動かす" end,
            enter = function() local o = menuObj(); closeMenu(); if o then M.moveHere(o) end end }
        list[#list + 1] = { text = function() return "閉じる" end, enter = closeMenu }
        return list
    end
    local ITEMS = items()

    local function renderMenu()
        if not menu.open then return end
        if not menuObj() then closeMenu() return end
        local lines = { "■ " .. opts.shopName, "" }
        for i, it in ipairs(ITEMS) do
            lines[#lines + 1] = (i == menu.index and "▶ " or "　 ") .. it.text()
        end
        lines[#lines + 1] = ""
        lines[#lines + 1] = "↑↓ 選ぶ　←→ 変える　Enter 決定　E 閉じる"
        lines[#lines + 1] = "（向き・大きさは Enter で元にもどす）"
        if not menu.widget or not menu.widget.widget:IsValid() then menu.widget = C.newText() end
        C.styleText(menu.widget, table.concat(lines, "\n"), { 1, 0.92, 0.75 }, 20, { 0.15, 0.05, 0 }, 3)
        C.showText(menu.widget, 0.7, 0.45, 1, 950)
        -- 右はしにそろえて、画面の大きさに合わせる
        pcall(function()
            local w = menu.widget.widget
            local vs = StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary"):GetViewportSize(C.localPC())
            local s = 1.2 * math.min(1, vs.Y / 1080)
            w:SetAlignmentInViewport({ X = 1, Y = 0.5 })
            w:SetPositionInViewport({ X = vs.X * 0.97, Y = vs.Y * 0.45 }, true)
            local ok = pcall(function() w:SetRenderTransformPivot({ X = 1, Y = 0.5 }) end)
            if not ok then s = math.min(s, 1) end
            w:SetRenderScale({ X = s, Y = s })
        end)
    end

    local KEYFN = {
        down = function() menu.index = menu.index % #ITEMS + 1 end,
        up = function() menu.index = (menu.index - 2) % #ITEMS + 1 end,
        left = function() local it = ITEMS[menu.index]; if it.left then it.left() end end,
        right = function() local it = ITEMS[menu.index]; if it.right then it.right() end end,
        enter = function() ITEMS[menu.index].enter() end,
    }

    local function openMenu(o)
        if menu.open then return end
        menu.open, menu.id, menu.index = true, o.id, 1
        gameInput(false)
        renderMenu()
    end

    -- E: 台の前ならメニュー（開いていれば閉じる）。届いた箱の前なら開けて置く
    local function useKey()
        if menu.open then closeMenu() return end
        local o = M.facing()
        if o then openMenu(o) return end
        local b = M.facingBox()
        if b and not buildModeAllowed() then M.openBox(b) end
    end
    bind(keys.use or "E", nil, useKey)
    local function menuKey(fn) return function() if menu.open then fn(); renderMenu() end end end
    bind("UP_ARROW", nil, menuKey(KEYFN.up))
    bind("DOWN_ARROW", nil, menuKey(KEYFN.down))
    bind("LEFT_ARROW", nil, menuKey(KEYFN.left))
    bind("RIGHT_ARROW", nil, menuKey(KEYFN.right))
    bind("RETURN", nil, menuKey(KEYFN.enter))

    -- パソコンを開いたとき・ときどき、品物の名前と値段を書きかえる
    RegisterHook("/Script/RealRamenSimulator.MenPlayerController:ClientOpenSupplyOrderMenu", function()
        C.renameShopItem(opts.supplyRow, opts.itemId, opts.shopName, opts.price)
    end)

    local tick = 0
    C.loop(250, function()
            tick = tick + 1
            if tick % 4 == 0 then M.scan() end
            if tick % 20 == 1 then C.renameShopItem(opts.supplyRow, opts.itemId, opts.shopName, opts.price) end
            if menu.open and not menuObj() then closeMenu() end
            local o = (not menu.open) and M.facing()
            local msg = nil
            if o then
                msg = opts.showPrompt ~= false and opts.prompt(o) or nil
            elseif not menu.open and (tick % 2 == 0 or prompt) then
                -- 届いた箱の前（ゲームが模様替えをさせてくれないときだけ）
                local b = M.facingBox()
                if b and not buildModeAllowed() then
                    msg = "E：" .. opts.shopName .. "の箱を開けて、ここに置く"
                end
            end
            if msg then
                if not prompt then prompt = C.newText() end
                C.styleText(prompt, msg, { 1, 1, 1 }, 22, { 0, 0, 0 }, 2)
                C.showText(prompt, 0.5, opts.promptY or 0.7, 1, 600)
            elseif prompt then
                C.hideText(prompt)
                prompt = nil
            end
    end, "placeables")

    return M
end

-------------------------------------------------------------------------------
-- 画面の文字（どデカ表示・小さい文字）
-------------------------------------------------------------------------------
local TEXT_WIDGET = "/Game/Men/Blueprints/UI/HUD/TextChat/WBP_Men_HUD_TextChatMessage.WBP_Men_HUD_TextChatMessage_C"

-- 新しい文字の部品を作る。返り値 { widget, text }
function C.newText()
    local pc = C.localPC()
    local cls = C.load(TEXT_WIDGET)
    local w = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary"):Create(pc, cls, pc)
    local tb
    for _, x in ipairs(FindAllOf("MenTextBlock") or {}) do
        local ok, o = pcall(function() return x:GetOuter():GetOuter() end)
        if ok and o and o:GetAddress() == w:GetAddress() then
            if x:GetFName():ToString() == "Text_Message" then tb = x else x:SetVisibility(1) end
        end
    end
    return { widget = w, text = tb }
end

-- 文字の見た目。size は 12〜48 くらい（大きくしたいときは scale で拡大）
function C.styleText(t, str, color, size, outlineColor, outline, wrap)
    pcall(function() t.text:SetAutoWrapText(wrap == true) end)
    t.text:SetText(FText(str))
    local f = t.text.Font
    f.Size = size or 40
    pcall(function()
        f.OutlineSettings.OutlineSize = outline or 3
        local oc = outlineColor or { 0, 0, 0 }
        f.OutlineSettings.OutlineColor = { R = oc[1], G = oc[2], B = oc[3], A = 1 }
    end)
    t.text:SetFont(f)
    t.text:SetColorAndOpacity({ SpecifiedColor = { R = color[1], G = color[2], B = color[3], A = 1 }, ColorUseRule = 0 })
end

-- 画面の割合（0〜1）の場所に置く
function C.showText(t, x, y, scale, z)
    local w = t.widget
    if not w:IsInViewport() then w:AddToViewport(z or 500) end
    local WLL = StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary")
    local vs = WLL:GetViewportSize(C.localPC())
    w:SetAlignmentInViewport({ X = 0.5, Y = 0.5 })
    w:SetPositionInViewport({ X = vs.X * x, Y = vs.Y * y }, true)
    w:SetRenderScale({ X = scale, Y = scale })
    w:SetRenderOpacity(1)
end

function C.hideText(t)
    if t and t.widget and t.widget:IsValid() then pcall(function() t.widget:RemoveFromParent() end) end
end

-- どデカ表示: title（大きい字）と sub（下の小さい字）。style = { color, outline, scale, hold }
function C.banner(title, sub, style)
    style = style or {}
    local scale = style.scale or 3
    local big = C.newText()
    C.styleText(big, title, style.color or { 1, 0.85, 0.1 }, 44, style.outline or { 0.35, 0, 0 }, 4)
    C.showText(big, 0.5, 0.36, scale * 2.2, 900)
    local small
    if sub and sub ~= "" then
        small = C.newText()
        C.styleText(small, sub, { 1, 1, 1 }, 26, { 0, 0, 0 }, 3, #sub > 60)
        C.showText(small, 0.5, 0.52, 1.6, 901)
    end
    -- ぽんっと出る → そのまま → 消える
    local steps, i = 8, 0
    C.loop(30, function()
        i = i + 1
        if big.widget:IsValid() then
            local s = scale * (2.2 - 1.2 * math.min(1, i / steps))
            big.widget:SetRenderScale({ X = s, Y = s })
        end
        return i >= steps
    end, "banner pop")
    local hold = style.hold or 2.2
    C.later(hold, function()
        local k = 0
        C.loop(40, function()
            k = k + 1
            local a = math.max(0, 1 - k / 10)
            if big.widget:IsValid() then big.widget:SetRenderOpacity(a) end
            if small and small.widget:IsValid() then small.widget:SetRenderOpacity(a) end
            if k >= 10 then C.hideText(big); C.hideText(small) end
            return k >= 10
        end, "banner fade")
    end)
    return big, small
end

-------------------------------------------------------------------------------
-- 演出
-------------------------------------------------------------------------------
local SE = {
    win = "/Game/WwiseAudio/Events/05_Facility/OneShot/Facil_OneShot_GambleWin.Facil_OneShot_GambleWin",
    lose = "/Game/WwiseAudio/Events/05_Facility/OneShot/Facil_OneShot_GambleLose.Facil_OneShot_GambleLose",
    money = "/Game/WwiseAudio/Events/00_Core/UI/Core_UI_GambleMoney.Core_UI_GambleMoney",
    zawa = "/Game/WwiseAudio/Events/00_Core/UI/Core_UI_GambleZawazawa.Core_UI_GambleZawazawa",
    dice = "/Game/WwiseAudio/Events/05_Facility/OneShot/Facil_OneShot_DiceMove.Facil_OneShot_DiceMove",
}
local VFX = {
    win = "/Game/Men/VFX/Niagara/NS_Men_Gambling_Win.NS_Men_Gambling_Win",
    lose = "/Game/Men/VFX/Niagara/NS_Men_Gambling_Lose.NS_Men_Gambling_Lose",
}

function C.sound(name, loc)
    local ev = C.load(SE[name] or name)
    if not ev then return end
    pcall(function()
        StaticFindObject("/Script/AkAudio.Default__AkGameplayStatics"):PostEventAtLocation(ev, loc or C.pawn():K2_GetActorLocation(),
            { Pitch = 0, Yaw = 0, Roll = 0 }, C.pawn())
    end)
end

function C.vfx(name, loc, scale)
    local s = scale or 1
    pcall(function()
        StaticFindObject("/Script/Niagara.Default__NiagaraFunctionLibrary"):SpawnSystemAtLocation(
            C.pawn(), C.load(VFX[name] or name), loc, { Pitch = 0, Yaw = 0, Roll = 0 }, { X = s, Y = s, Z = s }, true, true, 0, true)
    end)
end

-- 画面をその色で一瞬光らせる（strength 0〜1）
function C.flash(color, strength, sec)
    local pc = C.localPC()
    if not pc then return end
    local col = { R = color[1], G = color[2], B = color[3], A = 1 }
    pcall(function() pc.PlayerCameraManager:StartCameraFade(strength, 0, sec or 0.5, col, false, false) end)
end

-- 画面のゆれ（酔いとは別のカメラシェイクを借りる）
local SHAKE = "/Game/Bazaar_Meshingun/Environment/Misc/Cine/BP_Camerashake.BP_Camerashake_C"
local SHAKE_PAT = "/Game/Bazaar_Meshingun/Environment/Misc/Cine/BP_Camerashake.Default__BP_Camerashake_C:PerlinNoiseCameraShakePattern_0"
local shakeReady = false
function C.shake(strength, sec)
    local pc = C.localPC()
    if not pc then return end
    local cls = C.load(SHAKE)
    if not cls then return end
    if not shakeReady then
        local pat = StaticFindObject(SHAKE_PAT)
        if pat and pat:IsValid() then
            pat.BlendInTime = 0.05
            pat.BlendOutTime = 0.3
            pat.LocationAmplitudeMultiplier = 1
            pat.LocationFrequencyMultiplier = 1
            pat.X = { Amplitude = 4, Frequency = 18 }
            pat.Y = { Amplitude = 4, Frequency = 18 }
            pat.Z = { Amplitude = 4, Frequency = 18 }
            pat.RotationAmplitudeMultiplier = 1
            pat.Pitch = { Amplitude = 2, Frequency = 15 }
            pat.Yaw = { Amplitude = 2, Frequency = 15 }
            pat.Roll = { Amplitude = 3, Frequency = 12 }
            pat.FOV = { Amplitude = 3, Frequency = 10 }
        end
        shakeReady = true
    end
    pcall(function()
        local pat = StaticFindObject(SHAKE_PAT)
        if pat and pat:IsValid() then pat.Duration = sec or 0.6 end
        pc.PlayerCameraManager:StartCameraShake(cls, strength, 0, { Pitch = 0, Yaw = 0, Roll = 0 })
    end)
end

-- スローモーション（一人で遊んでいるときだけ。みんなの時間も遅くなるため）
function C.slowmo(rate, sec)
    if C.playerCount() > 1 then return end
    local pc = C.localPC()
    -- いまの速さ（RamenBenri で変えていることがある）を覚えて、あとで戻す
    local before = 1.0
    pcall(function() before = FindFirstOf("WorldSettings").TimeDilation end)
    pcall(function() pc.CheatManager:Slomo(rate * before) end)
    -- 遅くなっている間は時間がのびるので、ゲームの秒で数える
    C.later(sec * rate, function() pcall(function() pc.CheatManager:Slomo(before) end) end)
end

-------------------------------------------------------------------------------
-- アイテムを出す（場所と勢い）
-------------------------------------------------------------------------------
function C.spawnItem(itemId, loc, impulse)
    local sub = FindFirstOf("MenItemSpawnSubsystem")
    if not sub or not sub:IsValid() then return nil end
    local tf = { Rotation = quat(0, math.random(0, 359), 0), Translation = loc, Scale3D = { X = 1, Y = 1, Z = 1 } }
    local ok, a = pcall(function() return sub:SpawnItem({ TagName = FName(itemId) }, tf) end)
    if not ok or not a or not a:IsValid() then return nil end
    if impulse then
        pcall(function()
            local root = a:K2_GetRootComponent()
            if root:IsSimulatingPhysics(FName("None")) then root:AddImpulse(impulse, FName("None"), true) end
        end)
    end
    return a
end

return C
