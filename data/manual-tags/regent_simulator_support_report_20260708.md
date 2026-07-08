# Regent / Colorless Simulator Support Report

Generated: 2026-07-08

## Scope and Rule

- Primary scope: base cards whose card-pool membership includes `Regent` or `Colorless`.
- Excluded from the main table: multiplayer-only cards and cards/actions that reference `Ally`, matching the direct play-value candidate filter. They are listed at the end.
- Each base card is reported as two simulation forms: unupgraded `0` and upgraded `+1`.
- `SC` means simulatable and source-credit attributable. This is the safest direct value path.
- `PD` means simulatable by play-delta / deck EV delta, but source-credit attribution is incomplete. This covers draw, create-card, transform, pile movement, card selection, replay, auto-play, and play-delta-only powers.
- `NO` means the current simulator cannot reliably model the card under either source-credit or play-delta because it has an unsupported simulation action or unresolved calculated scaling.
- Estimator-only notes such as `No supported contribution was estimated for this card.` are not treated as simulator-unsupported by themselves.

Sources:

- `data/extracted/card_facts.generated.json`
- `data/extracted/card_pool_memberships.generated.json`
- `data/generated/simulation_card_library.generated.json`
- `history-analysis/data/localized_names_en_zhs.json`

Refresh command used before generating this report:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-card-resources --runs 1 --turns 1 --max-branch 1 --no-marginals
```

## Summary

| Scope | Base cards | Forms | SC forms | PD forms | NO forms | Missing forms | All-SC base cards | Any-PD base cards | Any-NO base cards | Missing base cards |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Regent + Colorless | 139 | 278 | 164 | 100 | 14 | 0 | 82 | 50 | 7 | 0 |
| Regent only | 86 | 172 | 118 | 50 | 4 | 0 | 59 | 25 | 2 | 0 |
| Colorless only | 53 | 106 | 46 | 50 | 10 | 0 | 23 | 25 | 5 | 0 |

## Dynamic Setup Metadata

Dynamic setup is a state-dependent search prior. It is reported separately from
static `beamSetupValue` / `playSetupValue`; it does not enter realized EV or
source-credit accounting directly.

| Card | 中文名 | Forms | Static beam/play | Dynamic setup | Slots | Formula | Runtime basis | Reporting note |
|---|---|---|---|---|---|---|---|---|
| Anointed | 天选 | 0, +1 | 0/0 | `anointed.rareDrawAverageDecisionValue` | beam/play | Average decision value of Rare cards currently in draw pile | drawPile cards with rarity == Rare | dynamic beam/play setup; value estimate remains play-delta |
| CosmicIndifference | 宇宙冷漠 | 0, +1 | generated static setup + dynamic play setup | `cosmicIndifference.maxDeckPlayValue` | play | `0.8 * max non-exhaust deck card immediate/resource play value` | non-exhaust deck cards in combat state | dynamic play setup only; value estimate remains play-delta |

## Unsupported Blockers

| Card | 中文名 | Pool | 0 | +1 | Blocking reason |
|---|---|---|---|---|---|
| Alchemize | 炼制药水 | Colorless | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'createPotion' from PotionCmd.TryToProcure.<br>+1: Unsupported simulation action 'createPotion' from PotionCmd.TryToProcure. |
| MonarchsGaze | 王之凝视 | Regent | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;MonarchsGazePower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;MonarchsGazePower&gt;. |
| PanicButton | 应急按钮 | Colorless | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;NoBlockPower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;NoBlockPower&gt;. |
| Rend | 撕碎 | Colorless | NO (不可模拟) | NO (不可模拟) | 0: Generic calculated damage scaling requires manual review.<br>0: Unsupported simulation action 'scalingDamage' from ExtraDamageVar + CalculatedDamageVar.<br>0: Upgraded: Generic calculated damage scaling requires manual review.<br>+1: Generic calculated damage scaling requires manual review.<br>+1: Unsupported simulation action 'scalingDamage' from ExtraDamageVar + CalculatedDamageVar.<br>+1: Upgraded: Generic calculated damage scaling requires manual review. |
| Royalties | 王国资产 | Regent | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'power' from PowerCmd.Apply&lt;RoyaltiesPower&gt;.<br>+1: Unsupported simulation action 'power' from PowerCmd.Apply&lt;RoyaltiesPower&gt;. |
| Splash | 飞溅 | Colorless | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'createCard' from CardPileCmd.AddGeneratedCardToCombat.<br>0: Unsupported simulation action 'selectCards' from CardSelectCmd.FromChooseACardScreen.<br>+1: Unsupported simulation action 'createCard' from CardPileCmd.AddGeneratedCardToCombat.<br>+1: Unsupported simulation action 'selectCards' from CardSelectCmd.FromChooseACardScreen. |
| TheGambit | 孤注一掷 | Colorless | NO (不可模拟) | NO (不可模拟) | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;TheGambitPower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;TheGambitPower&gt;. |

| Blocking reason | Forms | Affected forms |
|---|---:|---|
| Generic calculated damage scaling requires manual review. | 2 | Rend, Rend+1 |
| Unsupported simulation action 'createCard' from CardPileCmd.AddGeneratedCardToCombat. | 2 | Splash, Splash+1 |
| Unsupported simulation action 'createPotion' from PotionCmd.TryToProcure. | 2 | Alchemize, Alchemize+1 |
| Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;MonarchsGazePower&gt;. | 2 | MonarchsGaze, MonarchsGaze+1 |
| Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;NoBlockPower&gt;. | 2 | PanicButton, PanicButton+1 |
| Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;TheGambitPower&gt;. | 2 | TheGambit, TheGambit+1 |
| Unsupported simulation action 'power' from PowerCmd.Apply&lt;RoyaltiesPower&gt;. | 2 | Royalties, Royalties+1 |
| Unsupported simulation action 'scalingDamage' from ExtraDamageVar + CalculatedDamageVar. | 2 | Rend, Rend+1 |
| Unsupported simulation action 'selectCards' from CardSelectCmd.FromChooseACardScreen. | 2 | Splash, Splash+1 |
| Upgraded: Generic calculated damage scaling requires manual review. | 2 | Rend, Rend+1 |

## Play-Delta Only Reasons

| Incomplete attribution action | Forms | Affected forms |
|---|---:|---|
| draw | 30 | BigBang, BigBang+1, DecisionsDecisions, DecisionsDecisions+1, Finesse, Finesse+1, FlashOfSteel, FlashOfSteel+1, Glimmer, Glimmer+1, Glow, Glow+1, GuidingStar, GuidingStar+1, Impatience, Impatience+1, MasterOfStrategy, MasterOfStrategy+1, PhotonCut, PhotonCut+1, Prophesize, Prophesize+1, Restlessness, Restlessness+1, Scrawl, Scrawl+1, SpoilsOfBattle, SpoilsOfBattle+1, ThinkingAhead, ThinkingAhead+1 |
| selectCards | 30 | Begone, Begone+1, Charge, Charge+1, CosmicIndifference, CosmicIndifference+1, DecisionsDecisions, DecisionsDecisions+1, Discovery, Discovery+1, Glimmer, Glimmer+1, Guards, Guards+1, HeirloomHammer, HeirloomHammer+1, PhotonCut, PhotonCut+1, Purity, Purity+1, Quasar, Quasar+1, SecretTechnique, SecretTechnique+1, SecretWeapon, SecretWeapon+1, SeekerStrike, SeekerStrike+1, ThinkingAhead, ThinkingAhead+1 |
| moveCardBetweenPiles | 20 | Anointed, Anointed+1, CosmicIndifference, CosmicIndifference+1, Glimmer, Glimmer+1, PhotonCut, PhotonCut+1, Purity, Purity+1, SecretTechnique, SecretTechnique+1, SecretWeapon, SecretWeapon+1, SeekerStrike, SeekerStrike+1, SummonForth, SummonForth+1, ThinkingAhead, ThinkingAhead+1 |
| createCard | 16 | BundleOfJoy, BundleOfJoy+1, CollisionCourse, CollisionCourse+1, Discovery, Discovery+1, HeirloomHammer, HeirloomHammer+1, JackOfAllTrades, JackOfAllTrades+1, Jackpot, Jackpot+1, ManifestAuthority, ManifestAuthority+1, Quasar, Quasar+1 |
| power | 18 | Calamity, Calamity+1, Entropy, Entropy+1, Mayhem, Mayhem+1, Nostalgia, Nostalgia+1, PaleBlueDot, PaleBlueDot+1, SpectrumShift, SpectrumShift+1, Stratagem, Stratagem+1, Tyranny, Tyranny+1, VoidForm, VoidForm+1 |
| selfReturn | 8 | Bolas, Bolas+1, MakeItSo, MakeItSo+1, ShiningStrike, ShiningStrike+1, ThrummingHatchet, ThrummingHatchet+1 |
| autoPlay | 6 | BeatDown, BeatDown+1, Catastrophe, Catastrophe+1, DecisionsDecisions, DecisionsDecisions+1 |
| transformCard | 6 | Begone, Begone+1, Charge, Charge+1, Guards, Guards+1 |
| createCardChoices | 4 | ManifestAuthority, ManifestAuthority+1, Quasar, Quasar+1 |
| drawNextTurn | 4 | ForegoneConclusion, ForegoneConclusion+1, Glow, Glow+1 |
| grantReplay | 2 | HiddenGem, HiddenGem+1 |

| Card | 中文名 | Pool | 0 | +1 | Play-delta reason |
|---|---|---|---|---|---|
| Anointed | 天选 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'. |
| BeatDown | 狠揍 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'autoPlay'.<br>+1: Attribution incomplete for action 'autoPlay'. |
| Begone | 下去！ | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| BigBang | 大爆炸 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Bolas | 流星锤 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| BundleOfJoy | 新生之喜 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| Calamity | 劫难 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Catastrophe | 横祸 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'autoPlay'.<br>+1: Attribution incomplete for action 'autoPlay'. |
| Charge | 冲锋！！ | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| CollisionCourse | 碰撞轨迹 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| CosmicIndifference | 宇宙冷漠 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| DecisionsDecisions | 抉择，抉择 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'autoPlay'.<br>0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'autoPlay'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Discovery | 发现 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Entropy | 熵 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Finesse | 妙计 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| FlashOfSteel | 亮剑 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| ForegoneConclusion | 既定事项 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'drawNextTurn'.<br>+1: Attribution incomplete for action 'drawNextTurn'. |
| Glimmer | 微光 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Glow | 辉光 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'drawNextTurn'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'drawNextTurn'. |
| Guards | 护驾！！！ | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| GuidingStar | 引导之星 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| HeirloomHammer | 传承之锤 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'selectCards'. |
| HiddenGem | 未掘宝石 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'grantReplay'.<br>+1: Attribution incomplete for action 'grantReplay'. |
| Impatience | 急躁 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| JackOfAllTrades | 花样百出 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| Jackpot | 大奖 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| MakeItSo | 如此甚好 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| ManifestAuthority | 君权自授 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'createCardChoices'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCardChoices'. |
| MasterOfStrategy | 战略大师 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Mayhem | 乱战 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Nostalgia | 怀旧 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| PaleBlueDot | 暗淡蓝点 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| PhotonCut | 光子切割 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Prophesize | 预言 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Purity | 净化 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Quasar | 类星体 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'createCardChoices'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCardChoices'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Restlessness | 心神不宁 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Scrawl | 潦草急就 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| SecretTechnique | 秘密技法 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| SecretWeapon | 秘密武器 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| SeekerStrike | 探寻打击 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| ShiningStrike | 明耀打击 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| SpectrumShift | 光谱偏移 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| SpoilsOfBattle | 战利品 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Stratagem | 计策 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| SummonForth | 征召上前 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'. |
| ThinkingAhead | 深谋远虑 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| ThrummingHatchet | 无休手斧 | Colorless | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| Tyranny | 暴政 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| VoidForm | 虚空形态 | Regent | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |

## Regent Cards

| Card | 中文名 | Pool | Type | Rarity | Cost 0/+1 | 0 | +1 | 已建模能力/字段 | 阻塞或策略原因 |
|---|---|---|---|---|---:|---|---|---|---|
| Alignment | 星位序列 | Regent | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: energy=2, starCost=3<br>+1: energy=3, starCost=3 | - |
| Arsenal | 武器库 | Regent | Power | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: power:Arsenal<br>+1: innate, power:Arsenal | - |
| AstralPulse | 星界脉冲 | Regent | Attack | Common | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=12, starCost=3<br>+1: damage=16, starCost=3 | - |
| BeatIntoShape | 锻打成型 | Regent | Attack | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=5<br>+1: damage=7 | - |
| Begone | 下去！ | Regent | Skill | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | selectCards, transformCard | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| BigBang | 大爆炸 | Regent | Skill | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=1, energy=1, star=1, forge=5, exhaust<br>+1: draw=1, energy=1, star=1, forge=5, exhaust, innate | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| BlackHole | 黑洞 | Regent | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | persistentPowerTrigger, power:BlackHole | - |
| Bombardment | 轰击 | Regent | Attack | Rare | 3 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=18, exhaust, autoPlay<br>+1: damage=24, exhaust, autoPlay | - |
| Bulwark | 铸墙 | Regent | Skill | Uncommon | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=12, forge=10<br>+1: block=15, forge=13 | - |
| BundleOfJoy | 新生之喜 | Regent | Skill | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | exhaust, createCard | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| CelestialMight | 天穹之力 | Regent | Attack | Common | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | damage=6 | - |
| Charge | 冲锋！！ | Regent | Skill | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | selectCards, transformCard | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| ChildOfTheStars | 群星之子 | Regent | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | persistentPowerTrigger, power:ChildOfTheStars | - |
| CloakOfStars | 群星斗篷 | Regent | Skill | Common | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=7, starCost=1<br>+1: block=10, starCost=1 | - |
| CollisionCourse | 碰撞轨迹 | Regent | Attack | Common | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=11, createCard<br>+1: damage=15, createCard | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| Comet | 彗星 | Regent | Attack | Rare | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=33, starCost=5, vulnerable=3<br>+1: damage=44, starCost=5, vulnerable=3 | - |
| Conqueror | 征服者 | Regent | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: forge=3, power:Conqueror<br>+1: forge=5, power:Conqueror | - |
| Convergence | 汇流 | Regent | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: energyNext=1, starNext=1, power:RetainHand<br>+1: energyNext=1, starNext=2, power:RetainHand | - |
| CosmicIndifference | 宇宙冷漠 | Regent | Skill | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: block=6, selectCards, moveCardBetweenPiles<br>+1: block=9, selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| CrashLanding | 迫降 | Regent | Attack | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=21<br>+1: damage=26 | - |
| CrescentSpear | 新月长矛 | Regent | Attack | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | damage=8, scaling=starCostCardCount, starCost=1 | - |
| CrushUnder | 下砸 | Regent | Attack | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=7, power:CrushUnder<br>+1: damage=8, power:CrushUnder | - |
| DecisionsDecisions | 抉择，抉择 | Regent | Skill | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=3, starCost=6, exhaust, autoPlay, selectCards<br>+1: draw=5, starCost=6, exhaust, autoPlay, selectCards | 0: Attribution incomplete for action 'autoPlay'.<br>0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'autoPlay'.; ... +2 |
| DefendRegent | 防御 | Regent | Skill | Basic | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=5<br>+1: block=8 | - |
| Devastate | 葬送 | Regent | Attack | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=30, starCost=4<br>+1: damage=40, starCost=4 | - |
| DyingStar | 星灭 | Regent | Attack | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=9, starCost=3, ethereal, power:DyingStar<br>+1: damage=11, starCost=3, ethereal, power:DyingStar | - |
| FallingStar | 陨星 | Regent | Attack | Basic | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=8, starCost=2, vulnerable=1<br>+1: damage=12, starCost=2, vulnerable=1 | - |
| ForegoneConclusion | 既定事项 | Regent | Skill | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: drawNext=2, power:ForegoneConclusion<br>+1: drawNext=3, power:ForegoneConclusion | 0: Attribution incomplete for action 'drawNextTurn'.<br>+1: Attribution incomplete for action 'drawNextTurn'. |
| Furnace | 熔炉 | Regent | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Furnace | - |
| GammaBlast | 伽马爆破 | Regent | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=13, starCost=3, vulnerable=2<br>+1: damage=18, starCost=3, vulnerable=2 | - |
| GatherLight | 收集光辉 | Regent | Skill | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=8, star=1<br>+1: block=11, star=1 | - |
| Genesis | 创世纪 | Regent | Power | Rare | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Genesis | - |
| Glimmer | 微光 | Regent | Skill | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=3, selectCards, moveCardBetweenPiles<br>+1: draw=4, selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.; ... +2 |
| Glitterstream | 流光溢彩 | Regent | Skill | Common | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=11, blockNext=5<br>+1: block=13, blockNext=7 | - |
| Glow | 辉光 | Regent | Skill | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=1, drawNext=1, star=1<br>+1: draw=1, drawNext=1, star=2 | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'drawNextTurn'.<br>+1: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'drawNextTurn'. |
| Guards | 护驾！！！ | Regent | Skill | Rare | 2 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | exhaust, selectCards, transformCard | 0: Attribution incomplete for action 'selectCards'.<br>0: Attribution incomplete for action 'transformCard'.<br>+1: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'transformCard'. |
| GuidingStar | 引导之星 | Regent | Attack | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=12, draw=2, starCost=2<br>+1: damage=13, draw=3, starCost=2 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| HeavenlyDrill | 天际钻头 | Regent | Attack | Rare | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | xCostDamage | - |
| Hegemony | 霸权 | Regent | Attack | Uncommon | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=15, energyNext=2<br>+1: damage=18, energyNext=3 | - |
| HeirloomHammer | 传承之锤 | Regent | Attack | Rare | 2 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=20, selectCards, createCard<br>+1: damage=25, selectCards, createCard | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'selectCards'. |
| HiddenCache | 隐秘藏品 | Regent | Skill | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: star=1, starNext=3<br>+1: star=1, starNext=4 | - |
| IAmInvincible | 所向无敌 | Regent | Skill | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=10<br>+1: block=13 | - |
| KinglyKick | 王者之踢 | Regent | Attack | Uncommon | 4 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=27<br>+1: damage=35 | - |
| KinglyPunch | 王者之拳 | Regent | Attack | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=8<br>+1: damage=10 | - |
| KnockoutBlow | 决胜一击 | Regent | Attack | Uncommon | 3 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=30, star=5<br>+1: damage=38, star=5 | - |
| KnowThyPlace | 何人僭越 | Regent | Skill | Common | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: vulnerable=1, exhaust<br>+1: vulnerable=1 | - |
| LunarBlast | 月面射击 | Regent | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=4, scaling=skillsPlayedThisTurn<br>+1: damage=5, scaling=skillsPlayedThisTurn | - |
| MakeItSo | 如此甚好 | Regent | Attack | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=6, selfReturn<br>+1: damage=9, selfReturn | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| ManifestAuthority | 君权自授 | Regent | Skill | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: block=7, createCardChoices, createCard<br>+1: block=8, createCardChoices, createCard | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'createCardChoices'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCardChoices'. |
| MeteorShower | 流星雨 | Regent | Attack | Ancient | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=14, starCost=2, vulnerable=2<br>+1: damage=21, starCost=2, vulnerable=2 | - |
| MonarchsGaze | 王之凝视 | Regent | Power | Rare | 2/1 | NO (不可模拟) | NO (不可模拟) | no explicit modeled value/effect | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;MonarchsGazePower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;MonarchsGazePower&gt;. |
| Monologue | 独白 | Regent | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: power:Monologue<br>+1: retain, power:Monologue | - |
| NeutronAegis | 中子护盾 | Regent | Power | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | starCost=5, power:Plating | - |
| Orbit | 环绕轨道 | Regent | Power | Uncommon | 2/1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Orbit | - |
| PaleBlueDot | 暗淡蓝点 | Regent | Power | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:PaleBlueDot | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Parry | 招架 | Regent | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Parry | - |
| ParticleWall | 粒子墙 | Regent | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=9, starCost=2<br>+1: block=12, starCost=2 | - |
| Patter | 星星点点 | Regent | Skill | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=8, power:Vigor<br>+1: block=10, power:Vigor | - |
| PhotonCut | 光子切割 | Regent | Attack | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=10, draw=1, moveCardBetweenPiles, selectCards<br>+1: damage=13, draw=2, moveCardBetweenPiles, selectCards | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.; ... +2 |
| PillarOfCreation | 创世之柱 | Regent | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=3, power:PillarOfCreation<br>+1: block=4, power:PillarOfCreation | - |
| Prophesize | 预言 | Regent | Skill | Uncommon | 2 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=6<br>+1: draw=9 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Quasar | 类星体 | Regent | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | starCost=2, createCardChoices, selectCards, createCard | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'createCardChoices'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.; ... +2 |
| Radiate | 辐射 | Regent | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=3, scaling=starsGainedThisTurn<br>+1: damage=4, scaling=starsGainedThisTurn | - |
| RefineBlade | 淬炼刀刃 | Regent | Skill | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: energyNext=1, forge=9<br>+1: energyNext=1, forge=13 | - |
| Reflect | 倒映 | Regent | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=15, starCost=3, power:Reflect<br>+1: block=20, starCost=3, power:Reflect | - |
| Resonance | 共鸣 | Regent | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | starCost=3, power:Strength | - |
| RoyalGamble | 胜券在王 | Regent | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: star=9, starCost=5, exhaust<br>+1: star=9, starCost=5, exhaust, retain | - |
| Royalties | 王国资产 | Regent | Power | Rare | 1 | NO (不可模拟) | NO (不可模拟) | no explicit modeled value/effect | 0: Unsupported simulation action 'power' from PowerCmd.Apply&lt;RoyaltiesPower&gt;.<br>+1: Unsupported simulation action 'power' from PowerCmd.Apply&lt;RoyaltiesPower&gt;. |
| SeekingEdge | 追踪之刃 | Regent | Power | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: forge=7, power:SeekingEdge<br>+1: forge=11, power:SeekingEdge | - |
| SevenStars | 七星 | Regent | Attack | Rare | 2/1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | damage=49, starCost=7 | - |
| ShiningStrike | 明耀打击 | Regent | Attack | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=8, star=2, selfReturn<br>+1: damage=11, star=2, selfReturn | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| SolarStrike | 太阳打击 | Regent | Attack | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=9, star=1<br>+1: damage=10, star=2 | - |
| SpectrumShift | 光谱偏移 | Regent | Power | Uncommon | 2/1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:SpectrumShift | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| SpoilsOfBattle | 战利品 | Regent | Skill | Common | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=2, forge=5<br>+1: draw=2, forge=8 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Stardust | 星尘 | Regent | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=5<br>+1: damage=7 | - |
| StrikeRegent | 打击 | Regent | Attack | Basic | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=6<br>+1: damage=9 | - |
| SummonForth | 征召上前 | Regent | Skill | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: forge=8, moveCardBetweenPiles<br>+1: forge=11, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'. |
| Supermassive | 超质量体 | Regent | Attack | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | damage=5, scaling=generatedCardsCreated | - |
| SwordSage | 剑圣 | Regent | Power | Rare | 2/1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:SwordSage | - |
| Terraforming | 地形改造 | Regent | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Vigor | - |
| TheSealedThrone | 封印王座 | Regent | Power | Ancient | 1/0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | starCost=3, power:TheSealedThrone | - |
| TheSmith | 铸剑者 | Regent | Skill | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: starCost=4, forge=30<br>+1: starCost=4, forge=40 | - |
| Tyranny | 暴政 | Regent | Power | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: power:Tyranny<br>+1: innate, power:Tyranny | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Venerate | 崇拜 | Regent | Skill | Basic | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: star=2<br>+1: star=3 | - |
| VoidForm | 虚空形态 | Regent | Power | Rare | 3 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: endsTurn, ethereal, power:VoidForm<br>+1: endsTurn, power:VoidForm | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| WroughtInWar | 战火铸就 | Regent | Attack | Common | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=7, forge=7<br>+1: damage=9, forge=9 | - |

## Colorless Cards

| Card | 中文名 | Pool | Type | Rarity | Cost 0/+1 | 0 | +1 | 已建模能力/字段 | 阻塞或策略原因 |
|---|---|---|---|---|---:|---|---|---|---|
| Alchemize | 炼制药水 | Colorless | Skill | Rare | 1/0 | NO (不可模拟) | NO (不可模拟) | exhaust | 0: Unsupported simulation action 'createPotion' from PotionCmd.TryToProcure.<br>+1: Unsupported simulation action 'createPotion' from PotionCmd.TryToProcure. |
| Anointed | 天选 | Colorless | Skill | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: exhaust, moveCardBetweenPiles<br>+1: exhaust, retain, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'. |
| Automation | 自动化 | Colorless | Power | Uncommon | 1/0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Automation | - |
| BeatDown | 狠揍 | Colorless | Skill | Rare | 3 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | autoPlay | 0: Attribution incomplete for action 'autoPlay'.<br>+1: Attribution incomplete for action 'autoPlay'. |
| Bolas | 流星锤 | Colorless | Attack | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=3, selfReturn<br>+1: damage=4, selfReturn | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| Calamity | 劫难 | Colorless | Power | Rare | 3/2 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:Calamity | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Catastrophe | 横祸 | Colorless | Skill | Uncommon | 2 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | autoPlay | 0: Attribution incomplete for action 'autoPlay'.<br>+1: Attribution incomplete for action 'autoPlay'. |
| DarkShackles | 黑暗镣铐 | Colorless | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | exhaust, power:DarkShackles | - |
| Discovery | 发现 | Colorless | Skill | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: exhaust, selectCards, createCard<br>+1: selectCards, createCard | 0: Attribution incomplete for action 'createCard'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'selectCards'. |
| DramaticEntrance | 闪亮登场 | Colorless | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=11, exhaust, innate<br>+1: damage=15, exhaust, innate | - |
| Entropy | 熵 | Colorless | Power | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: power:Entropy<br>+1: innate, power:Entropy | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Equilibrium | 均衡 | Colorless | Skill | Uncommon | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=13, power:RetainHand<br>+1: block=16, power:RetainHand | - |
| EternalArmor | 永恒铠甲 | Colorless | Power | Rare | 3 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Plating | - |
| Fasten | 勒紧 | Colorless | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Fasten | - |
| Finesse | 妙计 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: block=4, draw=1<br>+1: block=7, draw=1 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Fisticuffs | 拳斗 | Colorless | Attack | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=7<br>+1: damage=9 | - |
| FlashOfSteel | 亮剑 | Colorless | Attack | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=5, draw=1<br>+1: damage=8, draw=1 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| GoldAxe | 金斧 | Colorless | Attack | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: scaling=cardsPlayedThisCombat<br>+1: scaling=cardsPlayedThisCombat, retain | - |
| HandOfGreed | 贪婪之手 | Colorless | Attack | Rare | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=20<br>+1: damage=25 | - |
| HiddenGem | 未掘宝石 | Colorless | Skill | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: replay=2, grantReplay<br>+1: replay=3, grantReplay | 0: Attribution incomplete for action 'grantReplay'.<br>+1: Attribution incomplete for action 'grantReplay'. |
| Impatience | 急躁 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=2<br>+1: draw=3 | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| JackOfAllTrades | 花样百出 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | exhaust, createCard | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| Jackpot | 大奖 | Colorless | Attack | Rare | 3 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=25, createCard<br>+1: damage=30, createCard | 0: Attribution incomplete for action 'createCard'.<br>+1: Attribution incomplete for action 'createCard'. |
| MasterOfStrategy | 战略大师 | Colorless | Skill | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=3, exhaust<br>+1: draw=4, exhaust | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| Mayhem | 乱战 | Colorless | Power | Rare | 2/1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:Mayhem | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| MindBlast | 心灵震慑 | Colorless | Attack | Uncommon | 1/0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | scaling=drawPileCount, innate | - |
| Nostalgia | 怀旧 | Colorless | Power | Rare | 1/0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:Nostalgia | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| Omnislice | 万向斩 | Colorless | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=8<br>+1: damage=11 | - |
| Panache | 神气制胜 | Colorless | Power | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Panache | - |
| PanicButton | 应急按钮 | Colorless | Skill | Uncommon | 0 | NO (不可模拟) | NO (不可模拟) | 0: block=30, exhaust<br>+1: block=40, exhaust | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;NoBlockPower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;NoBlockPower&gt;. |
| PrepTime | 准备时间 | Colorless | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:PrepTime | - |
| Production | 生产制造 | Colorless | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: energy=2, exhaust<br>+1: energy=3, exhaust | - |
| Prolong | 延伸 | Colorless | Skill | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: exhaust<br>+1: no explicit modeled value/effect | - |
| Prowess | 非凡技艺 | Colorless | Power | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:Strength, power:Dexterity | - |
| Purity | 净化 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | exhaust, retain, selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Rend | 撕碎 | Colorless | Attack | Rare | 2 | NO (不可模拟) | NO (不可模拟) | 0: damage=15<br>+1: damage=18 | 0: Generic calculated damage scaling requires manual review.<br>0: Unsupported simulation action 'scalingDamage' from ExtraDamageVar + CalculatedDamageVar.<br>0: Upgraded: Generic calculated damage scaling requires manual review.<br>+1: Generic calculated damage scaling requires manual review.; ... +2 |
| Restlessness | 心神不宁 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=2, energy=2, retain<br>+1: draw=3, energy=3, retain | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| RollingBoulder | 滚石 | Colorless | Power | Rare | 3 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:RollingBoulder | - |
| Salvo | 箭雨 | Colorless | Attack | Rare | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=12, power:RetainHand<br>+1: damage=16, power:RetainHand | - |
| Scrawl | 潦草急就 | Colorless | Skill | Rare | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: drawToFull, exhaust<br>+1: drawToFull, exhaust, retain | 0: Attribution incomplete for action 'draw'.<br>+1: Attribution incomplete for action 'draw'. |
| SecretTechnique | 秘密技法 | Colorless | Skill | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: exhaust, selectCards, moveCardBetweenPiles<br>+1: selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| SecretWeapon | 秘密武器 | Colorless | Skill | Rare | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: exhaust, selectCards, moveCardBetweenPiles<br>+1: selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| SeekerStrike | 探寻打击 | Colorless | Attack | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=9, selectCards, moveCardBetweenPiles<br>+1: damage=12, selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'moveCardBetweenPiles'.<br>+1: Attribution incomplete for action 'selectCards'. |
| Shockwave | 震荡波 | Colorless | Skill | Uncommon | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: vulnerable=3, exhaust<br>+1: vulnerable=5, exhaust | - |
| Splash | 飞溅 | Colorless | Skill | Uncommon | 1 | NO (不可模拟) | NO (不可模拟) | no explicit modeled value/effect | 0: Unsupported simulation action 'createCard' from CardPileCmd.AddGeneratedCardToCombat.<br>0: Unsupported simulation action 'selectCards' from CardSelectCmd.FromChooseACardScreen.<br>+1: Unsupported simulation action 'createCard' from CardPileCmd.AddGeneratedCardToCombat.<br>+1: Unsupported simulation action 'selectCards' from CardSelectCmd.FromChooseACardScreen. |
| Stratagem | 计策 | Colorless | Power | Uncommon | 1/0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | power:Stratagem | 0: Attribution incomplete for action 'power'.<br>+1: Attribution incomplete for action 'power'. |
| TheBomb | 炸弹 | Colorless | Skill | Uncommon | 2 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | power:TheBomb | - |
| TheGambit | 孤注一掷 | Colorless | Skill | Rare | 0 | NO (不可模拟) | NO (不可模拟) | 0: block=50<br>+1: block=75 | 0: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;TheGambitPower&gt;.<br>+1: Unsupported simulation action 'power' from DynamicVar + PowerCmd.Apply&lt;TheGambitPower&gt;. |
| ThinkingAhead | 深谋远虑 | Colorless | Skill | Uncommon | 0 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: draw=2, exhaust, selectCards, moveCardBetweenPiles<br>+1: draw=2, selectCards, moveCardBetweenPiles | 0: Attribution incomplete for action 'draw'.<br>0: Attribution incomplete for action 'moveCardBetweenPiles'.<br>0: Attribution incomplete for action 'selectCards'.<br>+1: Attribution incomplete for action 'draw'.; ... +2 |
| ThrummingHatchet | 无休手斧 | Colorless | Attack | Uncommon | 1 | PD (可模拟/play-delta) | PD (可模拟/play-delta) | 0: damage=11, selfReturn<br>+1: damage=14, selfReturn | 0: Attribution incomplete for action 'selfReturn'.<br>+1: Attribution incomplete for action 'selfReturn'. |
| UltimateDefend | 究极防御 | Colorless | Skill | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: block=11<br>+1: block=15 | - |
| UltimateStrike | 究极打击 | Colorless | Attack | Uncommon | 1 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | 0: damage=14<br>+1: damage=20 | - |
| Volley | 连射 | Colorless | Attack | Uncommon | 0 | SC (可模拟/source-credit) | SC (可模拟/source-credit) | xCostDamage | - |

## Out-of-Scope Regent/Colorless Cards

These cards are excluded from the main support counts because the current single-player Regent simulation candidate rule excludes multiplayer-only and Ally-targeted cards.

| Card | 中文名 | Pool | Reason |
|---|---|---|---|
| BeaconOfHope | 希望灯塔 | Colorless | multiplayerOnly |
| BelieveInYou | 相信着你 | Colorless | multiplayerOnly, Ally target/reference |
| Coordinate | 协同配合 | Colorless | multiplayerOnly, Ally target/reference |
| GangUp | 群起攻之 | Colorless | multiplayerOnly |
| HammerTime | 锤子时间 | Regent | multiplayerOnly |
| HuddleUp | 抱团 | Colorless | multiplayerOnly |
| Intercept | 拦截 | Colorless | multiplayerOnly, Ally target/reference |
| Knockdown | 击倒 | Colorless | multiplayerOnly |
| Largesse | 慷慨捐助 | Regent | multiplayerOnly, Ally target/reference |
| Lift | 托举 | Colorless | multiplayerOnly, Ally target/reference |
| Mimic | 拟态 | Colorless | multiplayerOnly, Ally target/reference |
| Rally | 集结 | Colorless | multiplayerOnly |
| TagTeam | 双打组合 | Colorless | multiplayerOnly |
