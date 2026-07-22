# Combat-aware 信息状态模拟器 Phase 1 实装评审

日期：2026-07-22
范围：A-G 离线实现、首轮 coverage 扩展、card-instance/Forge 与选择 continuation 候选
结论：**实现切片已落地；训练切换与 runtime 集成 No-Go**

## 1. 结论先行

Phase 1 已建立一条与旧 `DeckMonteCarloSimulator` 分离的 combat-aware 离线路径：

- 真实玩家/怪物 HP、格挡、死亡、目标、Weak/Vulnerable/Frail 和 A10 intent；
- 只按可见信息决策的 decision/chance AND/OR solver；
- 未知牌堆按 multiset 精确展开，当前 intent 可见，未来随机 intent 到 chance node
  才揭示；
- `1 actual enemy HP lost = 1 value`，格挡、破甲、overkill 和 unused block 不直接
  定价；
- Act 1/2/3 x Weak/Normal/Elite/Boss 十二格、4/8/12 独立 horizon、paired deck dEV、
  CI/ESS/风险/覆盖率报告；
- 可逆 mutation journal、128-bit canonical fingerprint、memo、显式 exact budget，
  exact 超限不会偷偷退化成 branch-one 或正常值。

但是，真实数据 coverage gate 明确失败：支持的目标权重质量为 **0.0%**，而门槛是
80%；最近一次完整执行的 semantics v6 中，316 个历史 deck snapshot 只有 5 个能被
严格物理切片完整编译。因此本轮
artifact 只能用于检查架构、物理语义、HP 先验敏感性和缺口，不能成为正式训练值。

当前工作树已继续推进到 semantics v7 候选：card instance 与静态 Forge 已完成执行
验证；Cosmic Indifference/Glimmer/Headbutt/Thinking Ahead 的“一张可见牌置于抽牌堆顶”
已改成抽牌 chance 之后的显式 pending choice，而不是静态分数代选。v7 编译为 0 error/
0 warning，但新生成的 Modeling DLL 被本机 Application Control 拦截，因此本报告的
coverage/smoke 数字仍固定为最后一个完整跑通的 v6 checkpoint，不把未执行候选冒充
验收通过。

## 2. A-G 交付状态

| 阶段 | 已实装内容 | 验收结论 |
| --- | --- | --- |
| A | 独立 Combat options/state/action/config；root csproj 排除整个 Combat 离线目录；四个 CLI 命令 | MSBuild compile/resource item 均为 0 个 Combat 匹配；当前 root build 被既存 runtime 脏改的 CS0161 阻断，见第 7 节 |
| B | raw action card compiler、实际 HP/格挡/多段伤害、状态、治疗、自伤、可逆 journal | 物理/toy/10,000 次 apply-undo 测试覆盖 |
| C | parser 按源码 index 保留 effect 顺序；A10 monster compiler；sourced monster/encounter override；unknown strict fail | 121 monster profile、143 张 exact matrix 链与 selected replay 已生成/核对 |
| D | information-state key、decision/chance 分层、精确 multiset draw、memo on/off、known-top、max-E、显式 state/chance budget | toy oracle 与隐藏牌序测试覆盖 |
| E | 十二格 budget-knee HP prior、death-dominance bound、只回传未来 HP 风险的 reference tail、敏感性 artifact | 仅 prior；不允许 empirical/approved/runtimeCandidate |
| F | 十二格 selector、stage deck/encounter plan、semantic streams、baseline cache、importance aggregation、paired CI、4/8/12 independent run | 基础设施完成；真实 supported sample 为 0，统计估计被阻断 |
| G | 1/2/4 worker benchmark、coverage artifact、200-run ownership 回归、综合 Go/No-Go | **No-Go**；扩覆盖和正式同语义性能对照后再评审 |

## 3. 当前 EV 标准

一条轨迹的基础回报是：

```text
offense = actualEnemyHpLost - actualEnemyHpRestored
hp      = Phi(playerHpFinal) - Phi(playerHpStart)
EV      = offense + hp
```

这统一了此前互相冲突的情况：

- 敌人格挡吸收的伤害没有减少敌人 HP，因此不产生攻击价值；
- overkill 和打向死亡目标的后续 hit 都是 0；
- 敌人治疗会扣回此前的进攻进度；
- 玩家格挡本身是 0，只有它实际避免 HP 损失时，终态 `Phi(HP)` 才产生防御价值；
- 超额格挡和战斗结束后的剩余格挡是 0；
- 击杀没有任意 victory bonus。击杀的价值来自已造成的实际 HP 损失，以及取消未来
  怪物行动后保住的 HP；
