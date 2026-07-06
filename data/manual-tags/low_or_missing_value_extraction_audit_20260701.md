# Low / Missing Card Values - Terms, Strategy & Extraction-Gap Audit

Generated: 2026-07-01 (regenerated from `low_or_missing_value_review_20260701.md`)

Companion to the value-threshold review. For each of the **53** flagged cards this audit (1) re-derives the card's real in-game 词条 (terms) from the decompiled game source, (2) runs the play-delta / direct-play (source-credit) decision logic against those terms, and (3) compares the source to `data/extracted/card_facts.generated.json` to find effects our parser failed to 词条化 (the *LunarBlast* class).

Sources: decompiled game source under `data/generated/decompiled/sts2/.../Cards/*.cs` (+ matching `*Power.cs`); extracted facts `data/extracted/card_facts.generated.json`; resolved strategy + values from the `auto` run `data/generated/direct_play_values/latest.generated.json` (d40 / r200 / t4.8.14).

## How the decision logic classifies a card

The `estimate-direct-play-values --value-strategy auto` resolver (`Program.DirectPlayValues.cs`) reads the card's **extracted actions** and simulator warnings, then picks:

- **source-credit** ("direct play value") - every term maps to a concrete value channel: `damage`, `block`, `energyGain/Next`, `starGain/Next/Cost`, `forge`, a *supported* `power`, or a debuff. Fully attributable, valued per direct play.
- **play-delta** - the card carries at least one incomplete-attribution term (`draw`, `drawNextTurn`, `selectCards`, `moveCardBetweenPiles`, `transformCard`, `createCard`, `createCardChoices`) and all other terms are attributable. Valued as `normalEV - blockedEV` per play.
- **excluded** - the card has an *unsupported* simulation action (an unsupported power key, `createPotion`, sourceless `CardPileCmd.Add`, generic calculated-damage-scaling). No value produced.

**Key structural weakness surfaced by this audit:** the resolver keys off the card's *own extracted actions*. When the parser drops a term (a computed multiplier, a state-sourced draw count) or hides a power's real effect (draw/create/transform living in `*Power.cs`), the resolver sees an incomplete or generic term set and can pick the **wrong strategy** - most often calling a card `source-credit` (then scoring it 0) when its real effect needs `play-delta`.

## Strategy split (as run today)

| Strategy | Count | Notes |
|---|---:|---|
| play-delta | 23 | All verified **correct** (each carries a genuine incomplete-attribution term). |
| source-credit | 23 | **11 are misclassified** (should be play-delta or excluded) - powers/cards whose real draw/create/transform/cost effect was not extracted; they score 0/0/0. |
| excluded | 7 | 6 correct (createPotion / unsupported powers / post-combat gold / sourceless Rare-add); **1 (Bolas) over-excluded** - its OnPlay is pure 3 dmg and source-creditable. |

## Extraction-gap severity summary

| Severity | Count | Meaning |
|---|---:|---|
| high | 8 | Value materially wrong or the defining effect is unmodeled (0/0/0 or flat), and/or strategy misclassified. |
| medium | 9 | Notable term missing/mis-encoded; strategy questionable or value biased. |
| low | 19 | Minor/mislabeled/harmless term, or the engine recovers it elsewhere. |
| none | 17 | Facts match source. |

## Extraction gaps by root cause

### A. Computed hit-count / damage scaling dropped (the *LunarBlast* class) - HIGH
`CardFactParser.HitCountRegex` (`CardFactParser.cs:100`) only matches a **literal integer** `.WithHitCount(4)`. A computed hit count via `CalculatedVar(...).WithMultiplier(...)` -> `.WithHitCount((int)(...).Calculate(...))` is silently dropped, so only flat base damage survives and the simulator has no recovery path.

- **LUNAR_BLAST (月面射击)** - base 4 dmg x *Skill cards played this turn*. Kept flat 4/4/4 -> 5/5/5. Undervalued on any turn with >=2 skills.
- **RADIATE (辐射)** - AoE base 3 dmg x *stars gained this turn*. Kept flat single-hit. Badly undervalued in star decks.
- Contrast **STARDUST (星尘)** - same `.WithHitCount(ResolveStarXValue())` shape, but the engine independently reconstructs star-X multi-hit via `HasStarCostX` + `XCostDamageValue`. **Not** a value error (low). This is the recovery path the two cards above lack.

### B. Core operation outside the recognized action set - parser produced nothing / a bare count - HIGH
The card body's defining op is in no extraction regex, so facts show `none (no facts parsed)` or only a stray count var; the card scores 0/0/0 and is mis-typed as source-credit.

