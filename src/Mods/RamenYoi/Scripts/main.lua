-- RamenYoi: カウンターでビールを飲むと酔っ払う。飲みすぎると倒れてリスポーン。
-- 醤油一気飲み（RamenEmotes のエモート）でも倒れる。

local C = require("ramen_common")

-- 調整できる数（酔いは 0〜100）。Mods/RamenYoi/config.lua で変えられる
local CFG = C.config("RamenYoi", {
    beer          = true,  -- カウンターでビールを飲むと酔う
    shoyu         = true,  -- 醤油一気飲み（エモート）で倒れる
    passOutOn     = true,  -- 飲みすぎると倒れる（false なら、酔うだけで倒れない）
    strength      = 1.0,   -- ゆれ・かたむき・ふらつきの強さ（0.5 で半分、2 で 2 倍、0 でゆれない）
    beerSip       = 25,    -- ビールを 1 口飲むと増える酔い
    noodleSober   = 3,     -- 麺を食べると減る酔い
    soupSober     = 5,     -- スープや水を飲むと減る酔い
    decayPerSec   = 0.4,   -- 何もしなくても 1 秒で減る酔い
    passOut       = 100,   -- この酔いで倒れる
    swayYaw       = 5.0,   -- 酔い 100 のときの、向きの左右のずれ（度）
    swayPitch     = 2.5,   -- 酔い 100 のときの、向きの上下のずれ（度）
    shakeRoll     = 12.0,  -- 酔い 100 のときの、画面のかたむき（度）
    shakeFov      = 8.0,   -- 酔い 100 のときの、視野の伸び縮み
    shakeYaw      = 3.0,   -- 酔い 100 のときの、画面のゆらゆら（左右）
    shakePitch    = 2.0,   -- 酔い 100 のときの、画面のゆらゆら（上下）
    stagger       = 2.5,   -- 酔い 100 のときの、歩くときの横ずれ（1 回あたり）
    shoyuDelay    = 2.0,   -- 醤油を飲んでから倒れるまでの秒
    respawnDark   = 3.0,   -- 倒れてから復活するまでの暗い時間（秒）
})

local TICK = 0.05
local AM = "/Game/Men/Animation/AnimationMontage/Player/"
-- 画面のゆれに借りるカメラシェイク（お店では使われていない素材のもの）
local SHAKE = "/Game/Neon_Point/Blueprints/BP_Camera_Shake_01.BP_Camera_Shake_01_C"
local SHAKE_PATTERN = "/Game/Neon_Point/Blueprints/BP_Camera_Shake_01.Default__BP_Camera_Shake_01_C:PerlinNoiseCameraShakePattern_0"

local yoi = 0
local t = 0
local dying = false
local spawnPos = nil
local prev = { sideActive = false, eating = 0 }
local applied = { yaw = 0, pitch = 0 }
local shake = nil       -- 動いているカメラシェイク
local shakeReady = false

local function say(msg) print("[RamenYoi] " .. msg .. "\n") end

-- チャット欄に、自分だけに見えるメッセージを出す
local function chat(msg)
    say(msg)
    local c = FindFirstOf("MenTextChatComponent")
    if c and c:IsValid() then
        pcall(function() c:ClientReceiveMessage({ SenderName = "酔い", Body = msg, SenderId = -1 }) end)
    end
end

local function localPC()
    for _, p in ipairs(FindAllOf("PlayerController") or {}) do
        if p:IsValid() and p:IsLocalController() then return p end
    end
end

local function playerPawn(pc)
    if pc and pc.Pawn:IsValid() then return pc.Pawn end
end

local function isA(obj, short)
    local ok, r = pcall(function() return obj:GetClass():GetFName():ToString():find(short) ~= nil end)
    return ok and r
end

local function load(path)
    local o = StaticFindObject(path)
    if o and o:IsValid() then return o end
    o = LoadAsset(path)
    if o and o:IsValid() then return o end
end