- horizon 后的 reference tail 只回传未来玩家 HP/死亡风险，不补发未来攻击价值，
  避免 4/8/12 节奏差被抵消。

当前 HP prior 使用 soft knee：

```text
loss   = maxHp - hp
excess = max(0, loss - lossBudget)
cost   = lambdaSafe * loss + kappa * excess^2
Phi    = -cost
```

`lossBudget` 是用户确认的风险膝点，不是免罚额度、掉血目标或硬上限。所有
`lambdaSafe`、`kappa`、death future reserve 数字仍是敏感性 prior。正式校准应使用
包含失败局的逐战斗 HP 轨迹，并联合 HEAL/SMITH、商店/事件 HP 交易等 revealed
preference；不能只用胜利局平均掉血反推单一 HP 价格。

建议后续把经验拟合目标设为“下一阶段存活/资源机会损失”，允许低损血近似线性、
接近风险区后凸增，而不是对每一点 HP 使用固定常数。P90/CVaR、超 budget 概率和死亡
概率继续作为报告维度，不在 Phase 1 目标里再叠一个风险罚项，以免重复计价。

## 4. 真实覆盖结果

| 项目 | 支持数 | 总数 | 结果 |
| --- | ---: | ---: | --- |
| card forms | 488 | 1156 | 部分支持（v6 已执行） |
| monsters | 48 | 121 | 部分支持 |
| encounter act-realizations | 11 | 80 | 严重不足 |
| history deck snapshots | 5 | 316 | 仍阻断所有十二格 paired sample |
| supported target-weight mass | 0.0% | 要求 >= 80% | No-Go |

十二格里只有 Act 1 Weak/Normal/Elite 的 fully-supported encounter 数达到每格至少 2
的 encounter 条件；但这些格同样没有完整支持的 deck，所以十二格 sample 支持率全部
是 0%。主要 blocker：

1. 历史 deck 普遍仍含未实现的 Power、生成、多选、选择/移动/变形等动作；
2. 大量 monster state 有 2-4 个 follow-up，但缺源码可证概率；
3. 21 个 monster 的 initial move 未解析；
4. Act 2/3 weak/elite/boss 的 fully-supported encounter 数不足；
5. stage-matched deck 来源目前是胜利历史 smoke 数据，不满足 failure-inclusive 校准。

报告没有把这些缺失质量重新分配给少数 supported encounter，因此结果是 0，而不是
一个看起来可用但代表错分布的 dEV。

### 4.1 首轮 coverage 扩展

严格支持数按同一 1,156 个 card form、316 个历史 snapshot 逐步变化：

| 语义切片 | card forms | complete decks |
| --- | ---: | ---: |
| A-G 初始基线 | 410 | 0 |
| Star cost/gain/next-turn | 444 | 1 |
| next-turn draw/energy/block | 462 | 1 |
| Retain/Ethereal/Innate/RetainHand | 468 | 1 |
| ChildOfTheStars/Orbit/PaleBlueDot/Fasten | 476 | 1 |
| stable card instance + static Forge | 488 | 5 |

最近一次完整执行的 combat semantics version 为 6。所有新增机制都改变物理状态和未来
合法行动，不增加
静态价值项：`ChildOfTheStars` 在真实 Star 支出后获得 unpowered block；每个 `Orbit`
实例独立保存 0-3 能量进度；`PaleBlueDot` 只在已安装时保存上一回合出牌数；`Fasten`
只修正带 Defend 标签的实际格挡。未知新关键字继续 fail closed。

v6 为每张牌分配稳定 instance id，并验证每个 instance 恰好属于 Hand、KnownTop、
UnknownDraw、Discard、Exhaust、Play 之一。canonical key 不包含任意标签，只包含会改变
未来的 physical key（当前为 definition + Forge damage）；相同 physical key 的副本仍按
组合概率合并，只有 Forge 状态不同才拆分分支。这样既保留实例语义，也不产生“同一张
Strike 因 instance id 不同而分叉”的性能灾难。

静态 Forge 的源码语义已经落地：若没有非 Exhaust 的 Sovereign Blade，则创建一张未
升级 Blade 到 Hand（满手时到 Discard）；随后同时增加所有 Blade 的实例伤害，包括
Exhaust 中的旧 Blade。已执行支持的是 Big Bang、Bulwark、Refine Blade、Spoils of
Battle、The Smith、Wrought in War 的 12 个 form。Conqueror 仍因其专属 Power
unsupported；Beat Into Shape 的 Forge 依赖本回合对目标造成的真实攻击命中历史，也
明确 fail closed，不能把 parsed 0 当成静态 Forge。

