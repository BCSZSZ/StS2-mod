# Card Play Value Group Review

Purpose: draft manual-review tags for replacing deck-delta training values with play-value-oriented estimates.

Sources:
- `CardValueOverlay/data/card_values.json` for the current 152-card value set.
- `data/extracted/card_facts.generated.json` for card type and extracted actions.
- `history-analysis/data/localized_names_en_zhs.json` for English and Chinese names.

Multiplayer filter: excluded 6 cards whose card/action `targetType` contains `Ally` / `AnyAlly`.

Draft rule: damage/block/weak/vulnerable/hp loss and numeric persistent payoff are numeric; draw, energy, stars, forge, card generation, pile movement, transform, retain, and unresolved card-selection effects are flow. `starCost` is treated as a play condition, not a payoff category.

| Group | Count | Intended handling |
|---|---:|---|
| 非能力-纯数值 | 52 | Average per direct play: play value plus attribution. |
| 非能力-纯运转 | 38 | Average per direct play: attribution, with flow-value estimate when attribution is incomplete. |
| 非能力-混合 | 24 | Average per direct play: play value plus flow-value estimate. |
| 能力-纯数值 | 14 | Per combat after played: cumulative attribution through simulation horizon. |
| 能力-纯运转 | 14 | Per combat after played: attribution, with flow-value estimate when attribution is incomplete. |
| 能力-混合 | 4 | Per combat after played: cumulative numeric attribution plus flow-value estimate. |
| Excluded multiplayer | 6 | Removed from the six play-value groups before review. |