local function playDamage(p)
    local m = load(AM .. "AM_Men_043_FrontBigDamage.AM_Men_043_FrontBigDamage")
    if not m then return end
    pcall(function()
        local a = p.CharacterMeshComponent:GetAnimInstance()
        if a and a:IsValid() then a:Montage_Play(m, 0.8, 0, 0.0, true) end
    end)
end

local function fade(pc, from, to, sec, hold)
    pcall(function()
        pc.PlayerCameraManager:StartCameraFade(from, to, sec, { R = 0, G = 0, B = 0, A = 1 }, false, hold)
    end)
end

-- 待つ・くり返すは ゲームのスレッドで。UE4SS の LoopAsync / ExecuteWithDelay は Lua を別のスレッドで
-- 動かすので、フックなどと同時に動くと Lua のメモリがこわれてゲームが落ちる（古い UE4SS なら前のやり方）
local GAME_THREAD = type(LoopInGameThreadWithDelay) == "function"
    and type(ExecuteInGameThreadWithDelay) == "function"

local function later(sec, fn)
    local ms = math.max(1, math.floor(sec * 1000))
    if GAME_THREAD then ExecuteInGameThreadWithDelay(ms, fn)
    else ExecuteWithDelay(ms, function() ExecuteInGameThread(fn) end) end
end

local function gameLoop(ms, fn)
    if GAME_THREAD then LoopInGameThreadWithDelay(ms, fn)
    else LoopAsync(ms, function() ExecuteInGameThread(fn) return false end) end
end

-- カメラシェイクの形を「酔い」に作りかえる（ずっと続く・ゆっくり・かたむき多め）
local function prepareShake()
    if shakeReady then return true end
    local cls = load(SHAKE)
    local pat = StaticFindObject(SHAKE_PATTERN)
    if not cls or not pat or not pat:IsValid() then return false end
    pat.Duration = 0
    pat.BlendInTime = 1.0
    pat.BlendOutTime = 1.0
    pat.LocationAmplitudeMultiplier = 0
    pat.RotationAmplitudeMultiplier = 1
    pat.Pitch = { Amplitude = CFG.shakePitch, Frequency = 0.2 }
    pat.Yaw = { Amplitude = CFG.shakeYaw, Frequency = 0.15 }
    pat.Roll = { Amplitude = CFG.shakeRoll, Frequency = 0.12 }
    pat.FOV = { Amplitude = CFG.shakeFov, Frequency = 0.2 }
    shakeReady = true
    return true
end

local function setShake(pc, k)
    if k <= 0 then
        if shake then
            pcall(function() pc.PlayerCameraManager:StopCameraShake(shake, false) end)
            shake = nil
        end
        return
    end
    if not shake or not shake:IsValid() then
        if not prepareShake() then return end
        shake = pc.PlayerCameraManager:StartCameraShake(load(SHAKE), k, 0, { Pitch = 0, Yaw = 0, Roll = 0 })
    end
    if shake and shake:IsValid() then shake.ShakeScale = k end
end

-- 倒れて、お店に入ったときの場所で復活する
local function die(reason)
    if dying then return end
    local pc = localPC(); local p = playerPawn(pc)
    if not p then return end
    dying = true
    chat(reason)
    playDamage(p)
    later(0.8, function() fade(pc, 0, 1, 1.2, true) end)
    later(0.8 + CFG.respawnDark, function()
        local q = playerPawn(pc)
        if q and spawnPos then
            pcall(function() q:K2_SetActorLocation(spawnPos, false, {}, true) end)
        end
        yoi = 0
        setShake(pc, 0)
        fade(pc, 1, 0, 1.5, false)
        dying = false
        chat("…はっ！ 気がついたら店の前にいた")
    end)
end

