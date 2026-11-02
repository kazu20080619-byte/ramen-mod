-- おみくじの設定。書きかえたら、ゲームを起動しなおすと反映されます。
return {
    price = 5,        -- 1 回のお金（$）
    share = true,     -- 大吉・大凶を、ほかのプレイヤー（同じ MOD を入れている人）にもどデカく見せる

    -- 確率（1000 回あたりの回数）
    rates = { daikichi = 50, chukichi = 100, shokichi = 150, kichi = 200, suekichi = 200, kyo = 200, daikyo = 100 },

    -- パソコンで売る「おみくじ箱」の値段（$）。モップの飾りを借りて売っています
    shopPrice = 30,

    -- キー（UE4SS の Key の名前）。use＝使う、rotL/rotR＝向き、small/big＝大きさ
    keys = { use = "E", rotL = "OEM_FOUR", rotR = "OEM_SIX", small = "OEM_MINUS", big = "OEM_PLUS" },

    -- 演出（true で使う・false で止める）
    fx = {
        flash = true,
        shake = true,
        slowmo = true,
        presign = true,
        fireworks = true,
        sound = true,
    },
}
