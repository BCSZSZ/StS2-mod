# 信息状态模拟器重构路线与 Phase 1 计划

状态：总体方向与十二格战斗组合已确认；具体实装仍需按配套的 method-level
计划评审后实施。

范围：本文件记录模拟器整体重构方向，并详细定义选定的方案 1：
“信息状态 AND/OR DP + 怪物意图 + 基于实际 HP 变化的 EV”。本文件本身
不授权修改运行时卡值，也不表示旧模拟器已经可以删除。

## 1. 决策摘要

当前事件模拟和卡牌行为数据应保留，但当前求解器应重写。目标不是继续更快地
搜索一个预先知道完整牌序的确定化世界，而是：

1. 只在玩家真实可见的信息状态上选择动作。
2. 抽牌、随机生成、变换和未来怪物意图作为机会节点求期望。
3. 让真正等价的手牌、牌堆尾部和顺序状态汇合。
4. 用实际敌人 HP、敌人格挡、玩家 HP 和怪物行动替代固定防御价格。
5. 卡牌最终仍使用 deck-level `dEV = J(D + c) - J(D)`。

以下原则在 Phase 1 中固定，不再作为可调参数：

- `1 点实际造成的敌人 HP 伤害 = 1 价值`，不随层数、敌人或牌组缩放。
- 攻击先经过敌人格挡；只有实际减少敌人 HP 的部分立即得到伤害价值。
- 过量伤害不计价值；敌人死亡后不能继续产生伤害价值。
- 玩家格挡本身没有固定价值。只有实际避免玩家 HP 损失时才产生价值。
- 超额且会在回合末消失的玩家格挡没有价值。
- Weak、防御、自伤、治疗和最大 HP 通过同一套玩家 HP 续航价值函数进入 EV。
- combat-aware 求解器不得再读取 `blockToDamage` 作为格挡价值。
- 同一玩家可见信息必须产生同一动作，不能因未揭示牌序不同而改变。
- 不支持的怪物状态或卡牌机制必须显式失败或排除，不能静默按 0 处理。
- 正式卡值必须覆盖 Act 1/2/3 x Weak/Normal/Elite/Boss 共十二个战斗格，
  不能只在一个 starter deck 或一个平均敌人上估值。
- 十二格的抽样配额与最终聚合权重必须分离；稀有格可以过采样，但不得因此
  获得更高的实际卡值权重。

## 2. 当前架构判断

当前模拟器已经是“外层 Monte Carlo + 回合内递归顺序搜索”，并非随机出牌。
当前工作树的普通选择宽度为前四次 `3`、接着两次 `2`、之后 `1`，并带有
强制零费牌、Power、资源牌和多个嵌套 continuation。性能和牌序误差来自同一
结构：每个样本对完整隐藏状态反复展开局部 Top-B，并复制几乎整个战斗状态。

关键问题按优先级排列：

1. **隐藏信息泄漏**：搜索会在决定是否打抽牌/生成牌前，比较已经实现的随机
   结果，近似计算 `E[max Q(a, omega)]`，而不是合法的 `max E[Q(a, omega)]`。
2. **局部 Top-B 和强制顺序**：低即时收益的 enabler 可能在进入搜索树前被
   删掉；零费牌和 Power 的硬规则会改变正确牌序。
3. **完整状态复制**：普通候选、card-object preview、生成牌选择都会克隆状态
   并递归搜索，形成高分配和慢尾。
4. **跨回合均值场**：固定 draw/energy/star 价格和 future opportunity profile
   无法准确表达组合同时上手、retain、置顶和击杀时机。
5. **没有战斗物理状态**：当前没有玩家 HP、敌人 HP、当前意图和怪物行动状态，
   因此格挡和 Weak 只能使用层级常数。
6. **训练 horizon 语义不一致**：离线 shortline/midline 从同一个 12 回合策略
   前缀读取，不能代表分别以 4/8 回合为目标的最优策略。

现有数据也说明这不是单纯的 C# 常数问题：历史 depth-8 final-deck 诊断曾产生
约 210 万搜索节点、141 万状态克隆和 26.8 万 budget fallback；floor-49 在大幅
减少分配后仍约有 529 万节点。现有 pointwise ranker 和 state-value MLP 也在
晚期/engine-heavy deck 上产生负收益。继续降低 branch/depth 会改变 dEV，继续
增加特殊 preview 又会扩大搜索树，因此必须改变状态等价关系和规划语义。

## 3. 整体重构路线

### 3.1 三个可独立替换旧求解器的方向

| 方向 | 核心 | 主要用途 | 主要风险 |
| --- | --- | --- | --- |
| 方案 1：信息状态 AND/OR DP | canonical 信息状态、决策节点、机会节点、精确/稀疏期望 | 离线权威值、小中型状态 | chance/生成状态爆炸 |
| 方案 2：Information-Set MCTS/POMCP | 粒子 belief、PUCT、progressive widening、固定模拟预算 | 复杂战斗与实时规划 | 采样误差、rollout/value 偏差 |
| 方案 3：解析基线 + 分布式残差模型 | 解析求解器加 set/attention residual 和不确定度门控 | 批量训练值与 overlay 推理 | 依赖可信 teacher、OOD 风险 |

当前选择方案 1 作为新的权威数学基座。方案 2 仅在方案 1 对复杂生成/变换状态
爆炸后考虑；方案 3 必须等待可信 oracle 数据，不能继续使用当前有偏 beam 输出
作为最终真值。

#### 方案 1：信息状态 AND/OR DP

- 状态只保存玩家公开信息、已知有序顶部和未知牌等价类计数。
- 打牌/目标/choice 是决策节点；抽牌/生成/未来 intent 是机会节点。
- 封闭手牌使用 exact bitmask/multiset DP；chance 分支使用精确求和或有界
  sparse sampling。