- **BEAT_DOWN (狠揍)** - `CardCmd.AutoPlay` loop over 3 discard-pile Attacks. No facts parsed.
- **CATASTROPHE (横祸)** - `CardCmd.AutoPlay` loop over 2 draw-pile cards. No facts parsed.
- **HIDDEN_GEM (未掘宝石)** - `cardModel.BaseReplayCount += IntVar("Replay")` grants Replay to a drawn card. No facts parsed.
- **SCRAWL (潦草急就)** - `CardPileCmd.Draw(ctx, MaxCardsInHand - Hand.Count)`; state-computed count, no `CardsVar`, so **nothing** was emitted.
- Related: **DECISIONS_DECISIONS (抉择，抉择)** - `RepeatVar(3)` + `CardCmd.AutoPlay` (auto-play a chosen hand Skill 3x). `RepeatVar` has no handling anywhere in the parser; the triple-replay payoff is invisible.

### C. Power's real effect is invisible -> mis-classified source-credit, scores 0 - MEDIUM/HIGH
A `power` action is deemed "fully attributable," but the power's per-turn effect (draw / create-card / transform / tutor / cost-reduction) lives in `*Power.cs` and is never extracted as a card action. So `auto` picks source-credit, which has no channel for those effects -> 0/0/0. All of these should be **play-delta** (VoidForm arguably excluded).

- **CALAMITY** (create random Attacks after each attack) - **SPECTRUM_SHIFT** (create Colorless each turn) - **STRATAGEM** (tutor Draw->Hand on shuffle) - **TYRANNY** (+draw & forced exhaust each turn) - **PALE_BLUE_DOT** (conditional next-turn +draw) - **ENTROPY** (per-turn transform hand cards) - all medium.
- **VOID_FORM** - high: cost-reduction power **plus** an entirely unextracted `PlayerCmd.EndTurn(canBackOut:false)` downside on the card itself.
- **PROLONG** - medium different flavor: correct source-credit *kind* (block), but the carried amount is game-state `creature.Block`, unknown at parse -> 0 value.

### D. Self-return-to-pile mis-read as a generic card move - MEDIUM
`CardPileCmd.Add(this, ...)` (the card returning *itself* to a pile) is flattened into a generic `moveCardBetweenPiles`, forcing play-delta / exclusion even though the direct-play payoff is fully attributable.

- **SHINING_STRIKE** - forced to play-delta; real payoff (8 dmg + 2 Stars) is source-creditable.
- **BOLAS** - excluded off its `BeforeHandDraw` self-return; OnPlay is pure 3 dmg and source-creditable.

### E. Cross-card synergy (verified NOT a bug)
- **PARRY** - `ParryPower` is inert; only `SovereignBlade` reads it (block per SovereignBlade play). Investigated because it looked like a standalone block ramp, but the simulator credits it **correctly**: both value paths return 0 for non-SovereignBlade cards (`AddSovereignBladePowerCredits`/`EstimateSovereignBladePowerValue`), so absent SovereignBlade the credit is 0. It is **not** over-credited. The only caveat is that its value is fully synergy-conditional - the 5.5/21.5/64.1 ramp reflects the SovereignBlade density of the training decks, not standalone card strength.

### F. Correctly excluded (effect genuinely out of scope) - confirmed from source
- **ALCHEMIZE** (`createPotion`), **ROYALTIES** (post-combat gold), **MAYHEM** (top-deck auto-play), **MONARCHS_GAZE** (powered-attack enemy -Str), **NOSTALGIA** (recycle first N cards to draw), **ANOINTED** (sourceless add of random Rare draw cards).

## Full per-card audit (53 cards, most-severe first)