## 非能力-纯数值 (52)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.ASTRAL_PULSE` | Astral Pulse | 星界脉冲 | Attack | 非能力-纯数值 | damage |
| `CARD.BOMBARDMENT` | Bombardment | 轰击 | Attack | 非能力-纯数值 | damage |
| `CARD.CELESTIAL_MIGHT` | Celestial Might | 天穹之力 | Attack | 非能力-纯数值 | damage |
| `CARD.CLOAK_OF_STARS` | Cloak of Stars | 群星斗篷 | Skill | 非能力-纯数值 | block |
| `CARD.COMET` | Comet | 彗星 | Attack | 非能力-纯数值 | damage, debuffVulnerable, debuffWeak |
| `CARD.CRASH_LANDING` | Crash Landing | 迫降 | Attack | 非能力-纯数值 | damage |
| `CARD.CRESCENT_SPEAR` | Crescent Spear | 新月长矛 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.CRUSH_UNDER` | Crush Under | 下砸 | Attack | 非能力-纯数值 | damage, power:CrushUnder |
| `CARD.DARK_SHACKLES` | Dark Shackles | 黑暗镣铐 | Skill | 非能力-纯数值 | power:DarkShackles |
| `CARD.DEFEND_REGENT` | Defend | 防御 | Skill | 非能力-纯数值 | block |
| `CARD.DEVASTATE` | Devastate | 葬送 | Attack | 非能力-纯数值 | damage |
| `CARD.DRAMATIC_ENTRANCE` | Dramatic Entrance | 闪亮登场 | Attack | 非能力-纯数值 | damage |
| `CARD.DYING_STAR` | Dying Star | 星灭 | Attack | 非能力-纯数值 | damage, power:DyingStar |
| `CARD.FALLING_STAR` | Falling Star | 陨星 | Attack | 非能力-纯数值 | damage, debuffVulnerable, debuffWeak |
| `CARD.FISTICUFFS` | Fisticuffs | 拳斗 | Attack | 非能力-纯数值 | damage |
| `CARD.GAMMA_BLAST` | Gamma Blast | 伽马爆破 | Attack | 非能力-纯数值 | damage, debuffVulnerable, debuffWeak |
| `CARD.GANG_UP` | Gang Up | 群起攻之 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.GLITTERSTREAM` | Glitterstream | 流光溢彩 | Skill | 非能力-纯数值 | block, blockNextTurn |
| `CARD.GOLD_AXE` | Gold Axe | 金斧 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.HAND_OF_GREED` | Hand of Greed | 贪婪之手 | Attack | 非能力-纯数值 | damage |
| `CARD.HEAVENLY_DRILL` | Heavenly Drill | 天际钻头 | Attack | 非能力-纯数值 | xCostDamage |
| `CARD.I_AM_INVINCIBLE` | I Am Invincible | 所向无敌 | Skill | 非能力-纯数值 | block |
| `CARD.KINGLY_KICK` | Kingly Kick | 王者之踢 | Attack | 非能力-纯数值 | damage |
| `CARD.KINGLY_PUNCH` | Kingly Punch | 王者之拳 | Attack | 非能力-纯数值 | damage |
| `CARD.KNOCKDOWN` | Knockdown | 击倒 | Attack | 非能力-纯数值 | damage, power:Knockdown |
| `CARD.KNOW_THY_PLACE` | Know Thy Place | 何人僭越 | Skill | 非能力-纯数值 | debuffVulnerable, debuffWeak |
| `CARD.LUNAR_BLAST` | Lunar Blast | 月面射击 | Attack | 非能力-纯数值 | damage |
| `CARD.METEOR_SHOWER` | Meteor Shower | 流星雨 | Attack | 非能力-纯数值 | damage, debuffVulnerable, debuffWeak |
| `CARD.MIND_BLAST` | Mind Blast | 心灵震慑 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.MONOLOGUE` | Monologue | 独白 | Skill | 非能力-纯数值 | power:Monologue |
| `CARD.OMNISLICE` | Omnislice | 万向斩 | Attack | 非能力-纯数值 | damage |
| `CARD.PANIC_BUTTON` | Panic Button | 应急按钮 | Skill | 非能力-纯数值 | block, power:NoBlock |
| `CARD.PARTICLE_WALL` | Particle Wall | 粒子墙 | Skill | 非能力-纯数值 | block |
| `CARD.PATTER` | Patter | 星星点点 | Skill | 非能力-纯数值 | block, power:Vigor |
| `CARD.PROLONG` | Prolong | 延伸 | Skill | 非能力-纯数值 | blockNextTurn |
| `CARD.RADIATE` | Radiate | 辐射 | Attack | 非能力-纯数值 | damage |
| `CARD.RALLY` | Rally | 集结 | Skill | 非能力-纯数值 | block |
| `CARD.REFLECT` | Reflect | 倒映 | Skill | 非能力-纯数值 | block, power:Reflect |
| `CARD.REND` | Rend | 撕碎 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.RESONANCE` | Resonance | 共鸣 | Skill | 非能力-纯数值 | power:Strength |
| `CARD.SEVEN_STARS` | Seven Stars | 七星 | Attack | 非能力-纯数值 | damage |
| `CARD.SHOCKWAVE` | Shockwave | 震荡波 | Skill | 非能力-纯数值 | debuffVulnerable, debuffWeak |
| `CARD.STARDUST` | Stardust | 星尘 | Attack | 非能力-纯数值 | damage |
| `CARD.STRIKE_REGENT` | Strike | 打击 | Attack | 非能力-纯数值 | damage |
| `CARD.SUPERMASSIVE` | Supermassive | 超质量体 | Attack | 非能力-纯数值 | damage, scalingDamage |
| `CARD.TAG_TEAM` | Tag Team | 双打组合 | Attack | 非能力-纯数值 | damage, power:TagTeam |
| `CARD.TERRAFORMING` | Terraforming | 地形改造 | Skill | 非能力-纯数值 | power:Vigor |
| `CARD.THE_BOMB` | The Bomb | 炸弹 | Skill | 非能力-纯数值 | power:TheBomb |
| `CARD.THE_GAMBIT` | The Gambit | 孤注一掷 | Skill | 非能力-纯数值 | block, power:TheGambit |
| `CARD.ULTIMATE_DEFEND` | Ultimate Defend | 究极防御 | Skill | 非能力-纯数值 | block |
| `CARD.ULTIMATE_STRIKE` | Ultimate Strike | 究极打击 | Attack | 非能力-纯数值 | damage |
| `CARD.VOLLEY` | Volley | 连射 | Attack | 非能力-纯数值 | xCostDamage |

## 非能力-纯运转 (38)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.ALCHEMIZE` | Alchemize | 炼制药水 | Skill | 非能力-纯运转 | manual: potion / generated resource |
| `CARD.ALIGNMENT` | Alignment | 星位序列 | Skill | 非能力-纯运转 | energyGain |
| `CARD.ANOINTED` | Anointed | 天选 | Skill | 非能力-纯运转 | moveCardBetweenPiles |
| `CARD.BEAT_DOWN` | Beat Down | 狠揍 | Skill | 非能力-纯运转 | manual: play/handle cards from draw pile |
| `CARD.BEGONE` | BEGONE! | 下去！ | Skill | 非能力-纯运转 | selectCards, transformCard |
| `CARD.BIG_BANG` | Big Bang | 大爆炸 | Skill | 非能力-纯运转 | draw, energyGain, forge, starGain |
| `CARD.BUNDLE_OF_JOY` | Bundle of Joy | 新生之喜 | Skill | 非能力-纯运转 | createCard |
| `CARD.CATASTROPHE` | Catastrophe | 横祸 | Skill | 非能力-纯运转 | manual: play/handle cards from draw pile |
| `CARD.CHARGE` | CHARGE!! | 冲锋！！ | Skill | 非能力-纯运转 | selectCards, transformCard |
| `CARD.CONVERGENCE` | Convergence | 汇流 | Skill | 非能力-纯运转 | energyNextTurn, power:RetainHand, starNextTurn |
| `CARD.DECISIONS_DECISIONS` | Decisions, Decisions | 抉择，抉择 | Skill | 非能力-纯运转 | draw, selectCards |
| `CARD.DISCOVERY` | Discovery | 发现 | Skill | 非能力-纯运转 | createCard, selectCards |
| `CARD.FOREGONE_CONCLUSION` | Foregone Conclusion | 既定事项 | Skill | 非能力-纯运转 | power:ForegoneConclusion |
| `CARD.GLIMMER` | Glimmer | 微光 | Skill | 非能力-纯运转 | draw, moveCardBetweenPiles, selectCards |
| `CARD.GLOW` | Glow | 辉光 | Skill | 非能力-纯运转 | draw, drawNextTurn, starGain |
| `CARD.GUARDS` | GUARDS!!! | 护驾！！！ | Skill | 非能力-纯运转 | selectCards, transformCard |
| `CARD.HIDDEN_CACHE` | Hidden Cache | 隐秘藏品 | Skill | 非能力-纯运转 | starGain, starNextTurn |
| `CARD.HIDDEN_GEM` | Hidden Gem | 未掘宝石 | Skill | 非能力-纯运转 | manual: add Replay to draw-pile card |
| `CARD.HUDDLE_UP` | Huddle Up | 抱团 | Skill | 非能力-纯运转 | draw |
| `CARD.IMPATIENCE` | Impatience | 急躁 | Skill | 非能力-纯运转 | draw |
| `CARD.JACK_OF_ALL_TRADES` | Jack of All Trades | 花样百出 | Skill | 非能力-纯运转 | createCard |
| `CARD.MASTER_OF_STRATEGY` | Master of Strategy | 战略大师 | Skill | 非能力-纯运转 | draw |
| `CARD.PRODUCTION` | Production | 生产制造 | Skill | 非能力-纯运转 | energyGain |
| `CARD.PROPHESIZE` | Prophesize | 预言 | Skill | 非能力-纯运转 | draw |
| `CARD.PURITY` | Purity | 净化 | Skill | 非能力-纯运转 | moveCardBetweenPiles, selectCards |
| `CARD.QUASAR` | Quasar | 类星体 | Skill | 非能力-纯运转 | createCard, createCardChoices, selectCards |
| `CARD.REFINE_BLADE` | Refine Blade | 淬炼刀刃 | Skill | 非能力-纯运转 | energyNextTurn, forge |
| `CARD.RESTLESSNESS` | Restlessness | 心神不宁 | Skill | 非能力-纯运转 | draw, energyGain |
| `CARD.ROYAL_GAMBLE` | Royal Gamble | 胜券在王 | Skill | 非能力-纯运转 | starGain |
| `CARD.SCRAWL` | Scrawl | 潦草急就 | Skill | 非能力-纯运转 | manual: draw to full hand |
| `CARD.SECRET_TECHNIQUE` | Secret Technique | 秘密技法 | Skill | 非能力-纯运转 | moveCardBetweenPiles, selectCards |
| `CARD.SECRET_WEAPON` | Secret Weapon | 秘密武器 | Skill | 非能力-纯运转 | moveCardBetweenPiles, selectCards |
| `CARD.SPLASH` | Splash | 飞溅 | Skill | 非能力-纯运转 | createCard, selectCards |
| `CARD.SPOILS_OF_BATTLE` | Spoils of Battle | 战利品 | Skill | 非能力-纯运转 | draw, forge |
| `CARD.SUMMON_FORTH` | Summon Forth | 征召上前 | Skill | 非能力-纯运转 | forge, moveCardBetweenPiles |
| `CARD.THE_SMITH` | The Smith | 铸剑者 | Skill | 非能力-纯运转 | forge |
| `CARD.THINKING_AHEAD` | Thinking Ahead | 深谋远虑 | Skill | 非能力-纯运转 | draw, moveCardBetweenPiles, selectCards |
| `CARD.VENERATE` | Venerate | 崇拜 | Skill | 非能力-纯运转 | starGain |