- 跨回合显式结算敌人行动、HP、格挡和状态递减。
- 优点是可解释、可审计、不需要训练数据；风险是复杂生成和多敌人状态爆炸。

#### 方案 2：Information-Set MCTS/POMCP

- 从根 belief 采样隐藏牌序和未来怪物状态，但树节点按 action-observation
  history 汇合。
- 同一可见状态共享动作统计，未知结果揭示后才进入不同 observation child。
- PUCT 把固定模拟预算集中到有希望的路线；大生成池使用 progressive widening。
- 优点是固定预算和复杂 chance 扩展性；风险是 rollout/value 偏差和统计方差。

#### 方案 3：解析基线 + 分布式残差模型

- `J_high = J_analytic + residual_model`。
- 解析层负责合法的小手牌 DP、可交换抽牌、资源约束和怪物当前意图。
- residual 使用 hand/deck/enemy token 和 candidate-conditioned set/attention，
  输出均值、quantile、死亡风险和 epistemic uncertainty。
- 优点是大量查询时推理最快；风险是依赖可信 teacher，且新机制必须 OOD fallback。

长期可以由方案 1 生产 oracle、方案 2 处理方案 1 爆炸的状态、方案 3 提供服务层
加速，但三者必须共享同一 transition kernel 和价值语义，不能各维护一套卡牌
规则。

### 3.2 三个方向共享的底层边界

新架构应共享以下纯建模组件：

- `CombatWorldState`：完整真实状态，包括隐藏牌序和随机源，仅供 transition 和
  采样器使用。
- `CombatInformationState`：Planner 可见的手牌、已知顶部、有序公开信息、未知
  牌计数、资源、玩家/敌人状态和怪物 belief。
- `CombatAction`：出牌、目标、card-object 选择、generated choice、skip 和
  end turn，全部是一等动作。
- `CombatOutcome`：抽牌、随机生成、变换结果和未来怪物意图，全部是一等机会
  结果。
- `CombatTransitionKernel`：纯状态转移，不在卡牌效果内部启动另一个搜索器。
- `CombatRewardLedger`：只记录物理事实和来源，不在 transition 内混入启发式
  卡值。
- `Planner`：AND/OR DP、POMCP 或 surrogate 均通过同一接口消费状态和动作。
- `PairedDeckDeltaEstimator`：用相同 context 和语义随机流比较 `D` 与 `D + c`。

旧求解器和新求解器可以在验证期间通过显式工具参数并存，但最终切换后应删除
被替代路径，不能永久保留静默 fallback、双重价值规则或兼容分支。

## 4. Phase 1 范围

### 4.1 Phase 1 目标

Phase 1 要交付一个可以独立验证的 combat-aware 权威切片：

- 支持实际玩家/敌人 HP、格挡、死亡和目标选择。
- 从 Ascension 10 怪物 profile 读取当前意图和后续状态。
- 对支持范围内的确定性手牌使用精确信息状态 DP。
- 对抽牌和随机意图使用显式机会节点。
- 用非线性玩家 HP 续航价值函数替代固定 `blockToDamage`。
- 在 Act 1/2/3 x Weak/Normal/Elite/Boss 的十二格战斗组合中分层评估。
- 独立求解 4、8、12 回合目标。
- 输出 paired deck-level dEV、置信区间和解释性物理指标。
- 与旧求解器 side-by-side，不写入 `CardValueOverlay/data/card_values.json`。

### 4.2 Phase 1 非目标

以下内容不应为了扩大覆盖而塞入 Phase 1：

- 所有 summon、复杂阶段转换、复活、逃跑和多人机制。
- 全部 generated-card、transform、card-object 和无限资源链。
- 用神经网络替代 DP leaf。
- 自动覆盖运行时卡值。
- 根据胜利样本的平均掉血直接生成 HP 价格。
- 为了覆盖率静默猜测未知怪物分支概率。

### 4.3 十二格战斗组合

正式估值的 context 不是一个“平均战斗”，而是以下十二个明确分层。表中数字
是当前确认的 **可接受掉血预算先验** `B(act, tier)`：

| Act | Weak | Normal | Elite | Boss |
| --- | ---: | ---: | ---: | ---: |
| 1 | 0 | 8 | 15 | 30 |
| 2 | 5 | 10 | 20 | 40 |
| 3 | 10 | 15 | 30 | 40 |

这里的 `B` 有严格语义：

- 它是 HP 损失成本曲线开始明显变陡的 **soft knee**，不是要求策略必须掉到
  该数值。
- 它不是免费额度、硬上限或目标函数中的奖励。少掉 1 HP 永远不会比多掉
  1 HP 更差；求解器不得为了“用完预算”主动吃伤害。
- 它是该格战斗分布的设计先验，不要求每一场战斗的实际掉血都等于 `B`。
- `B` 决定曲线拐点的位置，不能单独决定 `1 HP` 的价值尺度。安全区边际价值、
  超预算曲率和死亡机会损失仍需由 HEAL/SMITH、HP 事件交易以及包含失败样本的
  战斗数据校准。

对起始 HP 为 `H0`、未来最低保留线为 `Hreserve` 的样本，可先定义：

```text
Beffective = min(B, max(0, H0 - Hreserve))
Hknee      = H0 - Beffective
```

一个透明的 bootstrap 形式是：

```text
C(L) = lambda_safe * L + kappa * max(0, L - Beffective)^2
```

其中 `L = max(0, H0 - H)`。死亡状态另加由“剩余可获得战斗价值上界”推导的
机会损失，使死亡路线不能靠死前多打伤害胜过存活路线。`lambda_safe` 和
`kappa` 不能由预算表武断推出；在正式校准前只能作为标记为 `prior` 的敏感性
参数。上述式子只是 `Phi(H0) - Phi(H)` 的一种可解释参数化，最终仍以第 5 节
定义的 `Phi` 接口为准。