| Card | zh | Type | Resolved strategy | Extracted terms | Strategy verdict | Extraction gap | Sev |
|---|---|---|---|---|---|---|---|
| `CARD.BEAT_DOWN` BeatDown | 狠揍 | Skill | source-credit | none (no facts parsed) | WRONG -> play-delta or excluded | Parser wholly failed ("No card facts parsed"). Core op = auto-play 3 (U+1) random Attacks from discard (`CardCmd.AutoPlay` loop over Cards.IntValue). 0/0/0 is a total miss. | high |
| `CARD.CATASTROPHE` Catastrophe | 横祸 | Skill | source-credit | none (no facts parsed) | WRONG -> play-delta or excluded | Parser wholly failed. Core op = auto-play 2 (U+1) random draw-pile cards (`for i<Cards: CardCmd.AutoPlay(...,null)`). 0/0/0 total miss. | high |
| `CARD.DECISIONS_DECISIONS` DecisionsDecisions | 抉择，抉择 | Skill | play-delta | starCost, draw, selectCards | OK (strategy) / effect unmodeled | `RepeatVar(3)` + `CardCmd.AutoPlay` loop (choose a hand Skill, auto-play it 3x) entirely unextracted; only draw+select seen. Defining payoff invisible. | high |
| `CARD.HIDDEN_GEM` HiddenGem | 未掘宝石 | Skill | source-credit | none (no facts parsed) | WRONG -> play-delta or excluded | Parser wholly failed. Grants Replay (2, U->3) to a random drawn card via `cardModel.BaseReplayCount += DynamicVars["Replay"]` (var is `IntVar("Replay",2)`, unrecognized). 0/0/0 total miss. | high |
| `CARD.LUNAR_BLAST` LunarBlast | 月面射击 | Attack | source-credit | damage, damage | OK (strategy) / VALUE WRONG | Base 4 (U+1) dmg with HIT COUNT = Skill cards played this turn (`CalculatedVar("CalculatedHits").WithMultiplier(... Card.Type == CardType.Skill ...)` -> `.WithHitCount((int)(...).Calculate(...))`). `HitCountRegex` only matches a literal int, so the multiplier is dropped -> flat 4/4/4. THE ARCHETYPE. | high |
| `CARD.RADIATE` Radiate | 辐射 | Attack | source-credit | damage, damage | OK (strategy) / VALUE WRONG | AoE (`TargetingAllOpponents`): gain 1 Star, then base 3 (U+1) dmg with HIT COUNT = stars gained this turn (`CalculatedVar("CalculatedHits").WithMultiplier(...)` -> `.WithHitCount((int)(...).Calculate(...))`). Computed hit count dropped -> flat single-hit. No engine recovery path. | high |
| `CARD.SCRAWL` Scrawl | 潦草急就 | Skill | source-credit | - | WRONG -> play-delta | Parser emitted NOTHING (no actions/vars). Draw until hand full: `CardPileCmd.Draw(ctx, MaxCardsInHand - Hand.Count)` - the count is state-computed, not a `CardsVar`, so the draw term was missed entirely. 0/0/0. U: adds Retain. | high |
| `CARD.VOID_FORM` VoidForm | 虚空形态 | Power | source-credit | power | WRONG -> play-delta or excluded | VoidFormPower: first 2 cards/turn cost 0 energy+stars (cost-reduction, no channel) AND the card immediately `PlayerCmd.EndTurn(canBackOut:false)` (line 26) - a major turn-ending downside that is entirely unextracted. 0/0/0 hides big swings. U: removes Ethereal. | high |
| `CARD.BOLAS` Bolas | 流星锤 | Attack | excluded | damage, moveCardBetweenPiles | over-EXCLUDED -> should be source-credit | The excluding `moveCardBetweenPiles` is the `BeforeHandDraw` self-return (`CardPileCmd.Add(this, Hand)`, conditional), NOT OnPlay. OnPlay is pure 3 dmg (U+4) and is source-creditable if self-returns were distinguished. | medium |
| `CARD.CALAMITY` Calamity | 劫难 | Power | source-credit | power | WRONG -> play-delta | CalamityPower (supported+simulated) adds Amount random Attacks to hand after each Attack you play - a create-card effect with no source-credit channel, so it scores 0/0/0. U: -1 energy. | medium |
| `CARD.ENTROPY` Entropy | 熵 | Power | source-credit | power | WRONG -> play-delta or excluded | EntropyPower (supported+simulated): each turn select Amount hand cards & `TransformToRandom` them (transform/select) - no source-credit channel, 0/0/0. U: Innate. | medium |
| `CARD.PALE_BLUE_DOT` PaleBlueDot | 暗淡蓝点 | Power | source-credit | power | WRONG -> play-delta | PaleBlueDotPower (supported+simulated): after >=5 plays last turn, next draw +Amount (drawNextTurn, conditional) - draw has no source-credit channel, 0/0/0. U: Cards 1->2. | medium |
| `CARD.PROLONG` Prolong | 延伸 | Skill | source-credit | power | OK (kind) / 0 value | source-credit kind is right (block channel) but the carried amount is game-state `creature.Block` (`BlockNextTurnPower`), which resolves to null at parse time -> no amount -> 0 value. U removes Exhaust. | medium |
| `CARD.SHINING_STRIKE` ShiningStrike | 明耀打击 | Attack | play-delta | damage, starGain, moveCardBetweenPiles | WRONG -> should be source-credit | play-delta is forced by the self-return `CardPileCmd.Add(this, Draw, Top)`, which the fact model cannot tell apart from moving another card. Real single-play payoff (8 dmg U+3 + 2 Stars) is fully attributable. | medium |
| `CARD.SPECTRUM_SHIFT` SpectrumShift | 光谱偏移 | Power | source-credit | power | WRONG -> play-delta | SpectrumShiftPower (supported+simulated): each turn add Amount distinct Colorless cards to hand (create-card) - no source-credit channel, 0/0/0. U: -1 energy. | medium |
| `CARD.STRATAGEM` Stratagem | 计策 | Power | source-credit | power | WRONG -> play-delta | StratagemPower (supported+simulated): on each shuffle move Amount cards Draw->Hand (tutor) - selectCards/move, no source-credit channel, 0/0/0. U: -1 energy. | medium |
| `CARD.TYRANNY` Tyranny | 暴政 | Power | source-credit | power | WRONG -> play-delta | TyrannyPower (supported+simulated): +Amount draw/turn AND exhaust Amount hand cards/turn - draw boost has no source-credit channel, forced exhaust is a downside, nets 0/0/0. U: Innate. | medium |
| `CARD.ANOINTED` Anointed | 天选 | Skill | excluded | moveCardBetweenPiles | OK (excluded) | Correctly excluded: sourceless `CardPileCmd.Add` (put random Rare draw cards into hand). Hidden state-count (MaxHand-hand) + Rare-only filter unextracted; moot given exclusion. | low |
| `CARD.ARSENAL` Arsenal | 武器库 | Power | source-credit | power | OK | ArsenalPower: +1 Strength per card generated into combat. Sim active-power drives the S/M/L ramp. Innate-on-upgrade unmodeled. | low |
| `CARD.BEGONE` Begone | 下去！ | Skill | play-delta | selectCards, transformCard | OK | Clean terms (transform a chosen hand card into MinionStrike). Conditional upgrade of the created MinionStrike is unrepresented (no numeric var). | low |
| `CARD.COSMIC_INDIFFERENCE` CosmicIndifference | 宇宙冷漠 | Skill | play-delta | block, selectCards, moveCardBetweenPiles | OK | Retrieval is from Discard (`FromCombatPile(PileType.Discard)`) and may be mis-encoded as a Hand-source move. Block 6 (U+3) correct. | low |
| `CARD.DISCOVERY` Discovery | 发现 | Skill | play-delta | selectCards, createCard | OK | `SetToFreeThisTurn()` on the generated card not a parsed fact. | low |
| `CARD.FOREGONE_CONCLUSION` ForegoneConclusion | 既定事项 | Skill | play-delta | power | OK | Mislabeled generic `power`; real effect (next-turn choose N draw-pile cards into hand) lives in `ForegoneConclusionPower.BeforeHandDraw`. play-delta lands correctly anyway. | low |
| `CARD.HEIRLOOM_HAMMER` HeirloomHammer | 传承之锤 | Attack | play-delta | damage, selectCards, createCard | OK | `RepeatVar(1)` dropped (no RepeatVar support), but clone count 1 == parser default createCard amount, so no present error; fragile if rebalanced. Damage 20 (U+5) correct. | low |
| `CARD.JACKPOT` Jackpot | 大奖 | Attack | play-delta | damage, createCard | OK | `createCard` amount fixed 1 vs the 3-card generation (separate Cards=3 var present); 0-cost-only pool filter lost. Damage 25 (U+5) correct. | low |
| `CARD.JACK_OF_ALL_TRADES` JackOfAllTrades | 花样百出 | Skill | play-delta | createCard | OK | Clean terms (gen 1->2 Colorless to hand). Downstream pool-fidelity caveat on the generated card only. | low |
| `CARD.MAYHEM` Mayhem | 乱战 | Power | excluded | power | OK (excluded) | Correctly excluded: MayhemPower auto-plays top-of-draw cards each turn (unsupported). U: cost -1 (unmodeled). | low |
| `CARD.MONARCHS_GAZE` MonarchsGaze | 王之凝视 | Power | excluded | power | OK (excluded) | Correctly excluded: MonarchsGazePower applies enemy Strength-down on powered-attack hits (unsupported conditional debuff). U: cost -1 (unmodeled). | low |
| `CARD.MONOLOGUE` Monologue | 独白 | Skill | source-credit | power | OK | MonologuePower: +1 temp Strength per card played this turn, removed at turn end (sim special-cases it). Rough approximation; Retain-on-upgrade unmodeled. | low |
| `CARD.NOSTALGIA` Nostalgia | 怀旧 | Power | excluded | power | OK (excluded) | Correctly excluded: NostalgiaPower recycles the first N attacks/skills played each turn back to the draw pile top (unsupported). U: cost -1 (unmodeled). | low |
| `CARD.PARRY` Parry | 招架 | Power | source-credit | power | OK (synergy-gated) | `ParryPower` (10, U+4) is inert alone - only `SovereignBlade` reads it (block = Parry stacks per SovereignBlade play). Extraction clean; the sim credits it CORRECTLY: both value paths (`AddSovereignBladePowerCredits`, `EstimateSovereignBladePowerValue`) return 0 for non-SovereignBlade, so with no SovereignBlade Parry = 0. NOT over-credited - corrects an earlier draft claim. Caveat: the value is entirely synergy-conditional, so the 5.5/21.5/64.1 ramp reflects the SovereignBlade density of the training decks, not standalone strength. | low |
| `CARD.PURITY` Purity | 净化 | Skill | play-delta | selectCards, moveCardBetweenPiles | OK | Extracted `moveCardBetweenPiles` is spurious - the actual op is `CardCmd.Exhaust`. Choose up to 3 (U+5) hand cards & Exhaust. Harmless. | low |
| `CARD.RESONANCE` Resonance | 共鸣 | Skill | source-credit | starCost, power, power | OK | source-credit correct: +1 (U+2) Strength self, -1 Strength each enemy (AllEnemies). Upgraded values fine; unupgraded 0/0/0 most likely StarCost=3 gating. Confirm enemy -Str is credited. | low |
| `CARD.SPLASH` Splash | 飞溅 | Skill | play-delta | selectCards, createCard | OK | `SetToFreeThisTurn()` + cross-character Attack pool source not encoded. | low |
| `CARD.STARDUST` Stardust | 星尘 | Attack | source-credit | damage | OK | X-star cost, base 5 (U+2) dmg x stars spent (`.WithHitCount(ResolveStarXValue())`). Parser drops the term BUT the engine reconstructs star-X multi-hit via `HasStarCostX` + `XCostDamageValue`/`XCostHitCount`. Value is NOT flat - false alarm. | low |
| `CARD.SUMMON_FORTH` SummonForth | 征召上前 | Skill | play-delta | forge, moveCardBetweenPiles | OK | Move all SovereignBlade cards to hand (state-sourced count) + Forge 8 (U+3). `Retain` comes from a hover tip only, so it is missing from the extracted keyword set. | low |
| `CARD.ALCHEMIZE` Alchemize | 炼制药水 | Skill | excluded | - | OK (excluded) | Correctly excluded: `PotionCmd.TryToProcure` = createPotion, a known-unsupported action. U: cost 1->0. | none |
| `CARD.BUNDLE_OF_JOY` BundleOfJoy | 新生之喜 | Skill | play-delta | createCard | OK | Clean (gen 3 Colorless, U+1). Note: report U == U+ despite +1-card upgrade - a sim/value concern, not extraction. | none |
| `CARD.FURNACE` Furnace | 熔炉 | Power | source-credit | power | OK | Clean. FurnacePower forges 5/turn (U+2). | none |
| `CARD.GENESIS` Genesis | 创世纪 | Power | source-credit | power | OK | Clean. GenesisPower gains 2 Stars/turn (U+1). | none |
| `CARD.GLIMMER` Glimmer | 微光 | Skill | play-delta | draw, selectCards, moveCardBetweenPiles | OK | Clean. draw 3 (U+1) + put 1 hand card on draw top. | none |
| `CARD.GLOW` Glow | 辉光 | Skill | play-delta | starGain, draw, drawNextTurn, power | OK | Clean. gain 1 Star (U+1) + draw 1 + DrawCardsNextTurnPower (drawNextTurn). | none |
| `CARD.GUIDING_STAR` GuidingStar | 引导之星 | Attack | play-delta | starCost, damage, draw | OK | Clean. 12 dmg (U+1) + draw 2 (U+1). | none |
| `CARD.HIDDEN_CACHE` HiddenCache | 隐秘藏品 | Skill | source-credit | starGain, starNextTurn, power | OK | Clean. Gain 1 Star + StarNextTurnPower 3 (U+1). `StarNextTurnPower` specially parsed as a `starNextTurn` action (stays source-credit, not mistaken for an unsupported power). | none |
| `CARD.MANIFEST_AUTHORITY` ManifestAuthority | 君权自授 | Skill | play-delta | block, createCardChoices, createCard | OK | Clean (block 7 U+1 + gen 1 Colorless). Minor: single generated card double-encoded as createCardChoices + createCard. | none |
| `CARD.PHOTON_CUT` PhotonCut | 光子切割 | Attack | play-delta | damage, draw, moveCardBetweenPiles, selectCards | OK | Clean. 10 dmg (U+3) + draw 1 (U+1) + putback. | none |
| `CARD.PROPHESIZE` Prophesize | 预言 | Skill | play-delta | draw | OK | Clean. draw 6 (U+3). | none |
| `CARD.QUASAR` Quasar | 类星体 | Skill | play-delta | starCost, createCardChoices, selectCards, createCard | OK | Clean. Discover 3 Colorless, pick 1. | none |
| `CARD.ROYALTIES` Royalties | 王国资产 | Power | excluded | power | OK (excluded) | Correctly excluded: RoyaltiesPower grants +30 (U+10) gold after combat - no in-combat value channel by design. | none |
| `CARD.SEEKER_STRIKE` SeekerStrike | 探寻打击 | Attack | play-delta | damage, selectCards, moveCardBetweenPiles | OK | Clean. 9 dmg (U+3) + look top 3, add 1 to hand. | none |
| `CARD.SPOILS_OF_BATTLE` SpoilsOfBattle | 战利品 | Skill | play-delta | forge, draw | OK | Clean. Forge 5 (U+8) + draw 2. | none |
| `CARD.THINKING_AHEAD` ThinkingAhead | 深谋远虑 | Skill | play-delta | draw, selectCards, moveCardBetweenPiles | OK | Clean. draw 2 + putback; U removes Exhaust. | none |
| `CARD.VENERATE` Venerate | 崇拜 | Skill | source-credit | starGain | OK | Clean. Gain 2 Stars (U+1). | none |