工作树 v7 候选再增加 `PendingCardSelection`：Glimmer/Thinking Ahead 先完成抽牌 chance，
再在实际新手牌上分支；Cosmic Indifference/Headbutt 从真实 Discard 选实例；选择后保留
该实例的 mutable state 并插入 KnownTop 的真实 top。出牌卡在选择完成前停留在 Play，
因此不能错误地选择自己。该候选已通过编译及静态源码核对，但尚未计入上表。

## 5. 性能结果与边界

普通 wall benchmark（相同新 solver fixture，8 solves；不是 profiler wall）：

| workers | throughput solves/s | p50 ms | p95 ms | p99 ms | allocated/solve | states | memo hits | chance branches |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1.323 | 704.364 | 996.926 | 996.926 | 48,410,543 | 13,960 | 22,817 | 70,021 |
| 2 | 2.893 | 655.146 | 748.748 | 748.748 | 47,776,696 | 13,960 | 22,817 | 70,021 |
| 4 | 5.061 | 685.508 | 807.717 | 807.717 | 47,779,083 | 13,960 | 22,817 | 70,021 |

单个 solver 不做内层并行；workers 只并行独立 solve，默认最多 4。当前实现的热路径
没有完整 state clone，并复用 action buffer/draw outcome/chance outcome/terminal cache。

这里不能宣称已经满足“比旧 solver p95 快 3x、allocation 降 10x”：旧 solver 没有
同一 combat 语义，尚无可声明为 oracle-equivalent 的 fixture。当前数字证明新路径可测、
可复现和可横向扩展，但正式性能 gate 仍需建立同语义 oracle suite；这是 No-Go 的一
部分，不用不等价 benchmark 冒充结论。

同一 10 张纯 Strike/Defend smoke deck、同一 seed 和真实 A10 Fuzzy Wurm Crawler 的
end-to-end 结果更接近当前实际瓶颈：

| horizon | solver | value | actual damage | player HP loss | states | branches | wall ms | allocated |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | Exact | 33.944 | 52.065 | 12.783 | 12,648 | 75,451 | 1,340 | 24,704,000 |
| 8 | Exact | 49.392 | 59 | 6.379 | 193,312 | 955,658 | 14,973 | 373,089,680 |
| 12 | Exact | 49.392 | 59 | 6.379 | 415,792 | 1,589,050 | 28,055 | 746,353,816 |

wall time 会受本机负载影响，不能把单次 2026-07-21 与 2026-07-22 数字当回归结论；
states、branches 和 allocation 才是本轮语义扩展的稳定防线。一次中间实现曾因无条件把
“上一回合出牌数”放入 canonical key，使 4 回合 states 从 12,648 增至 26,628、12 回合
超预算；改成只在 `PaleBlueDot` 已安装时记录，并移除热路径 Orbit 排序数组后，上表三组
states/branches 与扩展前完全一致。

card-instance v6 后，三组 states/branches 仍逐项不变；allocation 则从此前约
40.8/525.7/975.4 MB 降到 24.7/373.1/746.4 MB（约 -39%/-29%/-23%）。主要结构变化是
chance outcome 不再为每个结果建立 definition-count dictionary，而保存可逆的实例列表，
同时仍按 physical key 聚合概率。这个结果证明“实例正确性必然导致分支爆炸”并不成立；
但 wall 仍受本机负载影响，12 回合 exact 仍然很慢，不能据单次 wall 宣称性能 gate 已过。

所以 Phase 1 的性能判断不是“已经解决”，而是：短线核心状态操作已比初版轻很多，
但 exact 长 horizon 的 chance/state 展开仍是数百 MB/fixture。下一阶段必须在保持
information-state 正确性的前提下做抽象、上界/下界或显式 sparse 方案，不能回到
隐藏牌序 determinization 或 branch-one 冒充 exact。

## 6. 离线 artifact

以下均位于 ignored 的 `data/generated/combat_aware/`，只供本地评审：

- `phase1_coverage.generated.json` / `phase1_coverage.md`
- `phase1_smoke.generated.json` / `phase1_smoke.md`（同一物理 deck/seed 的 4/8/12
  end-to-end fixture）
- `latest.generated.json` / `latest.md`（当前候选 `CARD.STRIKE_REGENT+0`；因 0 样本，
  4/8/12 `primaryDeltaEv` 均为 null）