每个 portfolio sample 必须包含：

```text
(act, tier, encounter, stage-matched deck, H0/maxHP,
 visible initial intent, future reserve context, semantic random keys)
```

牌组和 HP 必须与该战斗所处阶段匹配：Weak 使用 act 早段 snapshot，Boss 使用
进 Boss 前 snapshot，Normal/Elite 从该 act 内相应楼层分布取样。不能拿同一个
Act 1 starter deck 横跨十二格，也不能用最终牌组反向代表早期弱怪。

设抽样分布为 `q(z)`，目标部署分布为 `p(z)`：

- `q` 控制算力分配，可对 Boss、Elite、低 HP、低覆盖怪物和高方差样本过采样。
- `p` 控制最终价值，来自经审核的 A10 路线暴露/卡牌获得时点分布。
- 单样本使用 `p(z) / q(z)` 或先在格内估计再按目标权重聚合；报告必须给出
  effective sample size。
- 在 `p` 尚未由全量历史校准前，只能输出明确标为 research 的 balanced
  portfolio，不能把十二格等权平均伪装成运行时卡值。

十二格都要保留独立结果。稀有 Boss/Elite 可以在 `q` 中多抽，但不会自动改变
`p`；反过来，路线频率低也不能成为完全不抽某一格的理由。

## 5. 战斗 EV 标准

### 5.1 基础伤害单位

Phase 1 的进攻价值只认“实际减少敌人 HP 的数量”。定义一次玩家来源事件的
进攻奖励：

```text
enemyHpDamageValue = actualEnemyHpLostToPlayerSource
```

实际伤害必须经过游戏顺序：

1. 目标合法性和存活检查。
2. 攻击修正、Vulnerable、Strength 等。
3. 敌人格挡、无形或其他已支持减伤。
4. HP 下限和死亡处理。
5. 奖励等于本事件实际减少的敌人 HP。

因此：

- 敌人有 6 格挡，10 点攻击只减少 4 HP，则伤害价值为 `4`。
- 敌人只剩 4 HP，10 点攻击的伤害价值为 `4`，过量伤害 `6` 仅作为诊断。
- 已死亡敌人收到的后续 hit 价值为 `0`，并按真实规则取消或重定向。
- 敌人治疗必须抵消此前的净进度，不能通过“打伤 -> 治疗 -> 再打伤”刷价值。
- 攻击敌人格挡没有即时伤害价值；如果清除格挡能让后续攻击命中 HP，其价值
  会通过后续状态自然出现。

`enemyBlockBroken`、`attemptedDamage`、`overkillDamage` 可以保留在 ledger 中，
但不得直接加入总 EV。

### 5.2 玩家 HP 不是固定单价

定义 context `x` 下、拥有 `h` 点 HP 的后续续航价值为：

```text
Phi_x(h)
```

它表示从当前 context 继续游戏时，当前 HP 所能保留的未来战斗价值。边际 HP
价值为：

```text
lambda_hp(h, x) = Phi_x(h) - Phi_x(h - 1)
```

要求 `Phi_x(h)` 单调递增，并且低 HP 时的边际值更高。一次从 `h` 掉到
`h - d` 的 HP 成本为：

```text
hpLossCost(h, d, x) = Phi_x(h) - Phi_x(max(0, h - d))
```

这个定义有四个重要性质：

1. 高血量掉少量 HP 可以很便宜。
2. 接近危险线时，同样 1 HP 的价值会显著升高。
3. 一次掉 10 HP 与连续掉十次 1 HP，只要 context 和最终状态相同，HP 总成本
   相同，不会因攻击被拆成多段而凭空改变价值。
4. 自伤、敌人攻击、治疗、吸血、最大 HP 和格挡都能通过同一函数比较。

### 5.3 为什么平均掉血不能直接反推 HP 单价

平均掉血可以估计压力，但不能识别边际 HP 价值。两个未来场景都可能平均掉
10 HP：

- 场景 A 每次固定掉 10。
- 场景 B 一半不掉血，一半掉 20。

当玩家有 15 HP 时，两个场景的死亡风险完全不同。只使用均值会把致死尾部和
波动抹掉。此外，只使用 77 连胜样本会产生幸存者偏差：已经死亡的低 HP 路线
不会出现在胜利样本的后半段。

平均掉血适合用于估计 `Phi` 的 context 和安全血线，不适合直接得到
`1 HP = k value`。需要至少使用条件分布：

```text
R_x = 从当前 context 到下一可靠恢复点/检查点的未来 HP 损失分布
```

Phase 1 的可解释 bootstrap 形式建议为：

```text
Phi_x(h)
  = W_x * P(R_x < h)
  + lambda_carry_x * E[max(h - R_x, 0)]
```

含义：

- `W_x` 是活着到达下一检查点后保留下来的未来价值。
- `P(R_x < h)` 是以当前 HP 穿过这段风险的概率。
- `lambda_carry_x` 是把剩余 HP 带入下一段路线的价值。
- `E[max(h - R_x, 0)]` 区分“勉强活下来”和“带着健康血量活下来”。

`W_x` 应以 `1 damage = 1 value` 为尺度，优先用继续到下一检查点后可获得的
预期敌人 HP 伤害机会来锚定，而不是任意写一个死亡罚分。`Phi_x(0)` 必须低于
任何存活状态。若使用有限标量，还必须满足同一 context/horizon 内的死亡支配
条件：

```text
maxDamageRewardBeforeDeath + Phi_x(0)
  < minSurvivingTerminalValue
```