## Appendix - strategy groups with values (U S/M/L - U+ S/M/L)

### play-delta (23)

| Card | zh | Type | Terms | U S/M/L | U+ S/M/L |
|---|---|---|---|---|---|
| `CARD.PROPHESIZE` Prophesize | 预言 | Skill | draw | -5.7/-4.1/-14.8 | -6.6/-5.8/-19 |
| `CARD.DECISIONS_DECISIONS` DecisionsDecisions | 抉择，抉择 | Skill | starCost, draw, selectCards | -4/4.5/-17.6 | 0.1/13.7/12.2 |
| `CARD.QUASAR` Quasar | 类星体 | Skill | starCost, createCardChoices, selectCards, createCard | 3.6/0.6/-13.7 | 9.9/10.5/4.3 |
| `CARD.DISCOVERY` Discovery | 发现 | Skill | selectCards, createCard | -2.3/2.9/9.1 | -3.1/-3.1/-12 |
| `CARD.SPLASH` Splash | 飞溅 | Skill | selectCards, createCard | -3.1/-3.1/-12 | -3.1/-3.1/-12 |
| `CARD.GUIDING_STAR` GuidingStar | 引导之星 | Attack | starCost, damage, draw | 3.5/3.7/-9.6 | 5.7/6.8/-4.1 |
| `CARD.GLIMMER` Glimmer | 微光 | Skill | draw, selectCards, moveCardBetweenPiles | -3.5/-3.7/-8.9 | -2.1/-1.8/-7 |
| `CARD.PHOTON_CUT` PhotonCut | 光子切割 | Attack | damage, draw, moveCardBetweenPiles, selectCards | 2.4/-0.1/-8.5 | 7.6/5.7/-3.9 |
| `CARD.JACKPOT` Jackpot | 大奖 | Attack | damage, createCard | 7.6/7.3/-2.2 | 11.6/8.8/-5.2 |
| `CARD.COSMIC_INDIFFERENCE` CosmicIndifference | 宇宙冷漠 | Skill | block, selectCards, moveCardBetweenPiles | 1.6/1.6/-5 | 5.3/4.5/-2.3 |
| `CARD.BUNDLE_OF_JOY` BundleOfJoy | 新生之喜 | Skill | createCard | 6.6/13.9/-4.9 | 6.6/13.9/-4.9 |
| `CARD.HEIRLOOM_HAMMER` HeirloomHammer | 传承之锤 | Attack | damage, selectCards, createCard | 9.2/8/-4.6 | 13.2/11.3/-3.7 |
| `CARD.MANIFEST_AUTHORITY` ManifestAuthority | 君权自授 | Skill | block, createCardChoices, createCard | 10.1/11/-2.1 | 17.9/21/11.8 |
| `CARD.FOREGONE_CONCLUSION` ForegoneConclusion | 既定事项 | Skill | power | -1.1/5.2/6.1 | -0.5/5.4/2.5 |
| `CARD.PURITY` Purity | 净化 | Skill | selectCards, moveCardBetweenPiles | 0/5/18.4 | -0.5/5.9/16.8 |
| `CARD.SEEKER_STRIKE` SeekerStrike | 探寻打击 | Attack | damage, selectCards, moveCardBetweenPiles | 7.8/8.5/-0.4 | 10.8/11.2/2.7 |
| `CARD.SHINING_STRIKE` ShiningStrike | 明耀打击 | Attack | damage, starGain, moveCardBetweenPiles | 1.5/1.5/0.3 | 4.6/4.2/0.8 |
| `CARD.THINKING_AHEAD` ThinkingAhead | 深谋远虑 | Skill | draw, selectCards, moveCardBetweenPiles | 5.3/16.6/37.1 | 0.7/2.3/5.7 |
| `CARD.JACK_OF_ALL_TRADES` JackOfAllTrades | 花样百出 | Skill | createCard | 1/8.1/26 | 1/8.1/26 |
| `CARD.GLOW` Glow | 辉光 | Skill | starGain, draw, drawNextTurn, power | 1.1/7/5.3 | 2.3/10.4/11.1 |
| `CARD.SPOILS_OF_BATTLE` SpoilsOfBattle | 战利品 | Skill | forge, draw | 2.7/8.2/9.7 | 4.6/11.6/13.5 |
| `CARD.BEGONE` Begone | 下去！ | Skill | selectCards, transformCard | 2.9/5.6/3.6 | 5.9/8.3/6.3 |
| `CARD.SUMMON_FORTH` SummonForth | 征召上前 | Skill | forge, moveCardBetweenPiles | 4.1/15/26.6 | 6.4/18.8/30.7 |

