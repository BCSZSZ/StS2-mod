# Regent + Colorless Beam Score Breakdown

This report is a pre-2026-07-14 snapshot of Regent/Colorless heuristic beam-entry scores. The runtime setup catalog is authoritative; `Quasar` was subsequently raised to beam setup `10` / `20`, so its two rows and ranks below are intentionally historical until the full report is regenerated.

Baseline assumptions: horizon = `midline`; resource prices are draw `5.2`, energy `10.0`, star `5.3`; next-turn resources are multiplied by `0.75`; no active powers, buffs, enemy Vulnerable, player Frail, prior cards played, or current hand/deck counters are assumed; X-cost damage is shown at current energy `3`; Forge proxy assumes no existing unexhausted Sovereign Blade, so one blade is valued.

Formula after the beam cleanup:

```text
BeamScore = I + ResourceNext + Setup + Tie
I = intrinsic/runtime-immediate proxy + x-cost baseline damage + reflect/HP/power-loss proxy + forge proxy
ResourceNext = ExplicitResource(draw, energy, star, next-turn resources) + blockNextTurn * blockValuePerBlock
Setup = Power ? max(BeamSetupValue, 99) : BeamSetupValue
Tie = exhaust?0.003 : 0 + retain?0.002 : 0 - energyCost*0.0001 - starCost*0.00005
```

Rows: `304` card forms. Generated from `data/generated/simulation_card_library.generated.json` and `history-analysis/data/localized_names_en_zhs.json`.