也就是说，不能通过在死亡前多打一些伤害，让死亡路线胜过任意存活路线。死亡
损失至少应覆盖 context 中剩余可获得的战斗价值以及 horizon 内可能取得的最大
额外伤害。实现时应根据状态上界生成该界限，而不是散落一个全局 magic number。

长期目标不是永远使用上述闭式近似，而是估计 Bellman continuation：

```text
Phi_x(h)
  = E[未来实际敌人 HP 伤害 + Phi_next(nextHp) | x, h, policy]
```

bootstrap 曲线拟合完成后，应投影到单调、低 HP 高边际的形状约束空间。原始
经验 CDF 可以保留为诊断，但不能让抽样噪声制造“更高 HP 的下一点反而更贵”
这类局部反转。

### 5.4 如何从统计数据校准 `Phi`

Phase 1 先使用粗 context，避免样本过度切分：

- act/floor band；
- 普通、精英、Boss；
- 距离下一个休息点、Boss 或 act 结束的房间数；
- 当前/max HP band；
- 牌组防御/抽牌摘要；
- 是否存在可靠治疗来源。

需要的观测数据包括：

- 每个战斗前后 HP、max HP、damage taken、healing；
- encounter、floor、路线和下一个恢复点；
- 胜利、死亡、放弃和版本；
- HEAL/SMITH 决策；
- HP 换奖励的事件选项；
- 当时牌组、遗物和治疗来源。

当前 history-analysis 已经能提取 `current_hp`、`max_hp`、`damage_taken`、
`hp_healed` 和营火选择，可以扩展为校准输入。但正式拟合必须包含失败样本；
77 连胜只适合作为成功策略剖面和先验检查。

建议的尺度锚点按优先级排列：

1. **HEAL 与 SMITH 的 revealed preference**：在玩家切换选择的 HP 区间，
   `Phi(h + heal) - Phi(h)` 应接近该次升级的模拟 dEV。
2. **HP 成本事件**：支付 `k` HP 获得可估值奖励时，奖励 dEV 给出局部 HP
   区间的尺度信息。
3. **全量胜负与掉血分布**：估计未来损失分布和死亡尾部，而不是只拟合均值。
4. **敏感性分析**：在数据不足时使用公开的 prior，并报告参数变化造成的 dEV
   排名和符号变化，不能把 prior 包装成统计结论。

第 4.3 节的十二个 `B(act, tier)` 作为形状约束进入校准：它规定典型起始 HP
下的曲线 knee，而不是新增十二个固定 HP 单价。拟合时应同时报告：

- `E[loss]`、`P(loss > B)`、死亡概率、P90 和 CVaR90；
- `lambda_safe`、knee 后边际斜率/曲率以及死亡机会损失；
- budget prior 对排名的敏感性；
- empirical 曲线是否支持原预算，还是提示需要重新评审预算。

不应为了让历史平均掉血“贴住预算”而反向调低惩罚。若一个基准牌组在某格
长期显著超过预算，首先应检查牌组阶段、起始 HP、怪物覆盖、策略质量和预算
本身，而不是让价值函数掩盖问题。

### 5.5 格挡、Weak、治疗和击杀如何统一

格挡不获得独立奖励。假设玩家当前 20 HP，敌人将造成 10 点伤害：

- 无格挡：结束于 10 HP。
- 3 格挡：结束于 13 HP。
- 10 格挡：结束于 20 HP。
- 12 格挡：仍结束于 20 HP，额外 2 格挡若回合末消失则价值为 0。

3 点格挡在该状态的实际价值为：

```text
Phi_x(13) - Phi_x(10)
```

同理：

- Weak 的价值等于它实际减少的玩家 HP 损失带来的 `Phi` 差。
- 击杀敌人使其后续意图不再发生，避免的 HP 损失自然进入 `Phi` 差。
- 治疗价值为 `Phi(min(maxHp, h + heal)) - Phi(h)`；overheal 为 0。
- 自伤使用完全相同的 HP 损失函数，不再乘固定 `1.5`。
- 下一回合格挡、保留格挡只有在真正跨回合存活并阻止伤害时才有价值。
- 敌人不攻击的回合，普通临时格挡为 0；不再由 floor 常数强行赋值。

### 5.6 总战斗回报和 dEV

对固定 horizon `H`，单条轨迹的回报定义为：

```text
G_H
  = actualEnemyHpDamage
  + Phi_x(playerHpAtEnd) - Phi_x(playerHpAtStart)
  + terminalContinuation
```

其中：

- `actualEnemyHpDamage` 是所有玩家来源实际减少的敌人 HP，敌人治疗需要反向
  冲销净进度。
- `Phi` 差是玩家 HP 的非线性续航变化。
- 玩家死亡、战斗仍未结束、特殊阶段和下一 context 的价值只通过
  `terminalContinuation` 表达，禁止再叠加另一套固定格挡/Weak 奖励。
- 战斗胜利不另加任意 victory bonus；它通过敌人实际 HP 归零、取消未来意图，
  以及带着当前 HP 进入下一 context 的 continuation 体现。
- horizon 结束但战斗仍在进行时，terminal evaluator 必须读取剩余敌人状态；
  不能把“尚未打完”当成胜利，也不能把剩余 HP 简单按固定负值重复扣除。
- Phase 1 不给“更快结束战斗”额外固定奖励。速度只有在减少未来敌人行动、
  降低 HP 损失或改善 horizon 终态时才有价值。

有限 horizon 有一个容易被忽略的守恒问题：如果在 horizon 末把“以后最终会
打掉的全部剩余敌人 HP”也按 1:1 加回，那么所有最终获胜路线的总敌人伤害几乎
固定，4/8/12 回合内谁先造成伤害会被完全抵消；反过来，若未完成战斗完全没有
tail，又会鼓励只格挡拖到 horizon 结束。Phase 1 因此采用以下 terminal 语义：