### source-credit (23)

| Card | zh | Type | Terms | U S/M/L | U+ S/M/L |
|---|---|---|---|---|---|
| `CARD.BEAT_DOWN` BeatDown | 狠揍 | Skill | none (no facts parsed) | 0/0/0 | 0/0/0 |
| `CARD.CALAMITY` Calamity | 劫难 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.CATASTROPHE` Catastrophe | 横祸 | Skill | none (no facts parsed) | 0/0/0 | 0/0/0 |
| `CARD.ENTROPY` Entropy | 熵 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.HIDDEN_GEM` HiddenGem | 未掘宝石 | Skill | none (no facts parsed) | 0/0/0 | 0/0/0 |
| `CARD.PALE_BLUE_DOT` PaleBlueDot | 暗淡蓝点 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.PROLONG` Prolong | 延伸 | Skill | power | 0/0/0 | 0/0/0 |
| `CARD.RESONANCE` Resonance | 共鸣 | Skill | starCost, power, power | 0/0/0 | 3.9/8.4/12.7 |
| `CARD.SCRAWL` Scrawl | 潦草急就 | Skill | - | 0/0/0 | 0/0/0 |
| `CARD.SPECTRUM_SHIFT` SpectrumShift | 光谱偏移 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.STRATAGEM` Stratagem | 计策 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.TYRANNY` Tyranny | 暴政 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.VOID_FORM` VoidForm | 虚空形态 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.MONOLOGUE` Monologue | 独白 | Skill | power | 2.8/2.8/2.4 | 2.8/2.8/2.4 |
| `CARD.ARSENAL` Arsenal | 武器库 | Power | power | 3.2/22.2/81.1 | 6/34/106.2 |
| `CARD.VENERATE` Venerate | 崇拜 | Skill | starGain | 3.8/8.6/15.4 | 5/12.1/24.5 |
| `CARD.RADIATE` Radiate | 辐射 | Attack | damage, damage | 3.9/3.9/3.9 | 5.2/5.2/5.2 |
| `CARD.LUNAR_BLAST` LunarBlast | 月面射击 | Attack | damage, damage | 4/4/4 | 5/5/5 |
| `CARD.STARDUST` Stardust | 星尘 | Attack | damage | 4.3/4.3/4.3 | 6/6/6 |
| `CARD.FURNACE` Furnace | 熔炉 | Power | power | 4.8/30.9/134.5 | 7.1/44.9/198.7 |
| `CARD.GENESIS` Genesis | 创世纪 | Power | power | 5/34.9/157.6 | 6/42.8/202.9 |
| `CARD.HIDDEN_CACHE` HiddenCache | 隐秘藏品 | Skill | starGain, starNextTurn, power | 5.1/13.5/29.4 | 5.5/15.1/33.3 |
| `CARD.PARRY` Parry | 招架 | Power | power | 5.5/21.5/64.1 | 8/30.6/91.7 |

### excluded (7)

| Card | zh | Type | Terms | U S/M/L | U+ S/M/L |
|---|---|---|---|---|---|
| `CARD.ALCHEMIZE` Alchemize | 炼制药水 | Skill | - | 0/0/0 | 0/0/0 |
| `CARD.ANOINTED` Anointed | 天选 | Skill | moveCardBetweenPiles | 0/0/0 | 1.2/1.2/1.2 |
| `CARD.ROYALTIES` Royalties | 王国资产 | Power | power | 0/0/0 | 0/0/0 |
| `CARD.MAYHEM` Mayhem | 乱战 | Power | power | 1.6/1.6/1.6 | 1.6/1.6/1.6 |
| `CARD.MONARCHS_GAZE` MonarchsGaze | 王之凝视 | Power | power | 1.6/1.6/1.6 | 1.6/1.6/1.6 |
| `CARD.NOSTALGIA` Nostalgia | 怀旧 | Power | power | 1.6/1.6/1.6 | 1.6/1.6/1.6 |
| `CARD.BOLAS` Bolas | 流星锤 | Attack | damage, moveCardBetweenPiles | 3/3/3 | 4/4/4 |

## Recommended parser / model fixes (prioritized)

1. **Computed hit-count scaling** (`CardFactParser`): recognize `.WithHitCount((int)((CalculatedVar)DynamicVars["CalculatedHits"]).Calculate(...))` and the paired `CalculatedVar(...).WithMultiplier(...)`, emitting a `scalingDamagePer<...>` action (per skill-played / per star-gained-this-turn). Add a matching simulator channel like the existing `XCostHitCount`. Fixes **LunarBlast, Radiate**.
2. **`CardCmd.AutoPlay` loops** (`CardFactParser` + builder): recognize auto-play-N-from-pile as an action and model it as replay of sampled pile cards. Fixes **BeatDown, Catastrophe**, and DecisionsDecisions' auto-play half.
3. **`RepeatVar(N)`** (`CardFactParser`): add a dynamic-var + multiplier so repeat counts on clone/auto-play are captured. Fixes **DecisionsDecisions**; hardens HeirloomHammer.
4. **State-sourced draw count** (`CardFactParser`): emit a `draw` action for `CardPileCmd.Draw(ctx, <expr>)` even when the count is computed (`MaxCardsInHand - Hand.Count`), tagged variable. Fixes **Scrawl** (also flips it to play-delta).
5. **`BaseReplayCount +=` / `IntVar("Replay")`**: recognize the enchant/replay-grant construct. Fixes **HiddenGem**.
6. **Power-effect propagation into strategy** (builder): for installed powers whose downstream effect is draw/create/transform/tutor/cost-reduction, surface that as an incomplete-attribution term so `auto` selects **play-delta** instead of source-credit-then-zero. Fixes **Calamity, SpectrumShift, Stratagem, Tyranny, PaleBlueDot, Entropy, VoidForm**.
7. **`PlayerCmd.EndTurn`** (`CardFactParser`): capture forced end-of-turn as a downside term. Fixes **VoidForm** correctness.
8. **Self-return detection** (builder): treat `CardPileCmd.Add(this, ...)` as a replay/self-return marker rather than a generic `moveCardBetweenPiles`, so the card is not forced off source-credit. Fixes **ShiningStrike, Bolas**.
9. Minor / cosmetic: `SetToFreeThisTurn` (Discovery/Splash), Discard-source retrieval (CosmicIndifference), hover-tip `Retain` (SummonForth), createCard count vs 1 (Jackpot), spurious move label (Purity). Low value; batch when convenient.

> Not a fix: **Parry** was investigated and its simulator crediting is correct (SovereignBlade-gated), so no action is needed - its value is just synergy-conditional.

> Items 1-5 and 7 are **extraction (词条化) gaps**; item 6 is a **strategy-classification** gap driven by extraction; item 8 is **simulation-model** semantics; item 9 is cosmetic. None of these are float/seed drift.