| Rank | 中文名 | English | Pool | Upg | Type | Cost | Star | I | ResourceNext | Setup | Tie | BeamScore | ModelId |
|---:|---|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 滚石 | Rolling Boulder | Colorless | 1 | Power | 3 | 0 | 0 | 0 | 137 | -0.0003 | 136.9997 | `CARD.ROLLING_BOULDER+1` |
| 2 | 彗星 | Comet | Regent | 1 | Attack | 0 | 5 | 50.049 | 0 | 74.4 | -0.0003 | 124.4488 | `CARD.COMET+1` |
| 3 | 天际钻头 | Heavenly Drill | Regent | 1 | Attack | 0 | 0 | 30 | 0 | 85.6 | 0 | 115.6 | `CARD.HEAVENLY_DRILL+1` |
| 4 | 决胜一击 | Knockout Blow | Regent | 1 | Attack | 3 | 0 | 38 | 26.5 | 50.3 | -0.0003 | 114.7997 | `CARD.KNOCKOUT_BLOW+1` |
| 5 | 铸剑者 | The Smith | Regent | 1 | Skill | 1 | 4 | 40 | 0 | 74.6 | -0.0003 | 114.5997 | `CARD.THE_SMITH+1` |
| 6 | 追踪之刃 | Seeking Edge | Regent | 1 | Power | 1 | 0 | 11 | 0 | 99 | -0.0001 | 109.9999 | `CARD.SEEKING_EDGE+1` |
| 7 | 追踪之刃 | Seeking Edge | Regent | 0 | Power | 1 | 0 | 7 | 0 | 99 | -0.0001 | 105.9999 | `CARD.SEEKING_EDGE` |
| 8 | 滚石 | Rolling Boulder | Colorless | 0 | Power | 3 | 0 | 0 | 0 | 104.9 | -0.0003 | 104.8997 | `CARD.ROLLING_BOULDER` |
| 9 | 创世之柱 | Pillar of Creation | Regent | 1 | Power | 1 | 0 | 3.74 | 0 | 99 | -0.0001 | 102.7399 | `CARD.PILLAR_OF_CREATION+1` |
| 10 | 彗星 | Comet | Regent | 0 | Attack | 0 | 5 | 39.049 | 0 | 62.9 | -0.0003 | 101.9488 | `CARD.COMET` |
| 11 | 创世之柱 | Pillar of Creation | Regent | 0 | Power | 1 | 0 | 2.805 | 0 | 99 | -0.0001 | 101.8049 | `CARD.PILLAR_OF_CREATION` |
| 12 | 怀旧 | Nostalgia | Colorless | 1 | Power | 0 | 0 | 1.6 | 0 | 99 | 0 | 100.6 | `CARD.NOSTALGIA+1` |
| 13 | 希望灯塔 | Beacon of Hope | Colorless | 0 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.BEACON_OF_HOPE` |
| 14 | 希望灯塔 | Beacon of Hope | Colorless | 1 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.BEACON_OF_HOPE+1` |
| 15 | 锤子时间 | Hammer Time | Regent | 1 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.HAMMER_TIME+1` |
| 16 | 乱战 | Mayhem | Colorless | 1 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.MAYHEM+1` |
| 17 | 王之凝视 | Monarch's Gaze | Regent | 1 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.MONARCHS_GAZE+1` |
| 18 | 怀旧 | Nostalgia | Colorless | 0 | Power | 1 | 0 | 1.6 | 0 | 99 | -0.0001 | 100.5999 | `CARD.NOSTALGIA` |
| 19 | 锤子时间 | Hammer Time | Regent | 0 | Power | 2 | 0 | 1.6 | 0 | 99 | -0.0002 | 100.5998 | `CARD.HAMMER_TIME` |
| 20 | 乱战 | Mayhem | Colorless | 0 | Power | 2 | 0 | 1.6 | 0 | 99 | -0.0002 | 100.5998 | `CARD.MAYHEM` |
| 21 | 王之凝视 | Monarch's Gaze | Regent | 0 | Power | 2 | 0 | 1.6 | 0 | 99 | -0.0002 | 100.5998 | `CARD.MONARCHS_GAZE` |
| 22 | 独白 | Monologue | Regent | 1 | Skill | 0 | 0 | 0 | 0 | 99 | 0.002 | 99.002 | `CARD.MONOLOGUE+1` |
| 23 | 自动化 | Automation | Colorless | 1 | Power | 0 | 0 | 0 | 0 | 99 | 0 | 99 | `CARD.AUTOMATION+1` |
| 24 | 独白 | Monologue | Regent | 0 | Skill | 0 | 0 | 0 | 0 | 99 | 0 | 99 | `CARD.MONOLOGUE` |
| 25 | 神气制胜 | Panache | Colorless | 0 | Power | 0 | 0 | 0 | 0 | 99 | 0 | 99 | `CARD.PANACHE` |
| 26 | 神气制胜 | Panache | Colorless | 1 | Power | 0 | 0 | 0 | 0 | 99 | 0 | 99 | `CARD.PANACHE+1` |
| 27 | 计策 | Stratagem | Colorless | 1 | Power | 0 | 0 | 0 | 0 | 99 | 0 | 99 | `CARD.STRATAGEM+1` |
| 28 | 武器库 | Arsenal | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ARSENAL` |
| 29 | 武器库 | Arsenal | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ARSENAL+1` |
| 30 | 自动化 | Automation | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.AUTOMATION` |
| 31 | 黑洞 | Black Hole | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.BLACK_HOLE` |
| 32 | 黑洞 | Black Hole | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.BLACK_HOLE+1` |
| 33 | 群星之子 | Child of the Stars | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.CHILD_OF_THE_STARS` |
| 34 | 群星之子 | Child of the Stars | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.CHILD_OF_THE_STARS+1` |
| 35 | 熵 | Entropy | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ENTROPY` |
| 36 | 熵 | Entropy | Colorless | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ENTROPY+1` |
| 37 | 勒紧 | Fasten | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.FASTEN` |
| 38 | 勒紧 | Fasten | Colorless | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.FASTEN+1` |
| 39 | 熔炉 | Furnace | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.FURNACE` |
| 40 | 熔炉 | Furnace | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.FURNACE+1` |
| 41 | 环绕轨道 | Orbit | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ORBIT+1` |
| 42 | 暗淡蓝点 | Pale Blue Dot | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PALE_BLUE_DOT` |
| 43 | 暗淡蓝点 | Pale Blue Dot | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PALE_BLUE_DOT+1` |
| 44 | 招架 | Parry | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PARRY` |
| 45 | 招架 | Parry | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PARRY+1` |
| 46 | 准备时间 | Prep Time | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PREP_TIME` |
| 47 | 准备时间 | Prep Time | Colorless | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PREP_TIME+1` |
| 48 | 非凡技艺 | Prowess | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PROWESS` |
| 49 | 非凡技艺 | Prowess | Colorless | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.PROWESS+1` |
| 50 | 王国资产 | Royalties | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ROYALTIES` |
| 51 | 王国资产 | Royalties | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.ROYALTIES+1` |
| 52 | 光谱偏移 | Spectrum Shift | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.SPECTRUM_SHIFT+1` |
| 53 | 计策 | Stratagem | Colorless | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.STRATAGEM` |
| 54 | 剑圣 | Sword Sage | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.SWORD_SAGE+1` |
| 55 | 暴政 | Tyranny | Regent | 0 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.TYRANNY` |
| 56 | 暴政 | Tyranny | Regent | 1 | Power | 1 | 0 | 0 | 0 | 99 | -0.0001 | 98.9999 | `CARD.TYRANNY+1` |
| 57 | 封印王座 | The Sealed Throne | Regent | 1 | Power | 0 | 3 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.THE_SEALED_THRONE+1` |
| 58 | 劫难 | Calamity | Colorless | 1 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.CALAMITY+1` |
| 59 | 创世纪 | Genesis | Regent | 0 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.GENESIS` |
| 60 | 创世纪 | Genesis | Regent | 1 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.GENESIS+1` |
| 61 | 环绕轨道 | Orbit | Regent | 0 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.ORBIT` |
| 62 | 光谱偏移 | Spectrum Shift | Regent | 0 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.SPECTRUM_SHIFT` |
| 63 | 剑圣 | Sword Sage | Regent | 0 | Power | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.SWORD_SAGE` |
| 64 | 炸弹 | The Bomb | Colorless | 0 | Skill | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.THE_BOMB` |
| 65 | 炸弹 | The Bomb | Colorless | 1 | Skill | 2 | 0 | 0 | 0 | 99 | -0.0002 | 98.9998 | `CARD.THE_BOMB+1` |
| 66 | 封印王座 | The Sealed Throne | Regent | 0 | Power | 1 | 3 | 0 | 0 | 99 | -0.0003 | 98.9998 | `CARD.THE_SEALED_THRONE` |
| 67 | 劫难 | Calamity | Colorless | 0 | Power | 3 | 0 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.CALAMITY` |
| 68 | 永恒铠甲 | Eternal Armor | Colorless | 0 | Power | 3 | 0 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.ETERNAL_ARMOR` |
| 69 | 永恒铠甲 | Eternal Armor | Colorless | 1 | Power | 3 | 0 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.ETERNAL_ARMOR+1` |
| 70 | 虚空形态 | Void Form | Regent | 0 | Power | 3 | 0 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.VOID_FORM` |
| 71 | 虚空形态 | Void Form | Regent | 1 | Power | 3 | 0 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.VOID_FORM+1` |
| 72 | 中子护盾 | Neutron Aegis | Regent | 0 | Power | 1 | 5 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.NEUTRON_AEGIS` |
| 73 | 中子护盾 | Neutron Aegis | Regent | 1 | Power | 1 | 5 | 0 | 0 | 99 | -0.0003 | 98.9997 | `CARD.NEUTRON_AEGIS+1` |
| 74 | 决胜一击 | Knockout Blow | Regent | 0 | Attack | 3 | 0 | 30 | 26.5 | 42.3 | -0.0003 | 98.7997 | `CARD.KNOCKOUT_BLOW` |
| 75 | 天际钻头 | Heavenly Drill | Regent | 0 | Attack | 0 | 0 | 24 | 0 | 68 | 0 | 92 | `CARD.HEAVENLY_DRILL` |
| 76 | 倒映 | Reflect | Regent | 1 | Skill | 1 | 3 | 38.699 | 0 | 50.8 | -0.0003 | 89.4988 | `CARD.REFLECT+1` |
| 77 | 胜券在王 | Royal Gamble | Regent | 1 | Skill | 0 | 5 | 0 | 47.7 | 39 | 0.0047 | 86.7047 | `CARD.ROYAL_GAMBLE+1` |
| 78 | 心神不宁 | Restlessness | Colorless | 1 | Skill | 0 | 0 | 0 | 45.6 | 40.8 | 0.002 | 86.402 | `CARD.RESTLESSNESS+1` |
| 79 | 铸剑者 | The Smith | Regent | 0 | Skill | 1 | 4 | 30 | 0 | 54.9 | -0.0003 | 84.8997 | `CARD.THE_SMITH` |
| 80 | 流星雨 | Meteor Shower | Regent | 1 | Attack | 0 | 2 | 33.509 | 0 | 50.3 | -0.0001 | 83.8089 | `CARD.METEOR_SHOWER+1` |
| 81 | 胜券在王 | Royal Gamble | Regent | 0 | Skill | 0 | 5 | 0 | 47.7 | 35.3 | 0.0027 | 83.0028 | `CARD.ROYAL_GAMBLE` |
| 82 | 葬送 | Devastate | Regent | 1 | Attack | 1 | 4 | 40 | 0 | 40 | -0.0003 | 79.9997 | `CARD.DEVASTATE+1` |
| 83 | 霸权 | Hegemony | Regent | 1 | Attack | 2 | 0 | 18 | 22.5 | 38.2 | -0.0002 | 78.6998 | `CARD.HEGEMONY+1` |
| 84 | 大爆炸 | Big Bang | Regent | 0 | Skill | 0 | 0 | 5 | 20.5 | 46.6 | 0.003 | 72.103 | `CARD.BIG_BANG` |
| 85 | 大爆炸 | Big Bang | Regent | 1 | Skill | 0 | 0 | 5 | 20.5 | 46.5 | 0.003 | 72.003 | `CARD.BIG_BANG+1` |
| 86 | 孤注一掷 | The Gambit | Colorless | 1 | Skill | 0 | 0 | 71.721 | 0 | 0 | 0 | 71.721 | `CARD.THE_GAMBIT+1` |
| 87 | 王者之踢 | Kingly Kick | Regent | 1 | Attack | 4 | 0 | 35 | 0 | 35 | -0.0004 | 69.9996 | `CARD.KINGLY_KICK+1` |
| 88 | 连射 | Volley | Colorless | 1 | Attack | 0 | 0 | 42 | 0 | 27.7 | 0 | 69.7 | `CARD.VOLLEY+1` |
| 89 | 迫降 | Crash Landing | Regent | 1 | Attack | 1 | 0 | 33.8 | 0 | 33.8 | -0.0001 | 67.5999 | `CARD.CRASH_LANDING+1` |
| 90 | 倒映 | Reflect | Regent | 0 | Skill | 1 | 3 | 29.024 | 0 | 37.7 | -0.0003 | 66.7238 | `CARD.REFLECT` |
| 91 | 铸墙 | Bulwark | Regent | 1 | Skill | 2 | 0 | 27.024 | 0 | 39.3 | -0.0002 | 66.3238 | `CARD.BULWARK+1` |
| 92 | 流星雨 | Meteor Shower | Regent | 0 | Attack | 0 | 2 | 24.409 | 0 | 40.9 | -0.0001 | 65.3089 | `CARD.METEOR_SHOWER` |
| 93 | 震荡波 | Shockwave | Colorless | 1 | Skill | 2 | 0 | 10.248 | 0 | 52.2 | 0.0028 | 62.4508 | `CARD.SHOCKWAVE+1` |
| 94 | 伽马爆破 | Gamma Blast | Regent | 1 | Attack | 0 | 3 | 22.776 | 0 | 39 | -0.0002 | 61.7758 | `CARD.GAMMA_BLAST+1` |
| 95 | 霸权 | Hegemony | Regent | 0 | Attack | 2 | 0 | 15 | 15 | 30.1 | -0.0002 | 60.0998 | `CARD.HEGEMONY` |
| 96 | 葬送 | Devastate | Regent | 0 | Attack | 1 | 4 | 30 | 0 | 30 | -0.0003 | 59.9997 | `CARD.DEVASTATE` |
| 97 | 心神不宁 | Restlessness | Colorless | 0 | Skill | 0 | 0 | 0 | 30.4 | 27.3 | 0.002 | 57.702 | `CARD.RESTLESSNESS` |
| 98 | 战略大师 | Master of Strategy | Colorless | 1 | Skill | 0 | 0 | 0 | 20.8 | 34.8 | 0.003 | 55.603 | `CARD.MASTER_OF_STRATEGY+1` |
| 99 | 星灭 | Dying Star | Regent | 1 | Attack | 1 | 3 | 27.5 | 0 | 27.5 | -0.0003 | 54.9997 | `CARD.DYING_STAR+1` |
| 100 | 迫降 | Crash Landing | Regent | 0 | Attack | 1 | 0 | 27.3 | 0 | 27.3 | -0.0001 | 54.5999 | `CARD.CRASH_LANDING` |
| 101 | 王者之踢 | Kingly Kick | Regent | 0 | Attack | 4 | 0 | 27 | 0 | 27 | -0.0004 | 53.9996 | `CARD.KINGLY_KICK` |
| 102 | 伽马爆破 | Gamma Blast | Regent | 0 | Attack | 0 | 3 | 17.776 | 0 | 34 | -0.0002 | 51.7758 | `CARD.GAMMA_BLAST` |
| 103 | 铸墙 | Bulwark | Regent | 0 | Skill | 2 | 0 | 21.219 | 0 | 30.4 | -0.0002 | 51.6188 | `CARD.BULWARK` |
| 104 | 淬炼刀刃 | Refine Blade | Regent | 1 | Skill | 1 | 0 | 13 | 7.5 | 30.2 | -0.0001 | 50.6999 | `CARD.REFINE_BLADE+1` |
| 105 | 贪婪之手 | Hand of Greed | Colorless | 1 | Attack | 2 | 0 | 25 | 0 | 25 | -0.0002 | 49.9998 | `CARD.HAND_OF_GREED+1` |
| 106 | 孤注一掷 | The Gambit | Colorless | 0 | Skill | 0 | 0 | 48.347 | 0 | 0 | 0 | 48.347 | `CARD.THE_GAMBIT` |
| 107 | 轰击 | Bombardment | Regent | 1 | Attack | 3 | 0 | 24 | 0 | 24 | 0.0027 | 48.0027 | `CARD.BOMBARDMENT+1` |
| 108 | 预言 | Prophesize | Regent | 1 | Skill | 2 | 0 | 0 | 46.8 | 0 | -0.0002 | 46.7998 | `CARD.PROPHESIZE+1` |
| 109 | 流光溢彩 | Glitterstream | Regent | 1 | Skill | 2 | 0 | 12.154 | 6.5446 | 28 | -0.0002 | 46.6984 | `CARD.GLITTERSTREAM+1` |
| 110 | 星位序列 | Alignment | Regent | 1 | Skill | 0 | 3 | 0 | 30 | 15.9 | -0.0002 | 45.8999 | `CARD.ALIGNMENT+1` |
| 111 | 连射 | Volley | Colorless | 0 | Attack | 0 | 0 | 30 | 0 | 15.3 | 0 | 45.3 | `CARD.VOLLEY` |
| 112 | 星灭 | Dying Star | Regent | 0 | Attack | 1 | 3 | 22.5 | 0 | 22.5 | -0.0003 | 44.9997 | `CARD.DYING_STAR` |
| 113 | 生产制造 | Production | Colorless | 1 | Skill | 0 | 0 | 0 | 30 | 14.3 | 0.003 | 44.303 | `CARD.PRODUCTION+1` |
| 114 | 战略大师 | Master of Strategy | Colorless | 0 | Skill | 0 | 0 | 0 | 15.6 | 27.8 | 0.003 | 43.403 | `CARD.MASTER_OF_STRATEGY` |
| 115 | 震荡波 | Shockwave | Colorless | 0 | Skill | 2 | 0 | 7.864 | 0 | 35.3 | 0.0028 | 43.1668 | `CARD.SHOCKWAVE` |
| 116 | 星界脉冲 | Astral Pulse | Regent | 1 | Attack | 0 | 3 | 20.8 | 0 | 20.8 | -0.0002 | 41.5999 | `CARD.ASTRAL_PULSE+1` |
| 117 | 应急按钮 | Panic Button | Colorless | 1 | Skill | 0 | 0 | 40.597 | 0 | 0 | 0.003 | 40.6 | `CARD.PANIC_BUTTON+1` |
| 118 | 究极打击 | Ultimate Strike | Colorless | 1 | Attack | 1 | 0 | 20 | 0 | 20 | -0.0001 | 39.9999 | `CARD.ULTIMATE_STRIKE+1` |
| 119 | 贪婪之手 | Hand of Greed | Colorless | 0 | Attack | 2 | 0 | 20 | 0 | 20 | -0.0002 | 39.9998 | `CARD.HAND_OF_GREED` |
| 120 | 淬炼刀刃 | Refine Blade | Regent | 0 | Skill | 1 | 0 | 9 | 7.5 | 23.2 | -0.0001 | 39.6999 | `CARD.REFINE_BLADE` |
| 121 | 战火铸就 | Wrought in War | Regent | 1 | Attack | 1 | 0 | 18 | 0 | 21.6 | -0.0001 | 39.5999 | `CARD.WROUGHT_IN_WAR+1` |
| 122 | 闪亮登场 | Dramatic Entrance | Colorless | 1 | Attack | 0 | 0 | 19.5 | 0 | 19.5 | 0.003 | 39.003 | `CARD.DRAMATIC_ENTRANCE+1` |
| 123 | 大奖 | Jackpot | Colorless | 1 | Attack | 3 | 0 | 30 | 0 | 9 | -0.0003 | 38.9997 | `CARD.JACKPOT+1` |
| 124 | 护驾！！！ | GUARDS!!! | Regent | 1 | Skill | 2 | 0 | 0 | 0 | 38.5 | 0.0028 | 38.5028 | `CARD.GUARDS+1` |
| 125 | 均衡 | Equilibrium | Colorless | 1 | Skill | 2 | 0 | 14.959 | 0 | 23.5 | -0.0002 | 38.4588 | `CARD.EQUILIBRIUM+1` |
| 126 | 陨星 | Falling Star | Regent | 1 | Attack | 0 | 2 | 15.184 | 0 | 23.1 | -0.0001 | 38.2839 | `CARD.FALLING_STAR+1` |
| 127 | 太阳打击 | Solar Strike | Regent | 1 | Attack | 1 | 0 | 10 | 10.6 | 17.3 | -0.0001 | 37.8999 | `CARD.SOLAR_STRIKE+1` |
| 128 | 流光溢彩 | Glitterstream | Regent | 0 | Skill | 2 | 0 | 10.284 | 4.6747 | 22.5 | -0.0002 | 37.4585 | `CARD.GLITTERSTREAM` |
| 129 | 抉择，抉择 | Decisions, Decisions | Regent | 1 | Skill | 0 | 6 | 0 | 26 | 10.9 | 0.0027 | 36.9027 | `CARD.DECISIONS_DECISIONS+1` |
| 130 | 究极防御 | Ultimate Defend | Colorless | 1 | Skill | 1 | 0 | 14.024 | 0 | 22.5 | -0.0001 | 36.5239 | `CARD.ULTIMATE_DEFEND+1` |
| 131 | 征服者 | Conqueror | Regent | 1 | Skill | 1 | 0 | 5 | 0 | 31.4 | -0.0001 | 36.3999 | `CARD.CONQUEROR+1` |
| 132 | 传承之锤 | Heirloom Hammer | Regent | 1 | Attack | 2 | 0 | 25 | 0 | 11.4 | -0.0002 | 36.3998 | `CARD.HEIRLOOM_HAMMER+1` |
| 133 | 收集光辉 | Gather Light | Regent | 1 | Skill | 1 | 0 | 10.285 | 5.3 | 20.8 | -0.0001 | 36.3849 | `CARD.GATHER_LIGHT+1` |
| 134 | 黑暗镣铐 | Dark Shackles | Colorless | 1 | Skill | 0 | 0 | 18 | 0 | 18 | 0.003 | 36.003 | `CARD.DARK_SHACKLES+1` |
| 135 | 轰击 | Bombardment | Regent | 0 | Attack | 3 | 0 | 18 | 0 | 18 | 0.0027 | 36.0027 | `CARD.BOMBARDMENT` |
| 136 | 隐秘藏品 | Hidden Cache | Regent | 1 | Skill | 1 | 0 | 0 | 21.2 | 12.6 | -0.0001 | 33.7999 | `CARD.HIDDEN_CACHE+1` |
| 137 | 引导之星 | Guiding Star | Regent | 1 | Attack | 1 | 2 | 13 | 15.6 | 5 | -0.0002 | 33.5998 | `CARD.GUIDING_STAR+1` |
| 138 | 星位序列 | Alignment | Regent | 0 | Skill | 0 | 3 | 0 | 20 | 12.9 | -0.0002 | 32.8999 | `CARD.ALIGNMENT` |
| 139 | 汇流 | Convergence | Regent | 1 | Skill | 1 | 0 | 0 | 15.45 | 17.3 | -0.0001 | 32.7499 | `CARD.CONVERGENCE+1` |
| 140 | 箭雨 | Salvo | Colorless | 1 | Attack | 1 | 0 | 16 | 0 | 16 | -0.0001 | 31.9999 | `CARD.SALVO+1` |
| 141 | 所向无敌 | I Am Invincible | Regent | 1 | Skill | 1 | 0 | 12.154 | 0 | 19.4 | -0.0001 | 31.5539 | `CARD.I_AM_INVINCIBLE+1` |
| 142 | 大奖 | Jackpot | Colorless | 0 | Attack | 3 | 0 | 25 | 0 | 6.5 | -0.0003 | 31.4997 | `CARD.JACKPOT` |
| 143 | 生产制造 | Production | Colorless | 0 | Skill | 0 | 0 | 0 | 20 | 11.4 | 0.003 | 31.403 | `CARD.PRODUCTION` |
| 144 | 应急按钮 | Panic Button | Colorless | 0 | Skill | 0 | 0 | 31.248 | 0 | 0 | 0.003 | 31.251 | `CARD.PANIC_BUTTON` |
| 145 | 星界脉冲 | Astral Pulse | Regent | 0 | Attack | 0 | 3 | 15.6 | 0 | 15.6 | -0.0002 | 31.1998 | `CARD.ASTRAL_PULSE` |
| 146 | 预言 | Prophesize | Regent | 0 | Skill | 2 | 0 | 0 | 31.2 | 0 | -0.0002 | 31.1998 | `CARD.PROPHESIZE` |
| 147 | 均衡 | Equilibrium | Colorless | 0 | Skill | 2 | 0 | 12.154 | 0 | 18.9 | -0.0002 | 31.0538 | `CARD.EQUILIBRIUM` |
| 148 | 急躁 | Impatience | Colorless | 1 | Skill | 0 | 0 | 0 | 15.6 | 15.3 | 0 | 30.9 | `CARD.IMPATIENCE+1` |
| 149 | 战火铸就 | Wrought in War | Regent | 0 | Attack | 1 | 0 | 14 | 0 | 16.6 | -0.0001 | 30.5999 | `CARD.WROUGHT_IN_WAR` |
| 150 | 陨星 | Falling Star | Regent | 0 | Attack | 0 | 2 | 11.184 | 0 | 19.2 | -0.0001 | 30.3839 | `CARD.FALLING_STAR` |
| 151 | 碰撞轨迹 | Collision Course | Regent | 1 | Attack | 0 | 0 | 15 | 0 | 15.1 | 0 | 30.1 | `CARD.COLLISION_COURSE+1` |
| 152 | 相信着你 | Believe in You | Colorless | 1 | Skill | 0 | 0 | 0 | 30 | 0 | 0 | 30 | `CARD.BELIEVE_IN_YOU+1` |
| 153 | 粒子墙 | Particle Wall | Regent | 1 | Skill | 0 | 2 | 11.219 | 0 | 18.1 | -0.0001 | 29.3189 | `CARD.PARTICLE_WALL+1` |
| 154 | 光子切割 | Photon Cut | Regent | 1 | Attack | 1 | 0 | 13 | 10.4 | 5.8 | -0.0001 | 29.1999 | `CARD.PHOTON_CUT+1` |
| 155 | 收集光辉 | Gather Light | Regent | 0 | Skill | 1 | 0 | 7.48 | 5.3 | 16.2 | -0.0001 | 28.9799 | `CARD.GATHER_LIGHT` |
| 156 | 征服者 | Conqueror | Regent | 0 | Skill | 1 | 0 | 3 | 0 | 25.9 | -0.0001 | 28.8999 | `CARD.CONQUEROR` |
| 157 | 战利品 | Spoils of Battle | Regent | 1 | Skill | 1 | 0 | 8 | 10.4 | 10.4 | -0.0001 | 28.7999 | `CARD.SPOILS_OF_BATTLE+1` |
| 158 | 闪亮登场 | Dramatic Entrance | Colorless | 0 | Attack | 0 | 0 | 14.3 | 0 | 14.3 | 0.003 | 28.603 | `CARD.DRAMATIC_ENTRANCE` |
| 159 | 征召上前 | Summon Forth | Regent | 1 | Skill | 1 | 0 | 11 | 0 | 17.5 | -0.0001 | 28.4999 | `CARD.SUMMON_FORTH+1` |
| 160 | 隐秘藏品 | Hidden Cache | Regent | 0 | Skill | 1 | 0 | 0 | 17.225 | 11 | -0.0001 | 28.2249 | `CARD.HIDDEN_CACHE` |
| 161 | 传承之锤 | Heirloom Hammer | Regent | 0 | Attack | 2 | 0 | 20 | 0 | 8.2 | -0.0002 | 28.1998 | `CARD.HEIRLOOM_HAMMER` |
| 162 | 君权自授 | Manifest Authority | Regent | 1 | Skill | 1 | 0 | 7.48 | 0 | 20.6 | -0.0001 | 28.0799 | `CARD.MANIFEST_AUTHORITY+1` |
| 163 | 辉光 | Glow | Regent | 1 | Skill | 1 | 0 | 0 | 19.7 | 8.3 | -0.0001 | 27.9999 | `CARD.GLOW+1` |
| 164 | 究极打击 | Ultimate Strike | Colorless | 0 | Attack | 1 | 0 | 14 | 0 | 14 | -0.0001 | 27.9999 | `CARD.ULTIMATE_STRIKE` |
| 165 | 亮剑 | Flash of Steel | Colorless | 1 | Attack | 0 | 0 | 8 | 5.2 | 14.5 | 0 | 27.7 | `CARD.FLASH_OF_STEEL+1` |
| 166 | 妙计 | Finesse | Colorless | 1 | Skill | 0 | 0 | 6.545 | 5.2 | 15.9 | 0 | 27.645 | `CARD.FINESSE+1` |
| 167 | 星星点点 | Patter | Regent | 1 | Skill | 1 | 0 | 9.35 | 0 | 18 | -0.0001 | 27.3499 | `CARD.PATTER+1` |
| 168 | 太阳打击 | Solar Strike | Regent | 0 | Attack | 1 | 0 | 9 | 5.3 | 13 | -0.0001 | 27.2999 | `CARD.SOLAR_STRIKE` |
| 169 | 究极防御 | Ultimate Defend | Colorless | 0 | Skill | 1 | 0 | 10.284 | 0 | 16.3 | -0.0001 | 26.5839 | `CARD.ULTIMATE_DEFEND` |
| 170 | 新月长矛 | Crescent Spear | Regent | 1 | Attack | 1 | 1 | 8 | 0 | 18.5 | -0.0002 | 26.4998 | `CARD.CRESCENT_SPEAR+1` |
| 171 | 深谋远虑 | Thinking Ahead | Colorless | 0 | Skill | 0 | 0 | 0 | 10.4 | 15.6 | 0.003 | 26.003 | `CARD.THINKING_AHEAD` |
| 172 | 撕碎 | Rend | Colorless | 1 | Attack | 2 | 0 | 26 | 0 | 0 | -0.0002 | 25.9998 | `CARD.REND+1` |
| 173 | 明耀打击 | Shining Strike | Regent | 1 | Attack | 1 | 0 | 11 | 10.6 | 4.3 | -0.0001 | 25.8999 | `CARD.SHINING_STRIKE+1` |
| 174 | 崇拜 | Venerate | Regent | 1 | Skill | 1 | 0 | 0 | 15.9 | 10 | -0.0001 | 25.8999 | `CARD.VENERATE+1` |
| 175 | 下砸 | Crush Under | Regent | 1 | Attack | 1 | 0 | 12.8 | 0 | 12.8 | -0.0001 | 25.5999 | `CARD.CRUSH_UNDER+1` |
| 176 | 汇流 | Convergence | Regent | 0 | Skill | 1 | 0 | 0 | 11.475 | 13.9 | -0.0001 | 25.3749 | `CARD.CONVERGENCE` |
| 177 | 引导之星 | Guiding Star | Regent | 0 | Attack | 1 | 2 | 12 | 10.4 | 2.2 | -0.0002 | 24.5998 | `CARD.GUIDING_STAR` |
| 178 | 群星斗篷 | Cloak of Stars | Regent | 1 | Skill | 0 | 1 | 9.35 | 0 | 15 | -0.0001 | 24.3499 | `CARD.CLOAK_OF_STARS+1` |
| 179 | 所向无敌 | I Am Invincible | Regent | 0 | Skill | 1 | 0 | 9.349 | 0 | 14.8 | -0.0001 | 24.1489 | `CARD.I_AM_INVINCIBLE` |
| 180 | 护驾！！！ | GUARDS!!! | Regent | 0 | Skill | 2 | 0 | 0 | 0 | 24 | 0.0028 | 24.0028 | `CARD.GUARDS` |
| 181 | 箭雨 | Salvo | Colorless | 0 | Attack | 1 | 0 | 12 | 0 | 12 | -0.0001 | 23.9999 | `CARD.SALVO` |
| 182 | 新月长矛 | Crescent Spear | Regent | 0 | Attack | 1 | 1 | 8 | 0 | 15 | -0.0002 | 22.9998 | `CARD.CRESCENT_SPEAR` |
| 183 | 探寻打击 | Seeker Strike | Colorless | 1 | Attack | 1 | 0 | 12 | 0 | 10.3 | -0.0001 | 22.2999 | `CARD.SEEKER_STRIKE+1` |
| 184 | 粒子墙 | Particle Wall | Regent | 0 | Skill | 0 | 2 | 8.414 | 0 | 13.6 | -0.0001 | 22.0139 | `CARD.PARTICLE_WALL` |
| 185 | 万向斩 | Omnislice | Colorless | 1 | Attack | 0 | 0 | 11 | 0 | 11 | 0 | 22 | `CARD.OMNISLICE+1` |
| 186 | 碰撞轨迹 | Collision Course | Regent | 0 | Attack | 0 | 0 | 11 | 0 | 10.9 | 0 | 21.9 | `CARD.COLLISION_COURSE` |
| 187 | 战利品 | Spoils of Battle | Regent | 0 | Skill | 1 | 0 | 5 | 10.4 | 6.5 | -0.0001 | 21.8999 | `CARD.SPOILS_OF_BATTLE` |
| 188 | 黑暗镣铐 | Dark Shackles | Colorless | 0 | Skill | 0 | 0 | 10.8 | 0 | 10.8 | 0.003 | 21.603 | `CARD.DARK_SHACKLES` |
| 189 | 急躁 | Impatience | Colorless | 0 | Skill | 0 | 0 | 0 | 10.4 | 11.1 | 0 | 21.5 | `CARD.IMPATIENCE` |
| 190 | 亮剑 | Flash of Steel | Colorless | 0 | Attack | 0 | 0 | 5 | 5.2 | 11.1 | 0 | 21.3 | `CARD.FLASH_OF_STEEL` |
| 191 | 星星点点 | Patter | Regent | 0 | Skill | 1 | 0 | 7.48 | 0 | 13.8 | -0.0001 | 21.2799 | `CARD.PATTER` |
| 192 | 征召上前 | Summon Forth | Regent | 0 | Skill | 1 | 0 | 8 | 0 | 13.2 | -0.0001 | 21.1999 | `CARD.SUMMON_FORTH` |
| 193 | 微光 | Glimmer | Regent | 1 | Skill | 1 | 0 | 0 | 20.8 | 0 | -0.0001 | 20.7999 | `CARD.GLIMMER+1` |
| 194 | 下砸 | Crush Under | Regent | 0 | Attack | 1 | 0 | 10.3 | 0 | 10.3 | -0.0001 | 20.5999 | `CARD.CRUSH_UNDER` |
| 195 | 妙计 | Finesse | Colorless | 0 | Skill | 0 | 0 | 3.74 | 5.2 | 11.6 | 0 | 20.54 | `CARD.FINESSE` |
| 196 | 明耀打击 | Shining Strike | Regent | 0 | Attack | 1 | 0 | 8 | 10.6 | 1.5 | -0.0001 | 20.0999 | `CARD.SHINING_STRIKE` |
| 197 | 相信着你 | Believe in You | Colorless | 0 | Skill | 0 | 0 | 0 | 20 | 0 | 0 | 20 | `CARD.BELIEVE_IN_YOU` |
| 198 | 王者之拳 | Kingly Punch | Regent | 1 | Attack | 1 | 0 | 10 | 0 | 10 | -0.0001 | 19.9999 | `CARD.KINGLY_PUNCH+1` |
| 199 | 撕碎 | Rend | Colorless | 0 | Attack | 2 | 0 | 20 | 0 | 0 | -0.0002 | 19.9998 | `CARD.REND` |
| 200 | 辉光 | Glow | Regent | 0 | Skill | 1 | 0 | 0 | 14.4 | 5.4 | -0.0001 | 19.7999 | `CARD.GLOW` |
| 201 | 超质量体 | Supermassive | Regent | 1 | Attack | 1 | 0 | 5 | 0 | 14.5 | -0.0001 | 19.4999 | `CARD.SUPERMASSIVE+1` |
| 202 | 防御 | Defend | Regent | 1 | Skill | 1 | 0 | 7.48 | 0 | 11.7 | -0.0001 | 19.1799 | `CARD.DEFEND_REGENT+1` |
| 203 | 击倒 | Knockdown | Colorless | 1 | Attack | 3 | 0 | 18.8 | 0 | 0 | -0.0003 | 18.7997 | `CARD.KNOCKDOWN+1` |
| 204 | 七星 | Seven Stars | Regent | 1 | Attack | 1 | 7 | 9.1 | 0 | 9.1 | -0.0004 | 18.1995 | `CARD.SEVEN_STARS+1` |
| 205 | 七星 | Seven Stars | Regent | 0 | Attack | 2 | 7 | 9.1 | 0 | 9.1 | -0.0006 | 18.1994 | `CARD.SEVEN_STARS` |
| 206 | 秘密技法 | Secret Technique | Colorless | 0 | Skill | 0 | 0 | 0 | 0 | 18.1 | 0.003 | 18.103 | `CARD.SECRET_TECHNIQUE` |
| 207 | 秘密武器 | Secret Weapon | Colorless | 0 | Skill | 0 | 0 | 0 | 0 | 18.1 | 0.003 | 18.103 | `CARD.SECRET_WEAPON` |
| 208 | 拳斗 | Fisticuffs | Colorless | 1 | Attack | 1 | 0 | 9 | 0 | 9 | -0.0001 | 17.9999 | `CARD.FISTICUFFS+1` |
| 209 | 打击 | Strike | Regent | 1 | Attack | 1 | 0 | 9 | 0 | 9 | -0.0001 | 17.9999 | `CARD.STRIKE_REGENT+1` |
| 210 | 金斧 | Gold Axe | Colorless | 0 | Attack | 1 | 0 | 0 | 0 | 17.6 | -0.0001 | 17.5999 | `CARD.GOLD_AXE` |
| 211 | 崇拜 | Venerate | Regent | 0 | Skill | 1 | 0 | 0 | 10.6 | 6.9 | -0.0001 | 17.4999 | `CARD.VENERATE` |
| 212 | 抉择，抉择 | Decisions, Decisions | Regent | 0 | Skill | 0 | 6 | 0 | 15.6 | 1.8 | 0.0027 | 17.4027 | `CARD.DECISIONS_DECISIONS` |
| 213 | 锻打成型 | Beat into Shape | Regent | 1 | Attack | 1 | 0 | 7 | 0 | 10.4 | -0.0001 | 17.3999 | `CARD.BEAT_INTO_SHAPE+1` |
| 214 | 冲锋！！ | CHARGE!! | Regent | 1 | Skill | 1 | 0 | 0 | 0 | 17.1 | -0.0001 | 17.0999 | `CARD.CHARGE+1` |
| 215 | 超质量体 | Supermassive | Regent | 0 | Attack | 1 | 0 | 5 | 0 | 12.1 | -0.0001 | 17.0999 | `CARD.SUPERMASSIVE` |
| 216 | 群星斗篷 | Cloak of Stars | Regent | 0 | Skill | 0 | 1 | 6.545 | 0 | 10.4 | -0.0001 | 16.9449 | `CARD.CLOAK_OF_STARS` |
| 217 | 探寻打击 | Seeker Strike | Colorless | 0 | Attack | 1 | 0 | 9 | 0 | 7.7 | -0.0001 | 16.6999 | `CARD.SEEKER_STRIKE` |
| 218 | 双打组合 | Tag Team | Colorless | 1 | Attack | 2 | 0 | 16.6 | 0 | 0 | -0.0002 | 16.5998 | `CARD.TAG_TEAM+1` |
| 219 | 君权自授 | Manifest Authority | Regent | 0 | Skill | 1 | 0 | 6.545 | 0 | 10 | -0.0001 | 16.5449 | `CARD.MANIFEST_AUTHORITY` |
| 220 | 金斧 | Gold Axe | Colorless | 1 | Attack | 1 | 0 | 0 | 0 | 16.2 | 0.0019 | 16.2019 | `CARD.GOLD_AXE+1` |
| 221 | 光子切割 | Photon Cut | Regent | 0 | Attack | 1 | 0 | 10 | 5.2 | 0.9 | -0.0001 | 16.0999 | `CARD.PHOTON_CUT` |
| 222 | 万向斩 | Omnislice | Colorless | 0 | Attack | 0 | 0 | 8 | 0 | 8 | 0 | 16 | `CARD.OMNISLICE` |
| 223 | 王者之拳 | Kingly Punch | Regent | 0 | Attack | 1 | 0 | 8 | 0 | 8 | -0.0001 | 15.9999 | `CARD.KINGLY_PUNCH` |
| 224 | 集结 | Rally | Colorless | 1 | Skill | 2 | 0 | 15.894 | 0 | 0 | -0.0002 | 15.8938 | `CARD.RALLY+1` |
| 225 | 抱团 | Huddle Up | Colorless | 1 | Skill | 1 | 0 | 0 | 15.6 | 0 | 0.0029 | 15.6029 | `CARD.HUDDLE_UP+1` |
| 226 | 既定事项 | Foregone Conclusion | Regent | 1 | Skill | 1 | 0 | 0 | 11.7 | 3.9 | -0.0001 | 15.5999 | `CARD.FOREGONE_CONCLUSION+1` |
| 227 | 微光 | Glimmer | Regent | 0 | Skill | 1 | 0 | 0 | 15.6 | 0 | -0.0001 | 15.5999 | `CARD.GLIMMER` |
| 228 | 何人僭越 | Know Thy Place | Regent | 1 | Skill | 0 | 0 | 3.184 | 0 | 11.8 | 0 | 14.984 | `CARD.KNOW_THY_PLACE+1` |
| 229 | 托举 | Lift | Colorless | 1 | Skill | 1 | 0 | 14.959 | 0 | 0 | -0.0001 | 14.9589 | `CARD.LIFT+1` |
| 230 | 拳斗 | Fisticuffs | Colorless | 0 | Attack | 1 | 0 | 7 | 0 | 7 | -0.0001 | 13.9999 | `CARD.FISTICUFFS` |
| 231 | 无休手斧 | Thrumming Hatchet | Colorless | 1 | Attack | 1 | 0 | 14 | 0 | 0 | -0.0001 | 13.9999 | `CARD.THRUMMING_HATCHET+1` |
| 232 | 何人僭越 | Know Thy Place | Regent | 0 | Skill | 0 | 0 | 3.184 | 0 | 10.8 | 0.003 | 13.987 | `CARD.KNOW_THY_PLACE` |
| 233 | 拦截 | Intercept | Colorless | 1 | Skill | 1 | 0 | 13.754 | 0 | 0 | -0.0001 | 13.7539 | `CARD.INTERCEPT+1` |
| 234 | 新生之喜 | Bundle of Joy | Regent | 0 | Skill | 1 | 0 | 0 | 0 | 13.7 | 0.0029 | 13.7029 | `CARD.BUNDLE_OF_JOY` |
| 235 | 新生之喜 | Bundle of Joy | Regent | 1 | Skill | 1 | 0 | 0 | 0 | 13.7 | 0.0029 | 13.7029 | `CARD.BUNDLE_OF_JOY+1` |
| 236 | 冲锋！！ | CHARGE!! | Regent | 0 | Skill | 1 | 0 | 0 | 0 | 13.3 | -0.0001 | 13.2999 | `CARD.CHARGE` |
| 237 | 锻打成型 | Beat into Shape | Regent | 0 | Attack | 1 | 0 | 5 | 0 | 8.2 | -0.0001 | 13.1999 | `CARD.BEAT_INTO_SHAPE` |
| 238 | 击倒 | Knockdown | Colorless | 0 | Attack | 3 | 0 | 13.2 | 0 | 0 | -0.0003 | 13.1997 | `CARD.KNOCKDOWN` |
| 239 | 协同配合 | Coordinate | Colorless | 1 | Skill | 1 | 0 | 12.8 | 0 | 0 | -0.0001 | 12.7999 | `CARD.COORDINATE+1` |
| 240 | 双打组合 | Tag Team | Colorless | 0 | Attack | 2 | 0 | 12.6 | 0 | 0 | -0.0002 | 12.5998 | `CARD.TAG_TEAM` |
| 241 | 宇宙冷漠 | Cosmic Indifference | Regent | 1 | Skill | 1 | 0 | 8.415 | 0 | 4.1 | -0.0001 | 12.5149 | `CARD.COSMIC_INDIFFERENCE+1` |
| 242 | 心灵震慑 | Mind Blast | Colorless | 0 | Attack | 1 | 0 | 0 | 0 | 12.4 | -0.0001 | 12.3999 | `CARD.MIND_BLAST` |
| 243 | 群起攻之 | Gang Up | Colorless | 1 | Attack | 1 | 0 | 12 | 0 | 0 | -0.0001 | 11.9999 | `CARD.GANG_UP+1` |
| 244 | 打击 | Strike | Regent | 0 | Attack | 1 | 0 | 6 | 0 | 6 | -0.0001 | 11.9999 | `CARD.STRIKE_REGENT` |
| 245 | 天穹之力 | Celestial Might | Regent | 0 | Attack | 2 | 0 | 6 | 0 | 6 | -0.0002 | 11.9998 | `CARD.CELESTIAL_MIGHT` |
| 246 | 天穹之力 | Celestial Might | Regent | 1 | Attack | 2 | 0 | 6 | 0 | 6 | -0.0002 | 11.9998 | `CARD.CELESTIAL_MIGHT+1` |
| 247 | 星尘 | Stardust | Regent | 1 | Attack | 0 | 0 | 5.95 | 0 | 6 | 0 | 11.95 | `CARD.STARDUST+1` |
| 248 | 深谋远虑 | Thinking Ahead | Colorless | 1 | Skill | 0 | 0 | 0 | 10.4 | 1.5 | 0 | 11.9 | `CARD.THINKING_AHEAD+1` |
| 249 | 防御 | Defend | Regent | 0 | Skill | 1 | 0 | 4.675 | 0 | 7.1 | -0.0001 | 11.7749 | `CARD.DEFEND_REGENT` |
| 250 | 类星体 | Quasar | Regent | 1 | Skill | 0 | 2 | 0 | 0 | 11.4 | -0.0001 | 11.3999 | `CARD.QUASAR+1` |
| 251 | 集结 | Rally | Colorless | 0 | Skill | 2 | 0 | 11.219 | 0 | 0 | -0.0002 | 11.2188 | `CARD.RALLY` |
| 252 | 心灵震慑 | Mind Blast | Colorless | 1 | Attack | 0 | 0 | 0 | 0 | 11.1 | 0 | 11.1 | `CARD.MIND_BLAST+1` |
| 253 | 无休手斧 | Thrumming Hatchet | Colorless | 0 | Attack | 1 | 0 | 11 | 0 | 0 | -0.0001 | 10.9999 | `CARD.THRUMMING_HATCHET` |
| 254 | 既定事项 | Foregone Conclusion | Regent | 0 | Skill | 1 | 0 | 0 | 7.8 | 3.1 | -0.0001 | 10.8999 | `CARD.FOREGONE_CONCLUSION` |
| 255 | 抱团 | Huddle Up | Colorless | 0 | Skill | 1 | 0 | 0 | 10.4 | 0 | 0.0029 | 10.4029 | `CARD.HUDDLE_UP` |
| 256 | 托举 | Lift | Colorless | 0 | Skill | 1 | 0 | 10.284 | 0 | 0 | -0.0001 | 10.2839 | `CARD.LIFT` |
| 257 | 拦截 | Intercept | Colorless | 0 | Skill | 1 | 0 | 10.014 | 0 | 0 | -0.0001 | 10.0139 | `CARD.INTERCEPT` |
| 258 | 群起攻之 | Gang Up | Colorless | 0 | Attack | 1 | 0 | 10 | 0 | 0 | -0.0001 | 9.9999 | `CARD.GANG_UP` |
| 259 | 秘密技法 | Secret Technique | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 9.1 | 0 | 9.1 | `CARD.SECRET_TECHNIQUE+1` |
| 260 | 秘密武器 | Secret Weapon | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 9.1 | 0 | 9.1 | `CARD.SECRET_WEAPON+1` |
| 261 | 如此甚好 | Make It So | Regent | 1 | Attack | 0 | 0 | 9 | 0 | 0 | 0 | 9 | `CARD.MAKE_IT_SO+1` |
| 262 | 地形改造 | Terraforming | Regent | 1 | Skill | 1 | 0 | 0 | 0 | 8.7 | -0.0001 | 8.6999 | `CARD.TERRAFORMING+1` |
| 263 | 星尘 | Stardust | Regent | 0 | Attack | 0 | 0 | 4.25 | 0 | 4.2 | 0 | 8.45 | `CARD.STARDUST` |
| 264 | 花样百出 | Jack of All Trades | Colorless | 0 | Skill | 0 | 0 | 0 | 0 | 8.4 | 0.003 | 8.403 | `CARD.JACK_OF_ALL_TRADES` |
| 265 | 花样百出 | Jack of All Trades | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 8.4 | 0.003 | 8.403 | `CARD.JACK_OF_ALL_TRADES+1` |
| 266 | 共鸣 | Resonance | Regent | 1 | Skill | 1 | 3 | 0 | 0 | 8.4 | -0.0003 | 8.3998 | `CARD.RESONANCE+1` |
| 267 | 协同配合 | Coordinate | Colorless | 0 | Skill | 1 | 0 | 8 | 0 | 0 | -0.0001 | 7.9999 | `CARD.COORDINATE` |
| 268 | 下去！ | BEGONE! | Regent | 1 | Skill | 1 | 0 | 0 | 0 | 7.9 | -0.0001 | 7.8999 | `CARD.BEGONE+1` |
| 269 | 宇宙冷漠 | Cosmic Indifference | Regent | 0 | Skill | 1 | 0 | 5.61 | 0 | 1.3 | -0.0001 | 6.9099 | `CARD.COSMIC_INDIFFERENCE` |
| 270 | 地形改造 | Terraforming | Regent | 0 | Skill | 1 | 0 | 0 | 0 | 6.5 | -0.0001 | 6.4999 | `CARD.TERRAFORMING` |
| 271 | 如此甚好 | Make It So | Regent | 0 | Attack | 0 | 0 | 6 | 0 | 0 | 0 | 6 | `CARD.MAKE_IT_SO` |
| 272 | 净化 | Purity | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 5.6 | 0.005 | 5.605 | `CARD.PURITY+1` |
| 273 | 辐射 | Radiate | Regent | 1 | Attack | 0 | 0 | 0 | 0 | 5.2 | 0 | 5.2 | `CARD.RADIATE+1` |
| 274 | 下去！ | BEGONE! | Regent | 0 | Skill | 1 | 0 | 0 | 0 | 5.2 | -0.0001 | 5.1999 | `CARD.BEGONE` |
| 275 | 净化 | Purity | Colorless | 0 | Skill | 0 | 0 | 0 | 0 | 5.1 | 0.005 | 5.105 | `CARD.PURITY` |
| 276 | 月面射击 | Lunar Blast | Regent | 1 | Attack | 0 | 0 | 0 | 0 | 5 | 0 | 5 | `CARD.LUNAR_BLAST+1` |
| 277 | 流星锤 | Bolas | Colorless | 1 | Attack | 0 | 0 | 4 | 0 | 0 | 0 | 4 | `CARD.BOLAS+1` |
| 278 | 月面射击 | Lunar Blast | Regent | 0 | Attack | 0 | 0 | 0 | 0 | 4 | 0 | 4 | `CARD.LUNAR_BLAST` |
| 279 | 辐射 | Radiate | Regent | 0 | Attack | 0 | 0 | 0 | 0 | 3.9 | 0 | 3.9 | `CARD.RADIATE` |
| 280 | 流星锤 | Bolas | Colorless | 0 | Attack | 0 | 0 | 3 | 0 | 0 | 0 | 3 | `CARD.BOLAS` |
| 281 | 发现 | Discovery | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 2.1 | 0.0029 | 2.1029 | `CARD.DISCOVERY` |
| 282 | 类星体 | Quasar | Regent | 0 | Skill | 0 | 2 | 0 | 0 | 1.2 | -0.0001 | 1.1999 | `CARD.QUASAR` |
| 283 | 天选 | Anointed | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0049 | 0.0049 | `CARD.ANOINTED+1` |
| 284 | 潦草急就 | Scrawl | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0049 | 0.0049 | `CARD.SCRAWL+1` |
| 285 | 炼制药水 | Alchemize | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 0 | 0.003 | 0.003 | `CARD.ALCHEMIZE+1` |
| 286 | 延伸 | Prolong | Colorless | 0 | Skill | 0 | 0 | 0 | 0 | 0 | 0.003 | 0.003 | `CARD.PROLONG` |
| 287 | 炼制药水 | Alchemize | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0029 | 0.0029 | `CARD.ALCHEMIZE` |
| 288 | 天选 | Anointed | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0029 | 0.0029 | `CARD.ANOINTED` |
| 289 | 拟态 | Mimic | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0029 | 0.0029 | `CARD.MIMIC` |
| 290 | 潦草急就 | Scrawl | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | 0.0029 | 0.0029 | `CARD.SCRAWL` |
| 291 | 慷慨捐助 | Largesse | Regent | 0 | Skill | 0 | 0 | 0 | 0 | 0 | 0 | 0 | `CARD.LARGESSE` |
| 292 | 慷慨捐助 | Largesse | Regent | 1 | Skill | 0 | 0 | 0 | 0 | 0 | 0 | 0 | `CARD.LARGESSE+1` |
| 293 | 延伸 | Prolong | Colorless | 1 | Skill | 0 | 0 | 0 | 0 | 0 | 0 | 0 | `CARD.PROLONG+1` |
| 294 | 发现 | Discovery | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.DISCOVERY+1` |
| 295 | 未掘宝石 | Hidden Gem | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.HIDDEN_GEM` |
| 296 | 未掘宝石 | Hidden Gem | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.HIDDEN_GEM+1` |
| 297 | 拟态 | Mimic | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.MIMIC+1` |
| 298 | 飞溅 | Splash | Colorless | 0 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.SPLASH` |
| 299 | 飞溅 | Splash | Colorless | 1 | Skill | 1 | 0 | 0 | 0 | 0 | -0.0001 | -0.0001 | `CARD.SPLASH+1` |
| 300 | 横祸 | Catastrophe | Colorless | 0 | Skill | 2 | 0 | 0 | 0 | 0 | -0.0002 | -0.0002 | `CARD.CATASTROPHE` |
| 301 | 横祸 | Catastrophe | Colorless | 1 | Skill | 2 | 0 | 0 | 0 | 0 | -0.0002 | -0.0002 | `CARD.CATASTROPHE+1` |
| 302 | 共鸣 | Resonance | Regent | 0 | Skill | 1 | 3 | 0 | 0 | 0 | -0.0003 | -0.0003 | `CARD.RESONANCE` |
| 303 | 狠揍 | Beat Down | Colorless | 0 | Skill | 3 | 0 | 0 | 0 | 0 | -0.0003 | -0.0003 | `CARD.BEAT_DOWN` |
| 304 | 狠揍 | Beat Down | Colorless | 1 | Skill | 3 | 0 | 0 | 0 | 0 | -0.0003 | -0.0003 | `CARD.BEAT_DOWN+1` |