- horizon 内的实际敌人 HP 损失按 1:1 计入。
- horizon 后由一个固定、非 clairvoyant 的 reference policy 完成剩余战斗，
  只把其造成的后续玩家 HP 变化、死亡风险和下一 context 的 `Phi` 带回。
- reference tail 中未来必然要造成的敌人伤害不再次计入；否则会抵消 horizon
  内的进攻节奏。
- reference policy、最大 tail turns 和未完成 tail 的边界必须进入报告和缓存
  hash；它不允许调用旧启发式卡值或读取隐藏牌序。

这样 4/8/12 仍然分别衡量该窗口内的进攻贡献，同时未解决的敌人威胁会通过
未来 HP 风险惩罚拖延路线。对于手算 oracle，可以把战斗构造成 horizon 内必然
结束，或显式使用 `tail = 0` 的测试 evaluator，避免把 terminal 近似误差混入
transition/solver 正确性测试。

牌组价值为：

```text
J_H(D, x) = E[G_H | deck D, context distribution x, policy]
```

卡牌加入价值为：

```text
dEV_H(c | D, x) = J_H(D + c, x) - J_H(D, x)
```

令十二格索引为 `g = (act, tier)`，正式聚合为：

```text
dEV_H(c) = sum_g w_g * dEV_H(c | g)
```

`w_g` 是目标部署权重而不是抽样次数占比。格内若使用非目标分布采样，还要先
做 importance correction。报告必须同时保留每格的 `dEV`、HP 分量、实际伤害
分量和风险指标，以便发现“总体均值看似正常、某个 Boss 格灾难性为负”的牌。
Balanced 十二格均值只作覆盖诊断；运行时最终仍只安装一个经批准的 primary
deck-level dEV，不建立第二套卡值。

要求：

- baseline 与 candidate 使用同一 context、同一 monster sample 和语义随机流。
- 4、8、12 回合分别优化策略，不能从 12 回合策略读取前缀。
- 存储值使用固定 horizon 的 total dEV；报告可同时给 `dEV / H`。
- 不得除以“实际战斗持续回合数”，否则快速击杀会改变分母并引入额外偏差。
- source credit、overkill、unused block、turns-to-kill 只作解释，不替代 dEV。
- 非线性 `Phi` 已经在逐轨迹求值后再取期望，因此自然惩罚低 HP 和尾部风险；
  Phase 1 不再额外把 CVaR 罚项叠入目标，避免同一风险双重计价。CVaR 只报告。

## 6. 怪物意图模型

仓库当前已有 121 个怪物 profile、351 个 move，以及 85 个 encounter、143 张
精确伤害矩阵。这些数据足以启动 Phase 1，但不能假设所有 profile 都已完整。

### 6.1 新怪物状态

每个怪物至少需要：

```text
MonsterState
  stableId
  modelId
  currentHp / maxHp
  block
  alive
  buffs / debuffs
  moveStateId
  visibleIntent
  hiddenMoveStateOrBelief
```

玩家状态至少需要：

```text
PlayerCombatState
  currentHp / maxHp
  block
  buffs / debuffs
  energy / stars
  alive
```

### 6.2 Phase 1 支持顺序

1. A10 单体 attack 和 multi-hit attack。
2. 当前可见 intent 和确定性 follow-up。
3. enemy defend/block。
4. 已有解析支持的 Strength、Weak、Vulnerable、Frail 等数值状态。
5. 有明确概率的随机 move branch，作为 chance node。
6. 1-3 个怪物的目标选择、死亡移除和剩余怪物行动。

暂不支持的 move 必须被标为 `UnsupportedMonsterTransition`，整个 encounter 不得
进入权威训练集。禁止用平均伤害或均匀随机分支静默代替源码语义。

### 6.3 当前意图与未来意图

- 当前已显示 intent 是信息状态的一部分，Planner 可以使用。
- 已公开的 move history 和可推导 state 也是信息状态的一部分。
- 未来尚未决定的随机 branch 属于 belief/chance node。
- 世界采样器可以持有真实未来 branch，但决策器在揭示前不得读取。
- 怪物死亡后，其计划 intent、攻击和后续状态转换必须取消。

## 7. 信息状态 AND/OR DP

### 7.1 信息状态

建议 canonical key 至少包含：

```text
hand instance signatures
known ordered draw prefix
unknown draw multiset counts
discard / exhaust signatures
energy / stars
cards / attacks / skills played counters
last-card and sequence-trigger state
active powers and mutable card state
player HP / block / statuses
all monster HP / block / statuses / visible intents / move belief
turn phase and remaining horizon
```

实例 ID 只有在真正影响未来语义时才进入 key；纯身份差异应通过稳定签名合并。
影响未来的计数器不得像当前 structural loop hash 一样被省略。

### 7.2 决策节点和机会节点

```text
V_h(I)
  = max_a [ immediateReward(I, a)
            + sum_o P(o | I, a) * V_h(T(I, a, o)) ]
```

决策节点：

- 打出某个卡牌实例；
- 选择怪物目标；
- 选择 card-object；
- 在已经看见的 generated screen 中选择牌；
- end turn。

机会节点：

- 未知牌堆抽牌；
- 随机生成 screen；
- 变换/附魔结果；
- 怪物随机 move branch。

### 7.3 精确资格和 fallback

第一批 exact-eligible 状态：

- 无未支持 action；
- 无无限或未证明有界的资源循环；
- 生成池和机会分支在配置上有明确上限；
- 所有怪物 transition 已解析；
- canonical key 覆盖全部未来相关状态。

无随机揭示的封闭手牌可使用 bitmask/multiset DP。遇到抽牌时进入逐张无放回
chance node；只有证明一批抽牌可交换时才使用超几何聚合。大生成池留给后续
sparse sampling 或方案 2，不能为了完成 Phase 1 强行展开。

