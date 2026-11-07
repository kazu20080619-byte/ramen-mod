-- RamenEmotes: ゲームにある動きを使って、エモートを増やす
local AM = "/Game/Men/Animation/AnimationMontage/Player/"

-- tag: 他と重ならない名前 / anim: 流す動き / loops: くり返す回数（なければ 1）
-- icon: もとからあるエモートのアイコンを借りる / body: 0=全身 1=上半身
local EMOTES = {
    { tag = "WipeTable",  name = "テーブル拭き",   anim = "AM_Men_025_WipeTable",        loops = 3, icon = "Udekumi",   body = 0 },
    { tag = "Mopping",    name = "モップがけ",     anim = "AM_Men_029_Mopping",          loops = 3, icon = "Udekumi",   body = 0 },
    { tag = "WashHands",  name = "手洗い",         anim = "AM_Men_033_WashHands",        loops = 2, icon = "Greeting1", body = 0 },
    { tag = "PeelEgg",    name = "ゆで卵むき",     anim = "AM_Men_093_PeelBoiledEgg",    loops = 3, icon = "Clap",      body = 1 },
    { tag = "AddShoyu",   name = "醤油さし",       anim = "AM_Men_064_AddShoyu",         loops = 2, icon = "Yugiri",    body = 1 },
    { tag = "Shuriken",   name = "手裏剣",         anim = "AM_Men_056_ThrowShuriken_TP", loops = 3, icon = "Punch",     body = 1 },
    { tag = "Slurp",      name = "ラーメンをすする", anim = "AM_Men_006_RamenEating",    icon = "Charumera", body = 0 },
    { tag = "DrinkSoup",  name = "スープを飲む",   anim = "AM_Men_003_DrinkSoup",        icon = "Charumera", body = 0 },
    { tag = "Yarareta",   name = "やられた〜",     anim = "AM_Men_043_FrontBigDamage",   icon = "Dogeza",    body = 0 },
    { tag = "Unko",       name = "うんこ",         anim = "AM_Men_071_Unko",             icon = "Dogeza",    body = 0 },
    { tag = "ShoyuChug",  name = "醤油一気飲み",   anim = "AM_Men_003_DrinkSoup",        icon = "Charumera", body = 0 },  -- RamenYoi で倒れる
}

-- RamenGacha があるときは、ガチャで出たエモートだけ使える（「ramenemote sync」で教えてもらう）。
-- 使えないエモートは、解放レベルを LOCKED_LEVEL にしておく。
-- config.lua の gachaOnly = false なら、ガチャがあっても最初から全部使える。
local LOCKED_LEVEL = 999
local C = require("ramen_common")
local CFG = C.config("RamenEmotes", {
    gachaOnly = true,   -- 新しいエモートは、ガチャで出たものだけ使える
})

local DEBUG = false -- true にすると、ログにくわしく出す
local TAG_PREFIX = "Men.Emote.Mod."
local byTag = {}
local added = false

local function iconPath(n)
    return string.format("/Game/Men/UI/Emote/T_Men_IconEmote%s_UI.T_Men_IconEmote%s_UI", n, n)
end

local function load(path)
    local o = StaticFindObject(path)
    if o and o:IsValid() then return o end
    o = LoadAsset(path)
    if o and o:IsValid() then return o end
    return nil
end

local function montage(name)
    return load(AM .. name .. "." .. name)
end

-- ロッカー（カスタマイズのエモート画面）は「動き（ThirdPersonMontage）」が空のエモートを候補から外す。
-- Lua で動きを直接入れるとゲームが落ちるので、ゲームのエモートの設定を そのまま写す（値の写しは落ちない）。
-- 実際の動きは、下の RequestPlayEmote のフックで自分の動きに入れかえる。
local function copyMontage(entries, dst, iconName)
    local src
    for i = 1, #entries do
        local t = entries[i].EmoteTag.TagName:ToString()
        if t:find(iconName, 1, true) and not t:find("%.Mod%.") then src = entries[i] break end
    end
    if not src then
        for i = 1, #entries do
            if not entries[i].EmoteTag.TagName:ToString():find("%.Mod%.") then src = entries[i] break end
        end
    end
    if not src then return end
    pcall(function() dst.ThirdPersonMontage = src.ThirdPersonMontage end)
    pcall(function() dst.FirstPersonMontage = src.FirstPersonMontage end)
