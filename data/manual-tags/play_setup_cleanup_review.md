# Play Setup Cleanup Review

Scope: play setup only. Beam setup is intentionally preserved except the existing code-level beam visibility for TheBomb/Monologue. Power cards resolve to play setup 99 by floor. Cosmic Indifference uses a runtime dynamic addend, because deck max play value is state-dependent.

## Special Terms With PlaySetup 0

| 中文名 | English | ModelId | Type | Special terms |
| --- | --- | --- | --- | --- |
| 炼制药水 | Alchemize | `CARD.ALCHEMIZE` | Skill | createPotion(1):source:combatPotion |
| 炼制药水 | Alchemize | `CARD.ALCHEMIZE+1` | Skill | createPotion(1):source:combatPotion |
| 天选 | Anointed | `CARD.ANOINTED` | Skill | moveCardBetweenPiles:to:Hand |
| 天选 | Anointed | `CARD.ANOINTED+1` | Skill | moveCardBetweenPiles:to:Hand |
| 狠揍 | Beat Down | `CARD.BEAT_DOWN` | Skill | autoPlay:requiresManualDescriptor |
| 狠揍 | Beat Down | `CARD.BEAT_DOWN+1` | Skill | autoPlay:requiresManualDescriptor |
| 流星锤 | Bolas | `CARD.BOLAS` | Attack | selfReturn:to:Hand |
| 流星锤 | Bolas | `CARD.BOLAS+1` | Attack | selfReturn:to:Hand |
| 轰击 | Bombardment | `CARD.BOMBARDMENT` | Attack | autoPlay:requiresManualDescriptor |
| 轰击 | Bombardment | `CARD.BOMBARDMENT+1` | Attack | autoPlay:requiresManualDescriptor |
| 新生之喜 | Bundle of Joy | `CARD.BUNDLE_OF_JOY` | Skill | createCard(1):source:item;pile:Hand |
| 新生之喜 | Bundle of Joy | `CARD.BUNDLE_OF_JOY+1` | Skill | createCard(1):source:item;pile:Hand |
| 横祸 | Catastrophe | `CARD.CATASTROPHE` | Skill | autoPlay:requiresManualDescriptor |
| 横祸 | Catastrophe | `CARD.CATASTROPHE+1` | Skill | autoPlay:requiresManualDescriptor |
| 碰撞轨迹 | Collision Course | `CARD.COLLISION_COURSE` | Attack | createCard(1):card:Debris;pile:Hand<br>createCard(1):source:base;pile:Hand |
| 碰撞轨迹 | Collision Course | `CARD.COLLISION_COURSE+1` | Attack | createCard(1):card:Debris;pile:Hand<br>createCard(1):source:base;pile:Hand |
| 汇流 | Convergence | `CARD.CONVERGENCE` | Skill | energyNextTurn(1)<br>starNextTurn(1)<br>power(1):power:RetainHand;var:RetainHand |
| 汇流 | Convergence | `CARD.CONVERGENCE+1` | Skill | energyNextTurn(1)<br>starNextTurn(2)<br>power(1):power:RetainHand;var:RetainHand |
| 协同配合 | Coordinate | `CARD.COORDINATE` | Skill | power(5):power:Coordinate;var:Strength |
| 协同配合 | Coordinate | `CARD.COORDINATE+1` | Skill | power(8):power:Coordinate;var:Strength |
| 下砸 | Crush Under | `CARD.CRUSH_UNDER` | Attack | power(1):power:CrushUnder;var:StrengthLoss |
| 下砸 | Crush Under | `CARD.CRUSH_UNDER+1` | Attack | power(2):power:CrushUnder;var:StrengthLoss |
| 黑暗镣铐 | Dark Shackles | `CARD.DARK_SHACKLES` | Skill | power(9):power:DarkShackles;var:StrengthLoss |
| 黑暗镣铐 | Dark Shackles | `CARD.DARK_SHACKLES+1` | Skill | power(15):power:DarkShackles;var:StrengthLoss |
| 抉择，抉择 | Decisions, Decisions | `CARD.DECISIONS_DECISIONS` | Skill | selectCards(1):from:Hand<br>autoPlay:requiresManualDescriptor |
| 抉择，抉择 | Decisions, Decisions | `CARD.DECISIONS_DECISIONS+1` | Skill | selectCards(1):from:Hand<br>autoPlay:requiresManualDescriptor |
| 发现 | Discovery | `CARD.DISCOVERY` | Skill | selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 发现 | Discovery | `CARD.DISCOVERY+1` | Skill | selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 星灭 | Dying Star | `CARD.DYING_STAR` | Attack | power(9):power:DyingStar;var:StrengthLoss |
| 星灭 | Dying Star | `CARD.DYING_STAR+1` | Attack | power(11):power:DyingStar;var:StrengthLoss |
| 均衡 | Equilibrium | `CARD.EQUILIBRIUM` | Skill | power(1):power:RetainHand;var:Equilibrium |
| 均衡 | Equilibrium | `CARD.EQUILIBRIUM+1` | Skill | power(1):power:RetainHand;var:Equilibrium |
| 既定事项 | Foregone Conclusion | `CARD.FOREGONE_CONCLUSION` | Skill | power(2):power:ForegoneConclusion;var:Cards |
| 既定事项 | Foregone Conclusion | `CARD.FOREGONE_CONCLUSION+1` | Skill | power(3):power:ForegoneConclusion;var:Cards |
| 护驾！！！ | GUARDS!!! | `CARD.GUARDS` | Skill | selectCards(0):from:Hand<br>transformCard(0):from:Hand;card:SIM.TRANSFORMED_CARD |
| 护驾！！！ | GUARDS!!! | `CARD.GUARDS+1` | Skill | selectCards(0):from:Hand<br>transformCard(0):from:Hand;card:SIM.TRANSFORMED_CARD |
| 微光 | Glimmer | `CARD.GLIMMER` | Skill | selectCards(1):from:Hand<br>moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top |
| 微光 | Glimmer | `CARD.GLIMMER+1` | Skill | selectCards(1):from:Hand<br>moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top |
| 流光溢彩 | Glitterstream | `CARD.GLITTERSTREAM` | Skill | blockNextTurn(5):power:BlockNextTurn |
| 流光溢彩 | Glitterstream | `CARD.GLITTERSTREAM+1` | Skill | blockNextTurn(7):power:BlockNextTurn |
| 辉光 | Glow | `CARD.GLOW` | Skill | drawNextTurn(1) |
| 辉光 | Glow | `CARD.GLOW+1` | Skill | drawNextTurn(1) |
| 霸权 | Hegemony | `CARD.HEGEMONY` | Attack | energyNextTurn(2) |
| 霸权 | Hegemony | `CARD.HEGEMONY+1` | Attack | energyNextTurn(3) |
| 传承之锤 | Heirloom Hammer | `CARD.HEIRLOOM_HAMMER` | Attack | selectCards(1):from:Hand<br>createCard(1):source:card;pile:Hand |
| 传承之锤 | Heirloom Hammer | `CARD.HEIRLOOM_HAMMER+1` | Attack | selectCards(1):from:Hand<br>createCard(1):source:card;pile:Hand |
| 隐秘藏品 | Hidden Cache | `CARD.HIDDEN_CACHE` | Skill | starNextTurn(3) |
| 隐秘藏品 | Hidden Cache | `CARD.HIDDEN_CACHE+1` | Skill | starNextTurn(4) |
| 未掘宝石 | Hidden Gem | `CARD.HIDDEN_GEM` | Skill | grantReplay(2):target:drawPile |
| 未掘宝石 | Hidden Gem | `CARD.HIDDEN_GEM+1` | Skill | grantReplay(3):target:drawPile |
| 拦截 | Intercept | `CARD.INTERCEPT` | Skill | power(1):power:Covered;var:Covered |
| 拦截 | Intercept | `CARD.INTERCEPT+1` | Skill | power(1):power:Covered;var:Covered |
| 花样百出 | Jack of All Trades | `CARD.JACK_OF_ALL_TRADES` | Skill | createCard(1):source:item;pile:Hand |
| 花样百出 | Jack of All Trades | `CARD.JACK_OF_ALL_TRADES+1` | Skill | createCard(1):source:item;pile:Hand |
| 大奖 | Jackpot | `CARD.JACKPOT` | Attack | createCard(1):source:item;pile:Hand |
| 大奖 | Jackpot | `CARD.JACKPOT+1` | Attack | createCard(1):source:item;pile:Hand |
| 击倒 | Knockdown | `CARD.KNOCKDOWN` | Attack | power(2):power:Knockdown;var:KnockdownPower |
| 击倒 | Knockdown | `CARD.KNOCKDOWN+1` | Attack | power(3):power:Knockdown;var:KnockdownPower |
| 慷慨捐助 | Largesse | `CARD.LARGESSE` | Skill | createCardChoices(1):pool:ColorlessCardPool;count:1<br>createCard(1):source:cardModel;pile:Hand |
| 慷慨捐助 | Largesse | `CARD.LARGESSE+1` | Skill | createCardChoices(1):pool:ColorlessCardPool;count:1<br>createCard(1):source:cardModel;pile:Hand |
| 如此甚好 | Make It So | `CARD.MAKE_IT_SO` | Attack | selfReturn:to:Hand |
| 如此甚好 | Make It So | `CARD.MAKE_IT_SO+1` | Attack | selfReturn:to:Hand |
| 君权自授 | Manifest Authority | `CARD.MANIFEST_AUTHORITY` | Skill | createCardChoices(1):pool:ColorlessCardPool;count:1<br>createCard(1):source:cardModel;pile:Hand |
| 君权自授 | Manifest Authority | `CARD.MANIFEST_AUTHORITY+1` | Skill | createCardChoices(1):pool:ColorlessCardPool;count:1<br>createCard(1):source:cardModel;pile:Hand |
| 独白 | Monologue | `CARD.MONOLOGUE` | Skill | power(1):power:Monologue;var:Monologue |
| 独白 | Monologue | `CARD.MONOLOGUE+1` | Skill | power(1):power:Monologue;var:Monologue |
| 应急按钮 | Panic Button | `CARD.PANIC_BUTTON` | Skill | power(2):power:NoBlock;var:Turns |
| 应急按钮 | Panic Button | `CARD.PANIC_BUTTON+1` | Skill | power(2):power:NoBlock;var:Turns |
| 星星点点 | Patter | `CARD.PATTER` | Skill | power(2):power:Vigor;var:VigorPower |
| 星星点点 | Patter | `CARD.PATTER+1` | Skill | power(3):power:Vigor;var:VigorPower |
| 光子切割 | Photon Cut | `CARD.PHOTON_CUT` | Attack | moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top<br>selectCards(1):from:Hand |
| 光子切割 | Photon Cut | `CARD.PHOTON_CUT+1` | Attack | moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top<br>selectCards(1):from:Hand |
| 延伸 | Prolong | `CARD.PROLONG` | Skill | blockNextTurn:power:BlockNextTurn |
| 延伸 | Prolong | `CARD.PROLONG+1` | Skill | blockNextTurn:power:BlockNextTurn |
| 净化 | Purity | `CARD.PURITY` | Skill | selectCards(3):from:Hand<br>moveCardBetweenPiles(3):from:Hand;to:Exhaust |
| 净化 | Purity | `CARD.PURITY+1` | Skill | selectCards(5):from:Hand<br>moveCardBetweenPiles(5):from:Hand;to:Exhaust |
| 类星体 | Quasar | `CARD.QUASAR` | Skill | createCardChoices(3):pool:ColorlessCardPool;count:3<br>selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 类星体 | Quasar | `CARD.QUASAR+1` | Skill | createCardChoices(3):pool:ColorlessCardPool;count:3<br>selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 倒映 | Reflect | `CARD.REFLECT` | Skill | power(1):power:Reflect;var:Reflect |
| 倒映 | Reflect | `CARD.REFLECT+1` | Skill | power(1):power:Reflect;var:Reflect |
| 共鸣 | Resonance | `CARD.RESONANCE` | Skill | power(1):power:Strength;var:StrengthPower<br>power(-1):power:Strength;var:Strength |
| 共鸣 | Resonance | `CARD.RESONANCE+1` | Skill | power(2):power:Strength;var:StrengthPower<br>power(-1):power:Strength;var:Strength |
| 箭雨 | Salvo | `CARD.SALVO` | Attack | power(1):power:RetainHand;var:RetainHand |
| 箭雨 | Salvo | `CARD.SALVO+1` | Attack | power(1):power:RetainHand;var:RetainHand |
| 秘密技法 | Secret Technique | `CARD.SECRET_TECHNIQUE` | Skill | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 秘密技法 | Secret Technique | `CARD.SECRET_TECHNIQUE+1` | Skill | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 秘密武器 | Secret Weapon | `CARD.SECRET_WEAPON` | Skill | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 秘密武器 | Secret Weapon | `CARD.SECRET_WEAPON+1` | Skill | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 探寻打击 | Seeker Strike | `CARD.SEEKER_STRIKE` | Attack | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 探寻打击 | Seeker Strike | `CARD.SEEKER_STRIKE+1` | Attack | selectCards(1):from:Draw<br>moveCardBetweenPiles(1):from:Draw;to:Hand |
| 明耀打击 | Shining Strike | `CARD.SHINING_STRIKE` | Attack | selfReturn:to:Draw;position:Top |
| 明耀打击 | Shining Strike | `CARD.SHINING_STRIKE+1` | Attack | selfReturn:to:Draw;position:Top |
| 飞溅 | Splash | `CARD.SPLASH` | Skill | selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 飞溅 | Splash | `CARD.SPLASH+1` | Skill | selectCards:screen:chooseACard<br>createCard(1):source:cardModel;pile:Hand |
| 双打组合 | Tag Team | `CARD.TAG_TEAM` | Attack | power(1):power:TagTeam;var:TagTeam |
| 双打组合 | Tag Team | `CARD.TAG_TEAM+1` | Attack | power(1):power:TagTeam;var:TagTeam |
| 地形改造 | Terraforming | `CARD.TERRAFORMING` | Skill | power(6):power:Vigor;var:VigorPower |
| 地形改造 | Terraforming | `CARD.TERRAFORMING+1` | Skill | power(8):power:Vigor;var:VigorPower |
| 炸弹 | The Bomb | `CARD.THE_BOMB` | Skill | power(3):power:TheBomb;var:Turns |
| 炸弹 | The Bomb | `CARD.THE_BOMB+1` | Skill | power(3):power:TheBomb;var:Turns |
| 孤注一掷 | The Gambit | `CARD.THE_GAMBIT` | Skill | power(1):power:TheGambit;var:TheGambit |
| 孤注一掷 | The Gambit | `CARD.THE_GAMBIT+1` | Skill | power(1):power:TheGambit;var:TheGambit |
| 深谋远虑 | Thinking Ahead | `CARD.THINKING_AHEAD` | Skill | selectCards(1):from:Hand<br>moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top |
| 深谋远虑 | Thinking Ahead | `CARD.THINKING_AHEAD+1` | Skill | selectCards(1):from:Hand<br>moveCardBetweenPiles(1):from:Hand;to:Draw;position:Top |
| 无休手斧 | Thrumming Hatchet | `CARD.THRUMMING_HATCHET` | Attack | selfReturn:to:Hand |
| 无休手斧 | Thrumming Hatchet | `CARD.THRUMMING_HATCHET+1` | Attack | selfReturn:to:Hand |