## 8. Phase 1 实装工作包

### P1.0 语义契约和 oracle fixtures

实装内容：

- 固化本文件的 damage、HP、block、death、intent 和 dEV 契约。
- 建立 5-8 张牌的小型穷举手牌 fixture。
- 建立隐藏牌序、抽牌、generated screen、计数牌和怪物意图 toy cases。
- 建立物理结算 fixture：敌人格挡、overkill、玩家超额格挡、多段攻击、击杀。
- 沿用 `data/manual-tags/simulation_decks/` 和
  `data/manual-tags/simulation_scenarios/`，给 scenario 增加显式 player/encounter
  初态；不为新求解器发明另一套一次性 fixture 目录。
- 新建经人工审核的 `data/manual-tags/combat_value_portfolios.json`，显式列出
  十二格、deck source、encounter selector、HP context、抽样配额和目标权重。
- portfolio validator 必须拒绝缺格、重复格、权重不归一、Boss 使用非 Boss
  encounter、Act 不匹配以及没有来源说明的 primary 权重。

完成条件：所有预期值可手算，测试不依赖旧启发式搜索结果。

### P1.1 新状态和纯 transition kernel

实装内容：

- 在 Modeling 项目下建立独立的 `CardValueOverlay.Modeling.Combat` namespace。
- 当前 root csproj 会通配编译 Modeling 源码；Phase 1 必须显式排除 Combat 离线
  loader/reporting，避免尚未验收的求解器和大 memo 意外进入游戏 DLL。
- 实现 WorldState、InformationState、Action、Outcome、RewardLedger。
- 使用 mutation journal、结构共享或 copy-on-write，禁止候选热路径完整复制四个
  牌堆和全部 Power。
- 把目标、generated choice 和 card-object 选择移出卡牌效果内部。
- 从现有 `CardBehaviorCatalog` 适配规则，不重新散布 `TypeName` 分支。

完成条件：纯 transition 不依赖 Godot/StS2 runtime assembly；相同输入和 outcome
得到完全确定的下一状态。

### P1.2 战斗物理结算

实装内容：

- 玩家/敌人 HP、block、alive、target。
- 单体、多段和 AoE 的实际伤害结算。
- 敌人格挡、玩家格挡、回合末清除和跨回合保留规则。
- death、overkill、healing、self HP loss。
- Weak、Vulnerable、Frail 在实际伤害/格挡流程中的动态作用。
- RewardLedger 的来源归因和净 HP 进度。

完成条件：物理 invariant 测试全部通过，不读取 `blockToDamage`。

### P1.3 怪物 profile 适配器

实装内容：

- 从 `monster_move_profiles.generated.json` 和手工 override 加载 A10 数值。
- 将初始 state、visible intent、follow-up 和随机 branch 编译为 transition。
- 从 encounter pattern 初始化 1-3 个怪物。
- 输出 supported/unsupported 覆盖报告和原因。
- 用现有 exact damage matrices 做回放验证。

完成条件：所有标记为 supported 的 encounter 在无玩家干预基线下逐回合复现已知
攻击序列；未知路径严格失败。

### P1.4 Information-state solver

实装内容：

- 实现 canonical state、memo、决策节点和机会节点。
- exact-eligible 手牌不使用当前强制零费牌/Power 顺序规则。
- 抽牌和未来怪物随机意图按期望求值。
- known top 与 unknown multiset 分离。
- 独立 horizon 和严格节点/状态统计。

完成条件：小手牌与穷举 oracle 的动作、值和终态完全一致；隐藏牌序置换不改变
揭示前动作。

### P1.5 HP continuation calibration

实装内容：

- 新建独立 HP continuation 配置，不复用 `blockToDamage`。
- 表示 `Phi_x(h)`、context、数据来源、生成时间和置信等级。
- 建议人工批准的 durable 输入为
  `data/manual-tags/hp_continuation_calibration.json`，每个 context 保存 max-HP
  band、单调曲线 knots、death value、method、source 和 confidence。
- 未批准的拟合曲线和敏感性结果写到
  `data/generated/hp_continuation/`，不得被 runtime 读取。
- 扩展 history-analysis 的风险分布和 HEAL/SMITH 校准报告。
- 数据不足时输出 prior 与敏感性范围，不输出伪精确单值。
- 十二个 HP context 保存 `acceptableLossBudget`，即第 4.3 节的
  `0/5/10, 8/10/15, 15/20/30, 30/40/40`，并保存 `futureReserveHp`、
  knee 生成方式、safe marginal 和 excess-loss curvature 的来源。
- `acceptableLossBudget` 只生成 `Phi` 的 knee；任何实现不得出现
  “未达到 budget 就奖励掉血”或“budget 内 HP cost = 0”的分支。
- generated review 输出留在 `data/generated`；稳定参考输入才进入
  `data/extracted` 或 `data/manual-tags`。

完成条件：曲线满足单调性、低 HP 高边际和死亡边界；每个参数有来源或明确
`prior` 标记。

### P1.6 Paired dEV estimator

实装内容：

- 对 `D` 和 `D + c` 使用相同 encounter/context sample。
- 先对十二格分别估计，再按独立于抽样配额的目标权重聚合。
- 每格的 encounter、stage-matched deck、起始 HP/最大 HP 和 visible intent
  都属于 paired context；candidate 只能改变牌组，不能改变这些外部条件。
- 使用语义随机流：shuffle、monster intent、generation 各自按事件身份索引。
- 4/8/12 回合独立规划和评估。
- 输出 total dEV、dEV/turn、paired CI、HP 分量、实际伤害分量、death probability、
  overkill、unused block、turns-to-kill。
- 输出每格 `E[loss]`、`P(loss > B)`、P90、CVaR90、death probability、
  target/proposal weight、importance ESS 和 supported encounter weight mass。