## 非能力-混合 (24)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.BEAT_INTO_SHAPE` | Beat into Shape | 锻打成型 | Attack | 非能力-混合 | damage, forge |
| `CARD.BOLAS` | Bolas | 流星锤 | Attack | 非能力-混合 | damage, moveCardBetweenPiles |
| `CARD.BULWARK` | Bulwark | 铸墙 | Skill | 非能力-混合 | block, forge |
| `CARD.COLLISION_COURSE` | Collision Course | 碰撞轨迹 | Attack | 非能力-混合 | createCard, damage |
| `CARD.CONQUEROR` | Conqueror | 征服者 | Skill | 非能力-混合 | forge, power:Conqueror |
| `CARD.COSMIC_INDIFFERENCE` | Cosmic Indifference | 宇宙冷漠 | Skill | 非能力-混合 | block, moveCardBetweenPiles, selectCards |
| `CARD.EQUILIBRIUM` | Equilibrium | 均衡 | Skill | 非能力-混合 | block, power:RetainHand |
| `CARD.FINESSE` | Finesse | 妙计 | Skill | 非能力-混合 | block, draw |
| `CARD.FLASH_OF_STEEL` | Flash of Steel | 亮剑 | Attack | 非能力-混合 | damage, draw |
| `CARD.GATHER_LIGHT` | Gather Light | 收集光辉 | Skill | 非能力-混合 | block, starGain |
| `CARD.GUIDING_STAR` | Guiding Star | 引导之星 | Attack | 非能力-混合 | damage, draw |
| `CARD.HEGEMONY` | Hegemony | 霸权 | Attack | 非能力-混合 | damage, energyNextTurn |
| `CARD.HEIRLOOM_HAMMER` | Heirloom Hammer | 传承之锤 | Attack | 非能力-混合 | createCard, damage, selectCards |
| `CARD.JACKPOT` | Jackpot | 大奖 | Attack | 非能力-混合 | createCard, damage |
| `CARD.KNOCKOUT_BLOW` | Knockout Blow | 决胜一击 | Attack | 非能力-混合 | damage, starGain |
| `CARD.MAKE_IT_SO` | Make It So | 如此甚好 | Attack | 非能力-混合 | damage, moveCardBetweenPiles |
| `CARD.MANIFEST_AUTHORITY` | Manifest Authority | 君权自授 | Skill | 非能力-混合 | block, createCard, createCardChoices |
| `CARD.PHOTON_CUT` | Photon Cut | 光子切割 | Attack | 非能力-混合 | damage, draw, moveCardBetweenPiles, selectCards |
| `CARD.SALVO` | Salvo | 箭雨 | Attack | 非能力-混合 | damage, power:RetainHand |
| `CARD.SEEKER_STRIKE` | Seeker Strike | 探寻打击 | Attack | 非能力-混合 | damage, moveCardBetweenPiles, selectCards |
| `CARD.SHINING_STRIKE` | Shining Strike | 明耀打击 | Attack | 非能力-混合 | damage, moveCardBetweenPiles, starGain |
| `CARD.SOLAR_STRIKE` | Solar Strike | 太阳打击 | Attack | 非能力-混合 | damage, starGain |
| `CARD.THRUMMING_HATCHET` | Thrumming Hatchet | 无休手斧 | Attack | 非能力-混合 | damage, moveCardBetweenPiles |
| `CARD.WROUGHT_IN_WAR` | Wrought in War | 战火铸就 | Attack | 非能力-混合 | damage, forge |