-- カウンターで飲んだ・食べたを見る。座っていれば true
local function watchSeat(p)
    local seat
    pcall(function() seat = p.SeatUseComponent end)
    if not seat or not seat:IsValid() then return false end
    local seated = false
    pcall(function() seated = seat:IsSeatUseActive() end)

    local side = seat.SideMealState
    local active = side.bActive
    if active and not prev.sideActive then
        local a = side.Actor
        if a and a:IsValid() and isA(a, "BeerMug") then
            if not CFG.beer then prev.sideActive = active return seated end
            yoi = yoi + CFG.beerSip
            chat(string.format("ビールを飲んだ（酔い %d / %d）", math.floor(yoi), CFG.passOut))
        elseif a and a:IsValid() then
            yoi = math.max(0, yoi - CFG.soupSober)
            say("飲んだ: " .. a:GetClass():GetFName():ToString())
        end
    end
    prev.sideActive = active

    local eating = seat.ReplicatedSeatUseState.EatingPhase
    if eating ~= prev.eating then
        if eating == 1 then yoi = math.max(0, yoi - CFG.noodleSober) end
        if eating == 2 then yoi = math.max(0, yoi - CFG.soupSober) end
    end
    prev.eating = eating
    return seated
end

-- ゆれ・ふらつき
local function drunkEffects(pc, p, seated)
    local k = math.min(yoi, CFG.passOut) / CFG.passOut * math.max(0, CFG.strength)
    setShake(pc, k)

    -- 向きのずれ（前の回のずれを戻してから新しいずれを足すので、ずっとずれていかない）
    local yaw = math.sin(t * 0.9) * CFG.swayYaw * k
    local pitch = math.sin(t * 1.3 + 1.0) * CFG.swayPitch * k
    local r = pc:GetControlRotation()
    pc:SetControlRotation({
        Pitch = r.Pitch + (pitch - applied.pitch),
        Yaw = r.Yaw + (yaw - applied.yaw),
        Roll = r.Roll,
    })
    applied.yaw, applied.pitch = yaw, pitch

    -- 歩いているときだけ、横にふらつく（座っているときはしない）
    if not seated and k > 0.2 then
        local v = p:GetVelocity()
        local speed = math.sqrt(v.X * v.X + v.Y * v.Y)
        if speed > 50 then
            local s = math.sin(t * 1.7) * CFG.stagger * k
            pcall(function()
                p:K2_AddActorWorldOffset({ X = -v.Y / speed * s, Y = v.X / speed * s, Z = 0 }, true, {}, false)
            end)
        end
    end
end

local lastPawn = nil
local function tick()
    t = t + TICK
    local pc = localPC(); local p = playerPawn(pc)
    if not p then return end
    local addr = p:GetAddress()
    if addr ~= lastPawn then  -- お店に入りなおしたら、ここを復活の場所にする
        lastPawn = addr
        local l = p:K2_GetActorLocation()
        spawnPos = { X = l.X, Y = l.Y, Z = l.Z }
        applied.yaw, applied.pitch = 0, 0
        shake = nil
        yoi = 0
    end
    if dying then return end
    local seated = watchSeat(p)
    if yoi >= CFG.passOut then
        if not CFG.passOutOn then yoi = CFG.passOut  -- 倒れない設定: 酔いはここで止まる
        elseif not seated then die("飲みすぎて倒れた！") return end
    end
    yoi = math.max(0, yoi - CFG.decayPerSec * TICK)
    if yoi > 0 or applied.yaw ~= 0 or applied.pitch ~= 0 or shake then drunkEffects(pc, p, seated) end
end

local errors = 0
gameLoop(math.floor(TICK * 1000), function()
    local ok, err = pcall(tick)
    if not ok and errors < 5 then errors = errors + 1; say("error: " .. tostring(err)) end
end)

-- 醤油一気飲み（エモート）→ 少しして倒れる
RegisterHook("/Script/RealRamenSimulator.MenEmoteComponent:RequestPlayEmote", function(self, tagParam)
    if not CFG.shoyu or tagParam:get().TagName:ToString() ~= "Men.Emote.Mod.ShoyuChug" then return end
    later(CFG.shoyuDelay, function() die("醤油を一気飲みして倒れた！") end)
end)

-- コンソール: 「yoi」で今の酔い、「yoi 80」で酔いを 80 にする（ためし用）
RegisterConsoleCommandHandler("yoi", function(full, params, ar)
    local n = tonumber(params[1])
    if n then yoi = n end
    ar:Log(string.format("酔い %d / %d", math.floor(yoi), CFG.passOut))
    return true
end)

say("loaded")
