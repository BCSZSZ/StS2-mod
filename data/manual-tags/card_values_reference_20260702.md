# 卡牌价值参考表 · Card Value Reference — 2026-07-02

来源文件：`CardValueOverlay/data/card_values.json`（运行时覆盖层数值）。英/中卡名取自 `history-analysis/data/localized_names_en_zhs.json`（从 SlayTheSpire2.pck 提取的官方本地化，1872 条）。

数值 = **play-delta（边际 ΔEV，每次直接打出的价值）**，来自 2026-07-02 全牌组重估。分 short(4 回合) / mid(8 回合) / long(14 回合) 三个时间跨度，按“未升级 mid”从高到低排序。负值属正常：前期铺垫/稀释成本，靠后回合或联动才回本。

> 数值只在同一口径内可比。以下 **主表 127 张** 都是 play-delta 口径；文末 **10 张**（不可模拟的卡）仍沿用旧的静态 layer-17 估值，口径不同，**不可与主表直接比较**。另有 2 张全 0 卡未列出：Alchemize（炼制药水）、Royalties（版税）。

## 主表 · play-delta 口径（127 张，按未升级 mid 降序）

| # | English | 中文 | 卡池 | 未升级·short(4) | 未升级·mid(8) | 未升级·long(14) | 升级·short(4) | 升级·mid(8) | 升级·long(14) |
|---:|---|---|---|---:|---:|---:|---:|---:|---:|
| 1 | Void Form | 虚空形态 | Regent | 4.5 | 79.7 | 228.9 | 3.2 | 67.2 | 211.2 |
| 2 | Rolling Boulder | 滚石 | Colorless | -8.5 | 75.8 | 381.7 | 2.1 | 106.9 | 449.1 |
| 3 | Comet | 彗星 | Regent | 38.1 | 41.5 | 41.5 | 49.8 | 54.2 | 54.6 |
| 4 | Decisions, Decisions | 抉择，抉择 | Regent | 33.5 | 39.7 | 48.2 | 43.7 | 51.7 | 62.3 |
| 5 | Big Bang | 大爆炸 | Regent | 23.5 | 39.2 | 60.1 | 25 | 39.8 | 58.7 |
| 6 | Spectrum Shift | 光谱偏移 | Regent | -3.2 | 35.2 | 82.3 | 7.1 | 48.1 | 96.9 |
| 7 | Heavenly Drill | 天际钻头 | Regent | 36.7 | 34.2 | 28 | 53.4 | 48.6 | 42.3 |
| 8 | Furnace | 熔炉 | Regent | -0.7 | 34.1 | 141.2 | 2.2 | 50.9 | 203.7 |
| 9 | Tyranny | 暴政 | Regent | -2.4 | 32.1 | 151.2 | 2.3 | 45.8 | 175.5 |
| 10 | Fasten | 勒紧 | Colorless | 3.6 | 31 | 74.1 | 10.9 | 51.3 | 115.3 |
| 11 | Black Hole | 黑洞 | Regent | 5 | 30.5 | 67.7 | 9.2 | 41.7 | 87.1 |
| 12 | Meteor Shower | 流星雨 | Regent | 29.6 | 30 | 29.4 | 39.8 | 40.5 | 40.4 |
| 13 | Orbit | 环绕轨道 | Regent | -6.2 | 29.7 | 102.9 | 5.6 | 45.1 | 121.9 |
| 14 | Seeking Edge | 追踪之刃 | Regent | 5 | 27.5 | 69.5 | 11.1 | 40.2 | 92.2 |
| 15 | Panache | 神气制胜 | Colorless | 7.1 | 26.2 | 54.8 | 9.9 | 33.8 | 69.4 |
| 16 | Automation | 自动化 | Colorless | -1.8 | 25.2 | 76.1 | 6.2 | 34.3 | 86.1 |
| 17 | The Bomb | 炸弹 | Colorless | 13 | 24.7 | 29.6 | 18.5 | 32.7 | 38.5 |
| 18 | Dramatic Entrance | 闪亮登场 | Colorless | 17.1 | 24.5 | 36.1 | 23.1 | 30.7 | 42.7 |
| 19 | Restlessness | 心神不宁 | Colorless | 25.2 | 24.4 | 23.5 | 36.3 | 35.9 | 36.3 |
| 20 | Master of Strategy | 战略大师 | Colorless | 14.8 | 23.6 | 36.6 | 18.8 | 28.9 | 43.2 |
| 21 | The Smith | 铸剑者 | Regent | 7.8 | 23.1 | 55.1 | 17.7 | 39.9 | 87 |
| 22 | Eternal Armor | 永恒铠甲 | Colorless | 1.4 | 22.5 | 36.7 | 11.7 | 45.4 | 77.2 |
| 23 | Calamity | 劫难 | Colorless | -11.5 | 22.1 | 46.7 | 0.2 | 43.2 | 72.4 |
| 24 | Prep Time | 准备时间 | Colorless | -0.2 | 21.8 | 57.3 | 3.4 | 33.9 | 81.6 |
| 25 | Shockwave | 震荡波 | Colorless | 7.6 | 21.2 | 32.2 | 7.8 | 34.1 | 50.9 |
| 26 | The Sealed Throne | 封印王座 | Regent | -0.6 | 21.1 | 58.8 | 8 | 30.1 | 69.3 |
| 27 | Scrawl | 潦草急就 | Colorless | 13.8 | 20.7 | 30.1 | 13.5 | 12.5 | 13.9 |
| 28 | Bundle of Joy | 新生之喜 | Regent | 10.7 | 20.5 | 27.9 | 10.7 | 20.5 | 27.9 |
| 29 | Child of the Stars | 群星之子 | Regent | 1.8 | 20.5 | 46.4 | 6.4 | 31.9 | 66.2 |
| 30 | Entropy | 熵 | Colorless | -2 | 20.1 | 50.8 | 2.9 | 26.2 | 50.1 |
| 31 | Production | 生产制造 | Colorless | 10.3 | 19.1 | 32.3 | 12.5 | 21.5 | 35.5 |
| 32 | CHARGE!! | 冲锋！！ | Regent | 15.3 | 18.4 | 20.2 | 17.4 | 21.8 | 23.5 |
| 33 | Gamma Blast | 伽马爆破 | Regent | 19 | 18.4 | 16.5 | 24.3 | 24 | 22.4 |
| 34 | Prowess | 非凡技艺 | Colorless | -0.1 | 18.3 | 47.3 | 8.1 | 40.2 | 90.2 |
| 35 | Dark Shackles | 黑暗镣铐 | Colorless | 11.2 | 18 | 29.2 | 18.4 | 24.8 | 36.1 |
| 36 | Hidden Gem | 未掘宝石 | Colorless | 6.2 | 18 | 38 | 11.5 | 28.3 | 59 |
| 37 | Know Thy Place | 何人僭越 | Regent | 10.6 | 18 | 28.9 | 9.8 | 10.8 | 11.3 |
| 38 | Royal Gamble | 胜券在王 | Regent | 8.4 | 18 | 33.6 | 7.5 | 15.6 | 22.6 |
| 39 | Arsenal | 武器库 | Regent | -4.1 | 17.9 | 79.9 | -0.2 | 28.9 | 101 |
| 40 | Knockout Blow | 决胜一击 | Regent | 16.1 | 17.3 | 16.7 | 23.9 | 24.3 | 23.1 |
| 41 | Crash Landing | 迫降 | Regent | 29.4 | 16.7 | -2.7 | 36.8 | 24.1 | 4.7 |
| 42 | Pillar of Creation | 创世之柱 | Regent | 2.4 | 16 | 37.3 | 5.7 | 22.5 | 48.2 |
| 43 | Sword Sage | 剑圣 | Regent | -8.7 | 14.9 | 70.3 | 2.7 | 28.8 | 86.4 |
| 44 | Thinking Ahead | 深谋远虑 | Colorless | 6.5 | 14.5 | 26.8 | 1.8 | 2 | 2.1 |
| 45 | Catastrophe | 横祸 | Colorless | 11 | 13.9 | 16.1 | 19.5 | 23.2 | 26.5 |
| 46 | Secret Technique | 秘密技法 | Colorless | 6.1 | 13.8 | 24.4 | 5.4 | 7 | 7.8 |
| 47 | Secret Weapon | 秘密武器 | Colorless | 6.1 | 13.8 | 24.4 | 5.4 | 7 | 7.8 |
| 48 | Manifest Authority | 君权自授 | Regent | 12 | 13.6 | 13.1 | 18.3 | 22.1 | 24 |
| 49 | Bulwark | 铸墙 | Regent | 8.8 | 13.3 | 22 | 13.4 | 19.8 | 31.7 |
| 50 | Refine Blade | 淬炼刀刃 | Regent | 7.8 | 13.2 | 24 | 11.8 | 19.8 | 35.5 |
| 51 | Hegemony | 霸权 | Regent | 11.7 | 12.9 | 13.4 | 17.2 | 19 | 20.4 |
| 52 | Stratagem | 计策 | Colorless | -3.4 | 12.8 | 47 | 4.8 | 22.6 | 58 |
| 53 | Summon Forth | 征召上前 | Regent | 2.4 | 12.7 | 26 | 6.2 | 19.3 | 36.8 |
| 54 | Reflect | 倒映 | Regent | 15.8 | 12.1 | 8.4 | 26.6 | 22.8 | 19.5 |
| 55 | Gold Axe | 金斧 | Colorless | 3.5 | 11.9 | 22.8 | 3.3 | 8.8 | 18.1 |
| 56 | Genesis | 创世纪 | Regent | -12 | 11.7 | 49.5 | -11.3 | 13 | 51.1 |
| 57 | Heirloom Hammer | 传承之锤 | Regent | 11.4 | 11.7 | 11 | 16.1 | 15.6 | 14.6 |
| 58 | Collision Course | 碰撞轨迹 | Regent | 13.3 | 11.6 | 7.8 | 17.8 | 16.2 | 12.3 |
| 59 | Neutron Aegis | 中子护盾 | Regent | -1.4 | 11.5 | 21.7 | 8.6 | 32.6 | 56.8 |
| 60 | Flash of Steel | 亮剑 | Colorless | 10.6 | 11.3 | 11.8 | 14.2 | 15 | 15.4 |
| 61 | Finesse | 妙计 | Colorless | 10.5 | 11 | 11.4 | 14.3 | 15.1 | 15.6 |
| 62 | Impatience | 急躁 | Colorless | 9.1 | 10.8 | 11.6 | 12.9 | 14.9 | 15.8 |
| 63 | Parry | 招架 | Regent | -1.6 | 10.5 | 31.5 | 1.2 | 17.1 | 42.9 |
| 64 | Kingly Kick | 王者之踢 | Regent | 5.6 | 10.4 | 5.7 | 12.4 | 13.1 | 9.8 |
| 65 | Wrought in War | 战火铸就 | Regent | 7 | 10.3 | 16.2 | 11.1 | 15.3 | 23.8 |
| 66 | Bombardment | 轰击 | Regent | 6.6 | 10 | 14.1 | 9.7 | 12.9 | 19.7 |
| 67 | Falling Star | 陨星 | Regent | 12.3 | 10 | 8 | 15.3 | 13.2 | 11.2 |
| 68 | Beat Down | 狠揍 | Colorless | 7.2 | 9.8 | 11.9 | 9.1 | 11.5 | 14.3 |
| 69 | Lunar Blast | 月面射击 | Regent | 9.5 | 9.8 | 9.8 | 12 | 12.3 | 12.4 |
| 70 | Thrumming Hatchet | 无休手斧 | Colorless | 9.8 | 9.7 | 9.4 | 11.1 | 11.1 | 10.9 |
| 71 | Salvo | 箭雨 | Colorless | 8.9 | 9.3 | 9.2 | 13.2 | 13.6 | 13.2 |
| 72 | Ultimate Strike | 究极打击 | Colorless | 9.7 | 9.2 | 8.5 | 15.7 | 14.9 | 13.8 |
| 73 | Omnislice | 万向斩 | Colorless | 9.2 | 9.1 | 9.1 | 12.5 | 12.3 | 12.4 |
| 74 | Hand of Greed | 贪婪之手 | Colorless | 10 | 9 | 7.8 | 14.1 | 12.8 | 10.9 |
| 75 | Seeker Strike | 探寻打击 | Colorless | 7.5 | 8.8 | 8.7 | 10.7 | 11.7 | 11.6 |
| 76 | Devastate | 葬送 | Regent | 12.9 | 8.7 | 5.4 | 22.6 | 18 | 14.7 |
| 77 | Convergence | 汇流 | Regent | 3.9 | 8.1 | 11 | 5.4 | 10.6 | 13.3 |
| 78 | Gather Light | 收集光辉 | Regent | 6.6 | 7.9 | 7.6 | 10.2 | 11.3 | 10.9 |
| 79 | Radiate | 辐射 | Regent | 9.3 | 7.9 | 6.9 | 12.5 | 10.6 | 9.2 |
| 80 | Solar Strike | 太阳打击 | Regent | 6.5 | 7.9 | 8.2 | 8.9 | 10.8 | 11 |
| 81 | Spoils of Battle | 战利品 | Regent | 3.8 | 7.8 | 13.8 | 5.5 | 12.3 | 21.5 |
| 82 | Ultimate Defend | 究极防御 | Colorless | 8.1 | 7.8 | 6.7 | 12.8 | 12.1 | 10.7 |
| 83 | GUARDS!!! | 护驾！！！ | Regent | -0.6 | 7.4 | 18 | 17.6 | 32.1 | 56.6 |
| 84 | Prolong | 延伸 | Colorless | 0.5 | 7.1 | 17.5 | 0 | -0.1 | -0.2 |
| 85 | Jack of All Trades | 花样百出 | Colorless | 0.4 | 6.9 | 18 | 0.4 | 6.9 | 18 |
| 86 | Make It So | 如此甚好 | Regent | 7 | 6.9 | 7 | 10.3 | 10.2 | 10.2 |
| 87 | Crescent Spear | 新月长矛 | Regent | 8.8 | 6.7 | 4.6 | 12.3 | 10.3 | 8 |
| 88 | I Am Invincible | 所向无敌 | Regent | 6.9 | 6.7 | 5.9 | 10.2 | 9.7 | 8.4 |
| 89 | Cosmic Indifference | 宇宙冷漠 | Regent | 7.9 | 6.5 | 6.2 | 7.1 | 7.2 | 7.4 |
| 90 | Patter | 星星点点 | Regent | 6.3 | 6.2 | 5.3 | 9.6 | 9.1 | 7.6 |
| 91 | Supermassive | 超质量体 | Regent | 3.8 | 6.1 | 8.7 | 4.8 | 7.9 | 11.5 |
| 92 | BEGONE! | 下去！ | Regent | 4.3 | 5.9 | 8.8 | 7.5 | 9.2 | 11.8 |
| 93 | Equilibrium | 均衡 | Colorless | 5.3 | 5.9 | 5.4 | 8.7 | 9.2 | 8.7 |
| 94 | Glitterstream | 流光溢彩 | Regent | 5.8 | 5.9 | 5.3 | 9.7 | 9.8 | 9 |
| 95 | Cloak of Stars | 群星斗篷 | Regent | 7.3 | 5.8 | 4.2 | 11.1 | 9.6 | 8.1 |
| 96 | Glow | 辉光 | Regent | 2.5 | 5.8 | 7.7 | 4.2 | 8.1 | 10.1 |
| 97 | Mind Blast | 心灵震慑 | Colorless | 9.2 | 5.6 | 4.4 | 15.1 | 11.8 | 10.7 |
| 98 | Astral Pulse | 星界脉冲 | Regent | 8.1 | 5.4 | 3.2 | 12.6 | 10.1 | 8.4 |
| 99 | Crush Under | 下砸 | Regent | 5.6 | 5.4 | 4.9 | 8 | 7.7 | 7.1 |
| 100 | Conqueror | 征服者 | Regent | 2.9 | 5 | 8.5 | 5.6 | 9.1 | 15.6 |
| 101 | Stardust | 星尘 | Regent | 4.8 | 4.8 | 4.8 | 6.5 | 6.4 | 6.4 |
| 102 | Beat into Shape | 锻打成型 | Regent | 3.5 | 4.7 | 6.6 | 5 | 6.8 | 9.6 |
| 103 | Volley | 连射 | Colorless | 4.9 | 4.5 | 4.6 | 12.8 | 11.9 | 10.8 |
| 104 | Pale Blue Dot | 暗淡蓝点 | Regent | -6.2 | 4.4 | 21.9 | -5.2 | 11.3 | 34.5 |
| 105 | Guiding Star | 引导之星 | Regent | 6.3 | 4.3 | 3 | 7.9 | 6.6 | 4.9 |
| 106 | Shining Strike | 明耀打击 | Regent | 3.4 | 3.8 | 3.7 | 6.2 | 6.1 | 5.4 |
| 107 | Strike | 打击 | Regent | 3.6 | 3.5 | 3.1 | 4.4 | 4.2 | 3.6 |
| 108 | Particle Wall | 粒子墙 | Regent | 6.7 | 3.4 | 1.1 | 10 | 7 | 5 |
| 109 | Terraforming | 地形改造 | Regent | 3.4 | 3.4 | 2.3 | 3.6 | 3.3 | 2.7 |
| 110 | Bolas | 流星锤 | Colorless | 3.3 | 3.3 | 3.3 | 4.6 | 4.6 | 4.7 |
| 111 | Kingly Punch | 王者之拳 | Regent | 3.3 | 3.3 | 2.7 | 5.6 | 5.5 | 4.9 |
| 112 | Hidden Cache | 隐秘藏品 | Regent | -0.6 | 3 | 3.9 | -0.5 | 3.1 | 3.6 |
| 113 | Photon Cut | 光子切割 | Regent | 3.7 | 2.8 | 2 | 8.7 | 8.1 | 7.2 |
| 114 | Purity | 净化 | Colorless | -1.4 | 2.8 | 12 | -1.7 | 2.2 | 10.6 |
| 115 | Fisticuffs | 拳斗 | Colorless | 2.6 | 2.6 | 2.1 | 4.4 | 4.2 | 3.6 |
| 116 | Defend | 防御 | Regent | 2.2 | 2.2 | 1.6 | 4.6 | 4.5 | 3.8 |
| 117 | Foregone Conclusion | 既定事项 | Regent | 1 | 2.2 | 2.7 | 1.2 | 3.1 | 3.9 |
| 118 | Venerate | 崇拜 | Regent | -0.9 | 1.6 | 2.6 | -0.4 | 2.7 | 3.8 |
| 119 | Celestial Might | 天穹之力 | Regent | 2 | 1.5 | 0.8 | 2 | 1.5 | 0.8 |
| 120 | Monologue | 独白 | Regent | 0.2 | 0.2 | -0.1 | -0.5 | -3 | -4.6 |
| 121 | Quasar | 类星体 | Regent | 1.1 | -0.1 | -3.2 | 8.6 | 10.8 | 12.1 |
| 122 | Glimmer | 微光 | Regent | -2.6 | -2.1 | -2 | -0.5 | 0 | 0 |
| 123 | Prophesize | 预言 | Regent | -2.4 | -2.2 | -2.4 | -3.9 | -4.5 | -5.4 |
| 124 | Alignment | 星位序列 | Regent | 0.2 | -2.3 | -3.3 | 1.5 | -0.8 | -2 |
| 125 | Dying Star | 星灭 | Regent | 5.3 | -3.5 | -7.5 | 9.5 | 1.5 | -3.2 |
| 126 | Seven Stars | 七星 | Regent | -8.7 | -5.2 | -4.2 | -5.3 | -5.2 | -5.9 |
| 127 | Resonance | 共鸣 | Regent | -7.2 | -10.7 | -11.4 | -5.8 | -5.9 | -3.4 |