## 能力-纯数值 (14)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.BEACON_OF_HOPE` | Beacon of Hope | 希望灯塔 | Power | 能力-纯数值 | power:BeaconOfHope |
| `CARD.ETERNAL_ARMOR` | Eternal Armor | 永恒铠甲 | Power | 能力-纯数值 | power:Plating |
| `CARD.FASTEN` | Fasten | 勒紧 | Power | 能力-纯数值 | power:Fasten |
| `CARD.HAMMER_TIME` | Hammer Time | 锤子时间 | Power | 能力-纯数值 | power:HammerTime |
| `CARD.MAYHEM` | Mayhem | 乱战 | Power | 能力-纯数值 | power:Mayhem |
| `CARD.MONARCHS_GAZE` | Monarch's Gaze | 王之凝视 | Power | 能力-纯数值 | power:MonarchsGaze |
| `CARD.NEUTRON_AEGIS` | Neutron Aegis | 中子护盾 | Power | 能力-纯数值 | power:Plating |
| `CARD.NOSTALGIA` | Nostalgia | 怀旧 | Power | 能力-纯数值 | power:Nostalgia |
| `CARD.PANACHE` | Panache | 神气制胜 | Power | 能力-纯数值 | power:Panache |
| `CARD.PARRY` | Parry | 招架 | Power | 能力-纯数值 | power:Parry |
| `CARD.PROWESS` | Prowess | 非凡技艺 | Power | 能力-纯数值 | power:Dexterity, power:Strength |
| `CARD.ROLLING_BOULDER` | Rolling Boulder | 滚石 | Power | 能力-纯数值 | power:RollingBoulder |
| `CARD.ROYALTIES` | Royalties | 王国资产 | Power | 能力-纯数值 | power:Royalties |
| `CARD.SWORD_SAGE` | Sword Sage | 剑圣 | Power | 能力-纯数值 | power:SwordSage |

