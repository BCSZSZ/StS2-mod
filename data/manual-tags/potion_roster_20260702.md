# 药水全量清单 . Potion Roster - 2026-07-02

来源：`data/extracted/potion_facts.generated.json`（由 `parse-potions` 从反编译源解析，64 个药水）。英/中名取自 `history-analysis/data/localized_names_en_zhs.json`（游戏官方本地化）。

**效果通道**=已有模拟价值通道（damage/block/draw/energy/star/forge/vulnerableWeak/powerInstall/cardGeneration/cardAutoPlay/aoe）；**需新通道**=模拟器目前没有对应通道的效果（heal/maxHp/orb/summon/gold/potionGeneration/cardManipulation/power:X）。**进随机池**=可被战斗内随机生成（Alchemize 抽取来源，= 共享/角色池 且 可战斗生成 且 普通/罕见/稀有）。

统计：共 **64** 个；进随机池 **57** 个；含"需新通道"效果 **42** 个。不可战斗生成（`CanBeGeneratedInCombat=false`）：FairyInABottle、FruitJuice、RegenPotion。

| # | English | 中文 | TypeName | 稀有度 | 使用时机 | 目标 | 生成池 | 可战斗生成 | 进随机池 | 效果通道 | 需新通道 | 数值 |
|---:|---|---|---|---|---|---|---|:-:|:-:|---|---|---|
| 1 | Attack Potion | 攻击药水 | AttackPotion | Common | CombatOnly | Self | Shared | Y | Y | cardGeneration | cardManipulation |  |
| 2 | Block Potion | 格挡药水 | BlockPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | block |  | Block:12 |
| 3 | Blood Potion | 鲜血药水 | BloodPotion | Common | AnyTime | AnyPlayer | Ironclad | Y | Y |  | heal |  |
| 4 | Colorless Potion | 无色药水 | ColorlessPotion | Common | CombatOnly | Self | Shared | Y | Y | cardGeneration | cardManipulation |  |
| 5 | Dexterity Potion | 敏捷药水 | DexterityPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | powerInstall |  | Dexterity:2 |
| 6 | Energy Potion | 能量药水 | EnergyPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | energy |  | Energy:2 |
| 7 | Explosive Ampoule | 爆炸安瓿 | ExplosiveAmpoule | Common | CombatOnly | AllEnemies | Shared | Y | Y | aoe, damage |  | Damage:10 |
| 8 | Fire Potion | 火焰药水 | FirePotion | Common | CombatOnly | AnyEnemy | Shared | Y | Y | damage |  | Damage:20 |
| 9 | Flex Potion | 肌肉药水 | FlexPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | powerInstall | power:FlexPotion | Strength:5 |
| 10 | Focus Potion | 集中药水 | FocusPotion | Common | CombatOnly | Self | Defect | Y | Y |  | power:Focus | Focus:2 |
| 11 | Poison Potion | 毒药水 | PoisonPotion | Common | CombatOnly | AnyEnemy | Silent | Y | Y |  | power:Poison | Poison:6 |
| 12 | Potion of Doom | 灾厄药水 | PotionOfDoom | Common | CombatOnly | AnyEnemy | Necrobinder | Y | Y |  | power:Doom | Doom:33 |
| 13 | Power Potion | 能力药水 | PowerPotion | Common | CombatOnly | Self | Shared | Y | Y | cardGeneration | cardManipulation |  |
| 14 | Skill Potion | 技能药水 | SkillPotion | Common | CombatOnly | Self | Shared | Y | Y | cardGeneration | cardManipulation |  |
| 15 | Speed Potion | 速度药水 | SpeedPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | powerInstall | power:SpeedPotion | Dexterity:5 |
| 16 | Star Potion | 星星药水 | StarPotion | Common | CombatOnly | Self | Regent | Y | Y | star |  | Stars:3 |
| 17 | Strength Potion | 力量药水 | StrengthPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | powerInstall |  | Strength:2 |
| 18 | Swift Potion | 迅捷药水 | SwiftPotion | Common | CombatOnly | AnyPlayer | Shared | Y | Y | draw |  | Cards:3 |
| 19 | Vulnerable Potion | 易伤药水 | VulnerablePotion | Common | CombatOnly | AnyEnemy | Shared | Y | Y | vulnerableWeak |  | Vulnerable:3 |
| 20 | Weak Potion | 虚弱药水 | WeakPotion | Common | CombatOnly | AnyEnemy | Shared | Y | Y | vulnerableWeak |  | Weak:3 |
| 21 | Ashwater | 灰水 | Ashwater | Uncommon | CombatOnly | Self | Ironclad | Y | Y |  | cardManipulation |  |
| 22 | Blessing of the Forge | 熔炉的祝福 | BlessingOfTheForge | Uncommon | CombatOnly | Self | Shared | Y | Y |  | cardManipulation |  |
| 23 | Bone Brew | 骨头酿 | BoneBrew | Uncommon | CombatOnly | Self | Necrobinder | Y | Y |  | summon | Summon:15 |
| 24 | Clarity Extract | 明晰提取物 | Clarity | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y | draw | power:Clarity | Clarity:3, Cards:1 |
| 25 | Cunning Potion | 狡诈药水 | CunningPotion | Uncommon | CombatOnly | Self | Silent | Y | Y | cardGeneration | cardManipulation | Cards:3 |
| 26 | Cure All | 痊愈药水 | CureAll | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y | draw, energy |  | Cards:2, Energy:1 |
| 27 | Duplicator | 复制药水 | Duplicator | Uncommon | CombatOnly | Self | Shared | Y | Y |  | power:Duplication |  |
| 28 | Fortifier | 固化药水 | Fortifier | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y | block |  |  |
| 29 | Fysh Oil | 异鱼之油 | FyshOil | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y | powerInstall |  | Strength:1, Dexterity:1 |
| 30 | Gambler's Brew | 赌徒特酿 | GamblersBrew | Uncommon | CombatOnly | Self | Shared | Y | Y |  | cardManipulation |  |
| 31 | Heart of Iron | 铁心药水 | HeartOfIron | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:Plating | Plating:7 |
| 32 | King's Courage | 王之勇气 | KingsCourage | Uncommon | CombatOnly | AnyPlayer | Regent | Y | Y | forge |  | Forge:15 |
| 33 | Liquid Bronze | 流动铜液 | LiquidBronze | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:Thorns | Thorns:3 |
| 34 | Potion of Binding | 缚魂药水 | PotionOfBinding | Uncommon | CombatOnly | AllEnemies | Shared | Y | Y | vulnerableWeak |  | Vulnerable:1, Weak:1 |
| 35 | Potion of Capacity | 扩容药水 | PotionOfCapacity | Uncommon | CombatOnly | Self | Defect | Y | Y |  | orb | Repeat:2 |
| 36 | Powdered Demise | 消亡粉末 | PowderedDemise | Uncommon | CombatOnly | AnyEnemy | Shared | Y | Y |  | power:Demise |  |
| 37 | Radiant Tincture | 明耀酊剂 | RadiantTincture | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y | energy | power:Radiance | Radiance:3, Energy:1 |
| 38 | Regen Potion | 再生药水 | RegenPotion | Uncommon | CombatOnly | AnyPlayer | Shared | N | N |  | power:Regen | Regen:5 |
| 39 | Stable Serum | 稳定血清 | StableSerum | Uncommon | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:RetainHand | Repeat:2 |
| 40 | Touch of Insanity | 癫狂之触 | TouchOfInsanity | Uncommon | CombatOnly | Self | Shared | Y | Y |  | cardManipulation |  |
| 41 | Beetle Juice | 甲虫汁 | BeetleJuice | Rare | CombatOnly | AnyEnemy | Shared | Y | Y |  | power:Shrink | Repeat:4 |
| 42 | Bottled Potential | 瓶装潜能 | BottledPotential | Rare | CombatOnly | AnyPlayer | Shared | Y | Y | draw | cardManipulation | Cards:5 |
| 43 | Cosmic Concoction | 宇宙药剂 | CosmicConcoction | Rare | CombatOnly | Self | Regent | Y | Y | cardGeneration | cardManipulation | Cards:3 |
| 44 | Distilled Chaos | 精炼混沌 | DistilledChaos | Rare | CombatOnly | Self | Shared | Y | Y | cardAutoPlay |  | Repeat:3 |
| 45 | Droplet of Precognition | 预知之滴 | DropletOfPrecognition | Rare | CombatOnly | Self | Shared | Y | Y |  |  |  |
| 46 | Entropic Brew | 混沌药水 | EntropicBrew | Rare | AnyTime | Self | Shared | Y | Y |  | potionGeneration |  |
| 47 | Essence of Darkness | 黑暗精华 | EssenceOfDarkness | Rare | CombatOnly | Self | Defect | Y | Y |  | orb |  |
| 48 | Fairy in a Bottle | 瓶中精灵 | FairyInABottle | Rare | Automatic | Self | Shared | N | N |  | heal |  |
| 49 | Fruit Juice | 果汁 | FruitJuice | Rare | AnyTime | AnyPlayer | Shared | N | N |  | maxHp | MaxHp:5 |
| 50 | Ghost in a Jar | 罐装幽灵 | GhostInAJar | Rare | CombatOnly | AnyPlayer | Silent | Y | Y |  | power:Intangible | Intangible:1 |
| 51 | Gigantification Potion | 超巨化药水 | GigantificationPotion | Rare | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:Gigantification | Gigantification:1 |
| 52 | Liquid Memories | 液态记忆 | LiquidMemories | Rare | CombatOnly | Self | Shared | Y | Y |  | cardManipulation |  |
| 53 | Lucky Tonic | 幸运补剂 | LuckyTonic | Rare | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:Buffer | Buffer:1 |
| 54 | Mazaleth's Gift | 马萨雷斯的赠礼 | MazalethsGift | Rare | CombatOnly | AnyPlayer | Shared | Y | Y |  | power:Ritual | Ritual:1 |
| 55 | Orobic Acid | 欧洛巴斯之酸 | OrobicAcid | Rare | CombatOnly | Self | Shared | Y | Y | cardGeneration | cardManipulation |  |
| 56 | Pot of Ghouls | 尸鬼瓮 | PotOfGhouls | Rare | CombatOnly | Self | Necrobinder | Y | Y | cardGeneration |  | Cards:2 |
| 57 | Shackling Potion | 镣铐药水 | ShacklingPotion | Rare | CombatOnly | AllEnemies | Shared | Y | Y | powerInstall | power:ShacklingPotion | Strength:7 |
| 58 | Ship in a Bottle | 瓶中船 | ShipInABottle | Rare | CombatOnly | AnyPlayer | Shared | Y | Y | block | power:BlockNextTurn | Block:10 |
| 59 | Snecko Oil | 异蛇之油 | SneckoOil | Rare | CombatOnly | AnyPlayer | Shared | Y | Y | draw |  | Cards:7 |
| 60 | Soldier's Stew | 士兵炖汤 | SoldiersStew | Rare | CombatOnly | AnyPlayer | Ironclad | Y | Y |  |  |  |
| 61 | Foul Potion | 污浊药水 | FoulPotion | Event | AnyTime | AllEnemies | Event | Y | N | aoe, damage | gold | Damage:12, Gold:100 |
| 62 | Glowwater Potion | 发光水 | GlowwaterPotion | Event | CombatOnly | Self | Event | Y | N | draw | cardManipulation | Cards:10 |
| 63 | Potion-Shaped Rock | 药水形状的石头 | PotionShapedRock | Token | CombatOnly | AnyEnemy | Token | Y | N | damage |  | Damage:15 |
| 64 | Deprecated Potion | 废弃药水 | DeprecatedPotion | None | CombatOnly | AnyEnemy | Deprecated | Y | N |  |  |  |