- 规划样本与最终 policy evaluation 样本分离，防止选择偏差。

完成条件：人工构造的零价值牌 dEV 为 0 且 CI 覆盖；已知正负 toy case 不发生
高置信符号翻转。

### P1.7 性能与迁移门槛

实装内容：

- 同时记录 wall/CPU、allocated bytes、canonical states、memo hit、chance nodes、
  state deltas 和 p50/p95/p99 turn latency。
- 建立 ordinary、draw、order、intent、defense、kill、multi-enemy 分层套件。
- 旧/新求解器只做 side-by-side 报告，不自动安装新卡值。
- 基准按十二格、机制类别和 horizon 分层；不得用只包含 easy Weak 的套件宣称
  整体性能达标。
- 建议新增独立 CLI 入口：
  `validate-combat-kernel`、`replay-monster-intents`、
  `estimate-hp-continuation`、`benchmark-information-state-solver` 和
  `estimate-combat-aware-deck-delta`。命令名在实现评审时最终确认，但各责任不得
  再塞回一个通用 command。

完成条件：达到第 9 节全部 go/no-go 门槛后，才允许讨论扩大机制覆盖或替换旧
训练路径。

## 9. 验收标准

### 9.1 战斗物理硬门槛

以下全部要求精确相等：

| Case | 预期 |
| --- | --- |
| 敌人 20 HP、6 block，受到 10 | block 归零、HP 16、伤害价值 4 |
| 敌人 4 HP、0 block，受到 10 | HP 0、伤害价值 4、overkill 6 |
| 已死亡敌人再受攻击 | 伤害价值 0，不再行动 |
| 玩家 20 HP、8 block，受到 5 | HP 20、剩余/丢弃 block 按规则、HP 成本 0 |
| 玩家 20 HP、3 block，受到 10 | HP 13，防御 dEV 等于 `Phi(13)-Phi(10)` |
| 玩家 20 HP、12 block，受到 10 | HP 20，额外 2 临时 block 价值 0 |
| 敌人治疗 5 后再次受伤 | 治疗反向冲销净进度，不能刷伤害价值 |
| 两条路线相同终态 | 除显式 timing/trigger 外，物理回报相同 |

### 9.2 HP 价值硬门槛

- 对所有 context，`Phi(h + 1) >= Phi(h)`。
- 对存活区间，HP 越低，边际 `lambda_hp` 不得更低。
- `Phi(0)` 必须使死亡轨迹在加入 horizon 内最大可能伤害后，仍劣于同 context
  下任何存活终态。
- overheal 和超过 max HP 的部分价值为 0。
- 同一 `Phi` 只能结算一次：事件 delta 与 terminal utility 不得双重计入。
- combat-aware 路径对 `blockToDamage`、固定 Weak 价值和固定 `selfHpLossPenalty`
  的读取次数为 0。
- 所有数据拟合必须报告样本包含哪些胜/负/放弃记录；仅胜利样本不能标为
  empirical HP value。

### 9.3 信息状态正确性硬门槛

- 两个状态仅未揭示牌序不同，揭示前首个动作和 action value 必须一致。
- 已知置顶牌不同可以改变动作，并必须有测试。
- 抽牌/生成 toy case 必须验证 `max E`，不能得到 `E max` 的 clairvoyant 值。
- 5-8 张 exact-eligible 手牌穷举 action regret 为 0。
- 所有影响未来的 Attack/Skill/总出牌计数进入 canonical key。
- 相同 seed/context 下重复执行逐状态、逐 outcome、逐 reward 完全一致。

### 9.4 怪物模型硬门槛

- Ascension 10 数值是唯一权威战斗基础；非 A10 只能作标注参考。
- supported profile 的初始 intent、follow-up、hit count 和 damage 精确匹配来源。
- 随机 branch 概率总和为 1，并保留来源；未知概率不得猜测。
- 怪物死亡后所有未来 intent 和行动取消。
- 多段攻击逐 hit 正确消费玩家 block。
- 每次构建输出覆盖报告；Phase 1 go/no-go 目标为覆盖现有 143 张 exact table
  中至少 80%，其余全部显式列为 unsupported，不得输出近似“权威值”。

### 9.5 十二格 portfolio 硬门槛

- Act 1/2/3 x Weak/Normal/Elite/Boss 十二格必须各出现且只能出现一次。
- 每格至少包含两个不同的 supported encounter；若该格权威数据确实不足，
  整个 primary aggregate 阻塞，而不是借用相邻格或把权重重分配给其他格。
- supported target-weight mass 整体至少 80%，每格至少 70%；其余权重和原因
  必须逐项报告。未达标时只能输出 conditional-on-supported research 结果。
- stage-matched deck、起始 HP、visible intent 和 future reserve 必须进入 sample
  identity 与缓存 hash。
- proposal allocation 与 target aggregation weight 分栏报告；改变过采样配额而
  保持样本充分时，最终估计不得系统性漂移。
- 每格报告 `E[loss]`、`P(loss > B)`、P90、CVaR90 和死亡概率。实际掉血不需要
  等于 budget，但不得省略 budget exceed 诊断。
- 十二格 balanced mean 只能标为诊断；primary dEV 的权重必须有明确 A10 数据
  来源或标为待审 prior。

### 9.6 dEV 与统计硬门槛

- `1 点实际敌人 HP 损失 = 1.000`，任何 act/layer 均相同。
- 4、8、12 回合单独运行与批量运行时结果逐样本相同。
- baseline/candidate 的共同卡实例在语义洗牌中保持相对顺序。
- monster intent 和 generation 使用各自独立的语义随机流。
- synthetic paired cases 的 95% CI 覆盖率通过预设统计测试。
- 对非接近零的 oracle dEV，平均相对误差目标不超过 2%，单 case 不超过 10%，
  不允许高置信符号翻转。
