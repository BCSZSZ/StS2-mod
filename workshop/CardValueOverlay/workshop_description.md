# Card Value Overlay

Card Value Overlay simulates how one card changes the expected value of the current deck. It currently focuses mainly on Regent and Colorless cards.

v0.2.0 highlights:

- Faster, smoother real-time dEV simulation on complex decks through bounded background work and a lower-allocation simulation path.
- Smarter selective search reduces clearly inferior branches while preserving full search for resource, draw, generation, Power, and card-manipulation effects.
- Card rewards, shops, and Ancient options now show global and personal local-history pick rates side by side. Ancient rates include picks/offers plus picked win rate with wins/picks, and each character uses a separate sample cohort.
- A compact upgrade comparison keeps the center dEV panel clear of both cards.

Requirements and usage:

- Requires BaseLib.
- Displays one real-time `dEV` table above cards.
- Real-time simulation parameters can be adjusted in the BaseLib mod configuration.
- This is a client-side display mod. It does not alter multiplayer game state, and other players do not need to install it.

Display guide:

- `short / mid / long`: 4 / 8 / 12-turn horizons.
- `add dEV`: expected deck value after adding the offered card minus the current deck value.
- `owned dEV`: current deck value minus the value after removing this one copy.
- `upgrade dEV`: expected deck value after replacing the unupgraded copy with the upgraded copy, minus the current deck value.
- `mean`: average paired per-run difference.
- `95% CI`: paired Student-t confidence interval around that mean.
- `global / local`: global Spire Codex statistics and personal statistics calculated once at startup. Card and shop statistics use matching A10-win history; Ancient statistics use matching solo A10 standard history with all outcomes.
- Ancient statistics use only the current character's data. The first line is `rate (picks/offers)`; the second is `picked win rate (wins/picks)`, so the second denominator is exactly the first numerator.

The simulator samples in checkpoints of 15, 30, 45, and 60 runs. Simple cards may stop once all three horizons have a stable sign; complex cards use at least 30 runs. The displayed 95% interval is for interpretation, while early stopping uses a stricter four-look Bonferroni interval. Green or red appears only when that stricter interval is wholly positive or negative.

Sample suffixes:

- `~`: preview or still refining.
- `ok`: stopped with a stable sign at all three horizons.
- `?`: reached the configured maximum without a stable sign at all three horizons.

The result is local and horizon-limited. It evaluates the current deck, simulator policy, and modeled effects; it does not predict the whole future run. Complex effects such as potion generation, random generation, selection, transformation, and unsupported card rules may still be quantified imperfectly.

Future plans:

- Support relics in value calculations.
- Add value calculations for other characters.
- Continue improving simulator policy and card-effect coverage.

This mod was inspired by Solisora's card-value framework, but the modeling, implementation, and numerical calculations are my own. Errors reflect limitations in my work, not a failure of Solisora's method or value system. Feedback is welcome.

---

# Card Value Overlay

Card Value Overlay 会实时模拟“某一张牌使当前卡组的期望价值发生多少变化”。目前主要支持储君与无色卡。

v0.2.0 重点更新：

- 通过受控的后台计算与更低分配的模拟路径，显著改善复杂牌组实时 dEV 的流畅度。
- 更智能的选择性搜索会削减明显落后的分支，同时保留资源、抽牌、生成、能力与牌对象操作的完整搜索。
- 卡牌奖励、商店和先古选项会并列显示网络全局与本机个人历史选取率；先古选项还会显示“选择后胜率（获胜选择次数/选择次数）”，两项都按当前角色分别统计。
- 升级对比采用更紧凑的布局，中间 dEV 面板不再与两侧卡牌重叠。

前置与使用：

- 前置需求：BaseLib。
- 在卡牌上方只显示一张实时 `dEV` 表。
- 可在 BaseLib 的 Mod 配置中调整实时模拟参数。
- 本 Mod 仅在本地显示信息，不会修改多人游戏状态，联机队友不需要安装。

显示内容：

- `short / mid / long`：4 / 8 / 12 回合视角。
- `add dEV`：加入候选牌后的卡组期望价值，减去当前卡组价值。
- `owned dEV`：当前卡组价值，减去移除这一张牌后的卡组价值。
- `upgrade dEV`：用升级形态替换当前未升级牌后的卡组价值，减去当前卡组价值。
- `mean`：逐轮配对差值的平均数。
- `95% CI`：围绕平均数计算的配对 Student-t 置信区间。
- `global / local`：网络全局统计，以及启动时从本机 run 历史计算的个人统计。卡牌和商店使用相同的 A10 胜局口径；先古使用相同的单人 A10 standard 全部胜负口径。
- 先古统计只使用当前角色的数据：第一行为“选取率（选择次数/出现次数）”，第二行为“选择后胜率（获胜选择次数/选择次数）”，所以下一行的分母必定等于上一行的分子。

模拟按 15、30、45、60 轮逐批执行。简单牌可在三个时间线的符号都稳定后提前停止；复杂牌至少计算 30 轮。界面显示的 95% 区间用于理解结果，提前停止则使用更严格的四次查看 Bonferroni 区间。只有该严格区间完全为正或完全为负时，数值才会显示为绿色或红色。

样本状态后缀：

- `~`：预览值，或仍在继续加算。
- `ok`：三个时间线的符号都已稳定，提前结束。
- `?`：达到所设最大轮数后，三个时间线仍未全部稳定。

dEV 是局部且受时间线限制的指标。它评估的是当前卡组、模拟器策略和已建模效果，并不预测整局游戏的未来。炼药、随机生成、选择、变形以及尚未支持的卡牌规则等复杂效果，仍可能无法被完全准确地量化。

未来计划：

- 数值计算支持遗物。
- 追加其他职业的数值计算。
- 持续改善模拟器策略与卡牌效果覆盖。

本 Mod 的思路受到 Solisora 价值化卡牌体系启发，具体建模、实现和数值计算由我独立完成。若数值或结论有误，应归因于我的建模和实现局限，并不代表 Solisora 的方法或价值体系不成立。欢迎提出建议。