- `hp_continuation_sensitivity.generated.json` / `.md`
- `information_state_solver_benchmark.generated.json` / `.md`
- `baselines/`（仅在存在可运行 sample 时写入）

所有 Phase 1 report 均固定 `runtimeCandidate: false`。本轮没有修改
`CardValueOverlay/data/card_values.json`，没有训练 cutover、publish 或游戏启动。

## 7. 最终验证状态

- `CardValueOverlay.Core.Tests`：通过。
- semantics v6 的 `CardValueOverlay.Modeling.Tests`：通过；包含 10,000 次 apply/undo、200-run
  ownership、概率归一、memo on/off、budget、4/8/12 batch equality 和
  zero/positive/negative toy dEV；新增 Star、延迟资源、手牌生命周期、Innate 初始抽牌、
  typed Power、多 Orbit、instance ownership/exchangeability、Forge 创建/全区域 mutation
  与实际 Blade 伤害回滚测试。
- semantics v7 当前工作树：`CardValueOverlay.Modeling.Tests` build 成功，0 warning/0 error；
  新增 Cosmic/Glimmer 显式 choice、抽牌后 continuation、exact instance move/top insertion
  与 apply/undo 测试。执行阶段被 WDAC 以 `0x800711C7` 拦截新生成的
  `CardValueOverlay.Modeling.dll`，Code Integrity 3033/3077 指向同一 policy
  `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`，所以这里不写“测试通过”。
- Tools `validate`：通过。
- Tools `validate-generated-data`：通过（保留既有低置信 parser warning）。
- `replay-monster-intents --encounter FuzzyWurmCrawlerWeak --turns 8`：与 exact
  damage matrix 逐状态/A10 damage 匹配。
- 生成数据复核：121 个 monster profile；85 个 matrix encounter、143 张表全部
  `mode=exact`，没有 all-zero table。
- root project 的 evaluated `Compile` 与 runtime resource item 对
  `CardValueOverlay.Modeling/Combat`、三套 Combat config 的匹配均为 0。
- root `dotnet build CardValueOverlay.csproj --no-restore` 当前未通过：既存、非本批
  Phase 1 修改的 `CardValueOverlayCode/Runtime/RealtimeEvService.cs:1009`
  `ComputeBaselineOnly` 有 `CS0161`（catch 后存在无返回路径）。本轮按范围约束没有
  修改或覆盖用户正在进行的 runtime 工作；因此只能确认 MSBuild 隔离，不能声称
  当前整个脏 worktree root build 为绿。
- 本机 WDAC 在 2026-07-22 以 Code Integrity 3033/3077 阻止新生成的
  `CardValueOverlay.Tools.dll`（企业签名策略）；coverage/smoke 使用同一公开 Modeling API
  从当时已获准的测试宿主执行并写出标准 v6 artifact。临时诊断入口在运行后已删除。
  v7 重建后同一策略进一步拦截 Modeling DLL，因此 v7 artifact 尚未生成；没有复制、
  改签或绕过策略。

## 8. 下一轮推荐顺序

1. 先让 Application Control 接受正常构建产物，执行 v7 新增测试并重跑严格 coverage 与
   4/8/12 smoke；任何 496/1156 等候选覆盖推算在此之前都不写成实测结果。
2. v7 通过后，在同一 pending-choice/instance 基础上实现 Charge 的“从 Draw 精确选择 2
   张并 TransformTo Minion Dive Bomb”；选择集合必须去除排列重复，且不能观察隐藏顺序。
3. 再扩展多选、Hand/Discard/Draw 的其他 move/transform 与生成牌事件；动态 Forge
   Beat Into Shape 必须先增加真实伤害历史状态，不能套静态 Forge。
4. 修复 21 个 initial intent parser 缺口，重跑 monster profile -> turn actions ->
   damage details -> matrix strict 链。
5. 生成真正 stage-matched、failure-inclusive 的 deck/HP snapshot；避免要求整副最终
   deck 的每张牌都先可模拟才获得任何样本，可按“支持切片 deck cohort”明确建组，
   但不得删除其目标质量。
6. 达到每格至少 2 个 fully-supported encounter、每格 >=70% 和总体 >=80% target
   mass 后，再运行 paired dEV 和 CI accuracy gate。
7. 最后用失败局轨迹 + HEAL/SMITH/HP trade 拟合 `Phi`；用户批准 primary weights 和
   HP calibration 后，才讨论训练 cutover。runtime dEV 仍是之后的独立 gate。

因此，当下应继续扩物理/怪物/样本覆盖，而不是现在调 `lambdaSafe/kappa`，更不应把
当前 0-mass report 安装为卡值。