## 能力-纯运转 (14)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.ARSENAL` | Arsenal | 武器库 | Power | 能力-纯运转 | power:Arsenal |
| `CARD.AUTOMATION` | Automation | 自动化 | Power | 能力-纯运转 | power:Automation |
| `CARD.CALAMITY` | Calamity | 劫难 | Power | 能力-纯运转 | power:Calamity |
| `CARD.ENTROPY` | Entropy | 熵 | Power | 能力-纯运转 | power:Entropy |
| `CARD.FURNACE` | Furnace | 熔炉 | Power | 能力-纯运转 | power:Furnace |
| `CARD.GENESIS` | Genesis | 创世纪 | Power | 能力-纯运转 | power:Genesis |
| `CARD.ORBIT` | Orbit | 环绕轨道 | Power | 能力-纯运转 | power:Orbit |
| `CARD.PALE_BLUE_DOT` | Pale Blue Dot | 暗淡蓝点 | Power | 能力-纯运转 | power:PaleBlueDot |
| `CARD.PREP_TIME` | Prep Time | 准备时间 | Power | 能力-纯运转 | power:PrepTime |
| `CARD.SPECTRUM_SHIFT` | Spectrum Shift | 光谱偏移 | Power | 能力-纯运转 | power:SpectrumShift |
| `CARD.STRATAGEM` | Stratagem | 计策 | Power | 能力-纯运转 | power:Stratagem |
| `CARD.THE_SEALED_THRONE` | The Sealed Throne | 封印王座 | Power | 能力-纯运转 | power:TheSealedThrone |
| `CARD.TYRANNY` | Tyranny | 暴政 | Power | 能力-纯运转 | power:Tyranny |
| `CARD.VOID_FORM` | Void Form | 虚空形态 | Power | 能力-纯运转 | power:VoidForm |

## 能力-混合 (4)

| Model ID | English | 中文 | Card type | 分组结果 | 初分依据 |
|---|---|---|---|---|---|
| `CARD.BLACK_HOLE` | Black Hole | 黑洞 | Power | 能力-混合 | persistentPowerTrigger, power:BlackHole |
| `CARD.CHILD_OF_THE_STARS` | Child of the Stars | 群星之子 | Power | 能力-混合 | persistentPowerTrigger, power:ChildOfTheStars |
| `CARD.PILLAR_OF_CREATION` | Pillar of Creation | 创世之柱 | Power | 能力-混合 | block, power:PillarOfCreation |
| `CARD.SEEKING_EDGE` | Seeking Edge | 追踪之刃 | Power | 能力-混合 | forge, power:SeekingEdge |

## Excluded Multiplayer Cards (6)

| Model ID | English | 中文 | Card type | Exclusion reason |
|---|---|---|---|---|
| `CARD.BELIEVE_IN_YOU` | Believe in You | 相信着你 | Skill | targetType contains Ally / AnyAlly |
| `CARD.COORDINATE` | Coordinate | 协同配合 | Skill | targetType contains Ally / AnyAlly |
| `CARD.INTERCEPT` | Intercept | 拦截 | Skill | targetType contains Ally / AnyAlly |
| `CARD.LARGESSE` | Largesse | 慷慨捐助 | Skill | targetType contains Ally / AnyAlly |
| `CARD.LIFT` | Lift | 托举 | Skill | targetType contains Ally / AnyAlly |
| `CARD.MIMIC` | Mimic | 拟态 | Skill | targetType contains Ally / AnyAlly |