## Rule 2/3 Modified Objects

| 中文名 | English | ModelId | Type | Old PlaySetup | New PlaySetup | Rule | Component |
| --- | --- | --- | --- | ---: | --- | --- | --- |
| 宇宙冷漠 | Cosmic Indifference | `CARD.COSMIC_INDIFFERENCE` | Skill | 1.3 | 0 + 0.8 * maxDeckPlayValue | Rule 2: discard fetch / deck-top setup | runtime dynamic; static JSON play = 0 |
| 宇宙冷漠 | Cosmic Indifference | `CARD.COSMIC_INDIFFERENCE+1` | Skill | 4.1 | 0 + 0.8 * maxDeckPlayValue | Rule 2: discard fetch / deck-top setup | runtime dynamic; static JSON play = 0 |
| 下去！ | BEGONE! | `CARD.BEGONE` | Skill | 5.2 | 11.2 | Rule 2: fixed transform -> MinionStrike play value | MinionStrike = 11.2 |
| 下去！ | BEGONE! | `CARD.BEGONE+1` | Skill | 7.9 | 14.2 | Rule 2: fixed transform -> MinionStrike+1 play value | MinionStrike+1 = 14.2 |
| 冲锋！！ | CHARGE!! | `CARD.CHARGE` | Skill | 13.3 | 26 | Rule 2: transform 2 draw cards -> MinionDiveBomb | 2 * MinionDiveBomb = 26 |
| 冲锋！！ | CHARGE!! | `CARD.CHARGE+1` | Skill | 17.1 | 32 | Rule 2: transform 2 draw cards -> MinionDiveBomb+1 | 2 * MinionDiveBomb+1 = 32 |
| 锻打成型 | Beat into Shape | `CARD.BEAT_INTO_SHAPE` | Attack | 8.2 | 0 + 2 * dynamicForge | Rule 3: Forge * 2 | dynamicForge = baseDamage * attacksPlayedThisTurn |
| 锻打成型 | Beat into Shape | `CARD.BEAT_INTO_SHAPE+1` | Attack | 10.4 | 0 + 2 * dynamicForge | Rule 3: Forge * 2 | dynamicForge = baseDamage * attacksPlayedThisTurn |
| 大爆炸 | Big Bang | `CARD.BIG_BANG` | Skill | 46.6 | 10 | Rule 3: Forge * 2 | 5 * 2 = 10 |
| 大爆炸 | Big Bang | `CARD.BIG_BANG+1` | Skill | 46.5 | 10 | Rule 3: Forge * 2 | 5 * 2 = 10 |
| 铸墙 | Bulwark | `CARD.BULWARK` | Skill | 30.4 | 20 | Rule 3: Forge * 2 | 10 * 2 = 20 |
| 铸墙 | Bulwark | `CARD.BULWARK+1` | Skill | 39.3 | 26 | Rule 3: Forge * 2 | 13 * 2 = 26 |
| 征服者 | Conqueror | `CARD.CONQUEROR` | Skill | 25.9 | 6 | Rule 3: Forge * 2 | 3 * 2 = 6 |
| 征服者 | Conqueror | `CARD.CONQUEROR+1` | Skill | 31.4 | 10 | Rule 3: Forge * 2 | 5 * 2 = 10 |
| 淬炼刀刃 | Refine Blade | `CARD.REFINE_BLADE` | Skill | 23.2 | 18 | Rule 3: Forge * 2 | 9 * 2 = 18 |
| 淬炼刀刃 | Refine Blade | `CARD.REFINE_BLADE+1` | Skill | 30.2 | 26 | Rule 3: Forge * 2 | 13 * 2 = 26 |
| 战利品 | Spoils of Battle | `CARD.SPOILS_OF_BATTLE` | Skill | 6.5 | 10 | Rule 3: Forge * 2 | 5 * 2 = 10 |
| 战利品 | Spoils of Battle | `CARD.SPOILS_OF_BATTLE+1` | Skill | 10.4 | 16 | Rule 3: Forge * 2 | 8 * 2 = 16 |
| 征召上前 | Summon Forth | `CARD.SUMMON_FORTH` | Skill | 13.2 | 16 | Rule 3: Forge * 2 | 8 * 2 = 16 |
| 征召上前 | Summon Forth | `CARD.SUMMON_FORTH+1` | Skill | 17.5 | 22 | Rule 3: Forge * 2 | 11 * 2 = 22 |
| 铸剑者 | The Smith | `CARD.THE_SMITH` | Skill | 54.9 | 60 | Rule 3: Forge * 2 | 30 * 2 = 60 |
| 铸剑者 | The Smith | `CARD.THE_SMITH+1` | Skill | 74.6 | 80 | Rule 3: Forge * 2 | 40 * 2 = 80 |
| 战火铸就 | Wrought in War | `CARD.WROUGHT_IN_WAR` | Attack | 16.6 | 14 | Rule 3: Forge * 2 | 7 * 2 = 14 |
| 战火铸就 | Wrought in War | `CARD.WROUGHT_IN_WAR+1` | Attack | 21.6 | 18 | Rule 3: Forge * 2 | 9 * 2 = 18 |

## Notes

- `Begone` resolves `SIM.TRANSFORMED_CARD` to fixed `MinionStrike` in the simulator; upgraded Begone resolves to `MinionStrike+1` when available.
- `Charge` resolves to two `MinionDiveBomb` cards; upgraded Charge resolves to `MinionDiveBomb+1` when available.
- `BeatIntoShape` has calculated Forge, so its static JSON play setup remains 0 and the runtime adds `2 * dynamicForge`.
- `SeekingEdge` is a Power with Forge; play setup remains the Power floor 99 rather than adding Forge twice.