end

local function addEntries(ca)
    local entries = ca.Entries
    local have = {}
    for i = 1, #entries do have[entries[i].EmoteTag.TagName:ToString()] = true end
    for i, e in ipairs(EMOTES) do
        local tag = TAG_PREFIX .. e.tag
        byTag[tag] = e
        if not have[tag] then
            entries[#entries + 1] = {
                EmoteTag = { TagName = FName(tag) },
                DisplayName = FText(e.name),
                Icon = load(iconPath(e.icon)),
                UnlockLevel = 1,
                SortOrder = 200 + i,
                BodyMode = e.body,
                PlayRate = 1.0,
                BlendOutTimeSeconds = 0.15,
            }
            copyMontage(entries, entries[#entries], e.icon)
        end
    end
    print(string.format("[RamenEmotes] catalog now has %d emotes\n", #entries))
end

-- ゲームのスレッドでくり返す。fn が true を返したら止まる。
-- UE4SS の LoopAsync は Lua を別のスレッドで動かすので、フックなどと同時に動くと
-- Lua のメモリがこわれてゲームが落ちる。古い UE4SS で …InGameThread… がないときだけ前のやり方
local GAME_THREAD = type(LoopInGameThreadWithDelay) == "function" and type(CancelDelayedAction) == "function"
local function gameLoop(ms, fn)
    local stopped, handle = false, nil
    local function step()
        if stopped then return end
        local ok, r = pcall(fn)
        if not ok then print("[RamenEmotes] error: " .. tostring(r) .. "\n")
        elseif r then stopped = true; if handle then CancelDelayedAction(handle) end end
    end
    if GAME_THREAD then
        handle = LoopInGameThreadWithDelay(ms, step)
    else
        LoopAsync(ms, function() if stopped then return true end ExecuteInGameThread(step) return false end)
    end
end

-- カタログが読み込まれるまで待って足す
gameLoop(2000, function()
    if added then return true end
    local ca = FindFirstOf("MenEmoteCatalogDataAsset")
    if ca and ca:IsValid() then
        added = true
        addEntries(ca)
        return true
    end
end)

-- いま流している新しいエモート（持ち主ごと）
-- { comp, anim, montage, tag, left = あと何回 }
local playing = {}

local function stopMod(owner)
    local key = owner:GetAddress()
    local p = playing[key]
    if p then
        playing[key] = nil
        pcall(function() p.anim:Montage_Stop(0.2, p.montage) end)
    end
end

-- ゲームのエモートは、カタログの動き（ソフト参照）が空だと止まってしまう。
-- そこで、動きは体に直接流して、エモート中の印（ActiveMontage と ReplicatedPlayback）を
-- 自分で立てる。印が立つと、ゲームがエモート用のカメラに切りかえる。
local function startMod(comp, owner, tag, e, left)
    local mesh = owner.CharacterMeshComponent
    local anim = mesh:GetAnimInstance()
    local m = montage(e.anim)
    if not anim or not anim:IsValid() then return end
    if not m then print("[RamenEmotes] montage not found: " .. e.anim .. "\n") return end

    local pb = comp.ReplicatedPlayback
    comp.ActiveMontage = m
    comp.ReplicatedPlayback = {
        EmoteTag = { TagName = FName(tag) },
        BodyMode = e.body,
        Serial = (pb.Serial + 1) % 65536,
        ServerStartTimeSeconds = pb.ServerStartTimeSeconds,
    }
    pcall(function() comp:OnRep_ReplicatedPlayback() end)
    -- OnRep のあとで流す（OnRep は今の動きを止めてしまう）
    anim:Montage_Play(m, 1.0, 0, 0.0, true)
    playing[owner:GetAddress()] = {
        comp = comp, owner = owner, anim = anim, montage = m, tag = tag, e = e,
        left = left or ((e.loops or 1) - 1),
        length = m:GetPlayLength() * 0.9, elapsed = 0,
    }
end

RegisterHook("/Script/RealRamenSimulator.MenEmoteComponent:RequestPlayEmote", function(self, tagParam)
    local comp = self:get()
    local owner = comp:GetOwner()
    if not owner or not owner:IsValid() then return end
    local tag = tagParam:get().TagName:ToString()
    local e = byTag[tag]
    if e and e.locked then e = nil end  -- まだガチャで出ていない
    if DEBUG then print(string.format("[RamenEmotes] play %s (mod=%s)\n", tag, tostring(e ~= nil))) end
    ExecuteInGameThread(function()
        stopMod(owner)
        if e then
            local ok, err = pcall(startMod, comp, owner, tag, e)
            if not ok then print("[RamenEmotes] error: " .. tostring(err) .. "\n") end
        end
    end)
end)

RegisterHook("/Script/RealRamenSimulator.MenEmoteComponent:RequestStopEmote", function(self)
    local owner = self:get():GetOwner()
    if owner and owner:IsValid() then
        ExecuteInGameThread(function() stopMod(owner) end)
    end
end)

-- 動きが終わったら、もう一度流すか、エモートを終わらせる。
-- ゲームは動きが終わると自分でエモートの印を消すので、最後まで流れたかは経った時間で見る。
gameLoop(150, function()
    if next(playing) == nil then return end
    for key, p in pairs(playing) do
        local ok, tag = pcall(function() return p.comp.ReplicatedPlayback.EmoteTag.TagName:ToString() end)
        p.elapsed = p.elapsed + 0.15
        local finished = p.elapsed >= p.length
        if not ok then
            playing[key] = nil
        elseif tag ~= p.tag and tag ~= "None" then
            playing[key] = nil  -- ほかのエモートに変わった
        elseif not p.anim:Montage_IsPlaying(p.montage) or tag == "None" then
            if DEBUG then print(string.format("[RamenEmotes] end: tag=%s playing=%s elapsed=%.2f left=%d\n", tag, tostring(p.anim:Montage_IsPlaying(p.montage)), p.elapsed, p.left)) end
            playing[key] = nil
            if finished and p.left > 0 then
                local ok2, err = pcall(startMod, p.comp, p.owner, p.tag, p.e, p.left - 1)
                if not ok2 then print("[RamenEmotes] error: " .. tostring(err) .. "\n") end
            elseif tag == p.tag then
                p.comp:RequestStopEmote()
            end
        end
    end
end)

-- コンソール「ramenemote sync タグ,タグ,…」: 並べたエモートだけ使えるようにする（「-」なら全部しまう）。
-- 「ramenemote all」で全部使えるようにもどす。
RegisterConsoleCommandHandler("ramenemote", function(full, params, ar)
    local cmd, arg = params[1], params[2]
    if cmd == "sync" and not CFG.gachaOnly then cmd = "all" end  -- 全部使える設定
    ExecuteInGameThread(function()
        local ca = FindFirstOf("MenEmoteCatalogDataAsset")
        if not ca or not ca:IsValid() then return end
        local owned = {}
        if cmd == "sync" and arg and arg ~= "-" then
            for t in arg:gmatch("[^,]+") do owned[t] = true end
        end
        for i = 1, #ca.Entries do
            local entry = ca.Entries[i]
            local tag = entry.EmoteTag.TagName:ToString()
            local e = byTag[tag]
            if e then
                e.locked = (cmd == "sync") and not owned[tag]
                entry.UnlockLevel = e.locked and LOCKED_LEVEL or 1
            end
        end
    end)
    return true
end)

print("[RamenEmotes] loaded\n")
