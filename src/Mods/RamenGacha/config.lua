-- ガチャの設定。書きかえたら、ゲームを起動しなおすと反映されます。
return {
    price = 10,       -- 1 回のお金（$）
    price10 = 100,    -- 10 連のお金（$）
    ceiling = 100,    -- 天井: この回数「神」が出なければ、次で「神」確定
    share = true,     -- 極・神を、ほかのプレイヤー（同じ MOD を入れている人）にもどデカく見せる

    -- 確率（1000 回あたりの回数）
    rates = { nami = 550, jo = 300, tokujo = 100, kiwami = 40, kami = 10 },

    rainCount = 20,   -- 極: 具材の雨の数
    ramenCount = 8,   -- 神: ラーメンの雨の数

    -- パソコンで売る「ガチャガチャ」の値段（$）。清掃中看板を借りて売っています
    shopPrice = 50,

    -- キー（UE4SS の Key の名前）。use＝使う、rotL/rotR＝向き、small/big＝大きさ
    keys = { use = "E", rotL = "OEM_FOUR", rotR = "OEM_SIX", small = "OEM_MINUS", big = "OEM_PLUS" },

    -- 演出（true で使う・false で止める）
    fx = {
        flash = true,     -- 画面が光る
        shake = true,     -- 画面がゆれる
        slowmo = true,    -- いい結果の瞬間スローモーション（一人のときだけ）
        presign = true,   -- 前触れ（確定演出）
        fireworks = true, -- キラキラ・お札の雨
        sound = true,     -- 音
    },
}