- 近零值使用绝对误差和 CI 判断，不使用不稳定的相对误差。

### 9.7 性能 go/no-go

以下是 Phase 1 立项目标，不是未经测量的性能承诺：

- exact-eligible 热路径不得调用完整 `SimulationState.Clone`。
- 与当前求解器相比，eligible suite 的 state-copy/allocated bytes 至少降低 10 倍。
- 在动作和值与 exhaustive oracle 一致的前提下，eligible suite 的 p95 wall time
  至少提升 3 倍。
- p99 和最大单回合延迟必须同时下降，不能只改善平均值。
- 1/2/4 worker 分别报告，不增加 candidate 内层并行。
- profiler wall time 不能与普通 benchmark 比较；profiler 只用于结构计数。
- 若性能目标未达成，先检查 canonical 等价率和 chance 分支规模，不通过降低
  正确性标准或恢复 hidden-order determinization 过关。

### 9.8 回归与发布门槛

- Modeling tests、Tools build、`validate-generated-data` 全部通过。
- 当前 clone ownership/missing-card 历史问题必须有确定性复现测试和至少 200-run
  post-fix 完整性验证。
- 新 solver 默认不覆盖 runtime JSON；先生成独立 review artifact。
- card_values 安装需要另一次明确批准和独立 release validation。

## 10. 评审时需要确认的设计决定

本计划给出以下推荐默认值，评审时可以逐项讨论：

1. **HP 曲线采用 run-continuation 价值，而不是固定 HP 单价。**
2. **Phase 1 不给速度/击杀额外常数奖励。**速度只通过少挨攻击和 horizon
   终态体现。
3. **敌人格挡吸收的伤害即时价值为 0。**清格挡的价值只来自后续可达状态。
4. **玩家死亡使用 `Phi(0)` 的未来机会损失表达。**标量应以剩余可获得敌人 HP
   伤害为尺度，不采用任意巨大常数。
5. **平均掉血只用于拟合风险分布。**HP 价格尺度优先由 HEAL/SMITH、HP 事件
   交易和全量胜负数据校准。
6. **最终卡牌值只保留 dEV。**HP、伤害、overkill、unused block 和 death risk
   是 dEV 的解释分量，不安装成第二套卡值。
7. **十二格掉血数值是 soft knee，不是掉血目标。**它们先作为 prior 进入
   `Phi`，后续可以由全量数据提出修订证据，但不能在求解时奖励“刚好掉到预算”。
8. **抽样与聚合分离。**推荐正式 primary 值使用 acquisition/route-conditioned
   A10 权重；十二格等权只用于诊断覆盖。
9. **风险不重复计价。**低 HP 高边际的 `Phi` 在逐轨迹上已经表达尾部风险，
   Phase 1 只报告 P90/CVaR，不另加一个可调 CVaR penalty。

## 11. 建议实施顺序

```text
P1.0 语义/oracle
  -> P1.1 状态与 transition kernel
  -> P1.2 战斗物理
  -> P1.3 怪物 intent adapter
  -> P1.4 information-state DP
  -> P1.5 HP continuation calibration
  -> P1.6 paired dEV
  -> P1.7 性能/覆盖/迁移评审
```

P1.1-P1.4 先用手工透明 `Phi` fixture 验证物理和规划正确性；P1.5 再替换为有
来源的 continuation curve。这样可以把“求解器是否正确”和“HP 标定是否准确”
分开验收，避免两类误差互相掩盖。

## 12. 现有输入与证据入口

- 当前求解器：`CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs`
- 当前搜索参数：`CardValueOverlay.Modeling/Simulation/DeckSimulationOptions.cs`
- 当前静态格挡/Weak 建模：
  `CardValueOverlay.Modeling/Simulation/SimulationCardLibraryBuilder.cs`
- 当前 4/8/12 训练入口：`CardValueOverlay.Tools/Program.TrainingValues.cs`
- 既有价值方法：`docs/modeling/card-value-methodology.md`
- 历史搜索优化结论：`.agents/docs/play-search-performance-optimization-20260715.md`
- 当前预算/分配验证：`.agents/docs/overlay-search-budget-validation-20260717.md`
- policy ranker 结果：`docs/modeling/search-policy-round1-results.md`
- state-value 结果：`docs/modeling/search-policy-value-network.md`
- 既有层级校准：`data/manual-tags/model_calibration.json`
- 怪物 move profiles：`data/extracted/monster_move_profiles.generated.json`
- encounter patterns：`data/extracted/encounter_patterns.generated.json`
- exact damage matrices：
  `data/generated/monster_encounter_damage_matrices.generated.json`
- history HP/掉血字段：`history-analysis/src/history_analysis/analysis.py`

当前数据规模的 sanity check：

- encounter pattern 中 Act 1 为 Weak/Normal/Elite/Boss = `8/22/6/6`，Act 2
  为 `4/10/3/3`，Act 3 为 `3/9/3/3`；十二格都有候选 encounter，但候选数
  不是最终路线权重。
- 当前 encounter-weighted pressure 只适合核对量级和发现异常；其 confidence
  与 warning 不足以反推出 HP 单价，也没有把 Normal 和 Elite 完整分开。
- 现有 `floor8/act2Start/preAct2Boss/final` deck snapshots 可用于工程 smoke，
  但主要来自胜利样本，不能单独承担正式 HP calibration 或路线权重估计。

当前检查的工作区没有 `history-dashen` 原始 `.run` 根目录，因此本文件没有伪造
任何“平均掉血”或 HP 曲线数值。P1.5 开始前必须先定位经过授权的全量历史数据，
确认其包含失败样本和版本信息，再生成校准报告。