## 附：旧静态估值（10 张不可模拟卡，口径不同，仅供参考，不可与主表比较）

| # | English | 中文 | 卡池 | 未升级·short(4) | 未升级·mid(8) | 未升级·long(14) | 升级·short(4) | 升级·mid(8) | 升级·long(14) |
|---:|---|---|---|---:|---:|---:|---:|---:|---:|
| 1 | The Gambit | 孤注一掷 | Colorless | 89.45 | 89.45 | 89.45 | 133.375 | 133.375 | 133.375 |
| 2 | Panic Button | 应急按钮 | Colorless | 55.91 | 55.91 | 55.91 | 73.48 | 73.48 | 73.48 |
| 3 | Rend | 撕碎 | Colorless | 20 | 20 | 20 | 26 | 26 | 26 |
| 4 | Jackpot | 大奖 | Colorless | 7.6 | 7.3 | -2.2 | 11.6 | 8.8 | -5.2 |
| 5 | Discovery | 发现 | Colorless | -2.3 | 2.9 | 9.1 | -3.1 | -3.1 | -12 |
| 6 | Mayhem | 乱战 | Colorless | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 |
| 7 | Monarch's Gaze | 王之凝视 | Regent | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 |
| 8 | Nostalgia | 怀旧 | Colorless | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 | 1.6 |
| 9 | Anointed | 天选 | Colorless | 0 | 0 | 0 | 1.2 | 1.2 | 1.2 |
| 10 | Splash | 飞溅 | Colorless | -3.1 | -3.1 | -12 | -3.1 | -3.1 | -12 |
