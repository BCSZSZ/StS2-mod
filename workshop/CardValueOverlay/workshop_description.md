# Card Value Overlay

This mod was inspired by Solisora's card-value framework, but the actual modeling, implementation, and numerical calculations are my own work based on my own understanding. If you think some values, calculations, or conclusions are wrong, that is due to the immaturity of my modeling and implementation. It does not imply that Solisora's method or value system is invalid. Feedback and suggestions are welcome in the comments or by contacting me.

The mod uses mathematical modeling and simulation to display card play value, deck-level value changes, and historical pick data directly in game. It currently focuses mainly on Regent and Colorless cards. Some complex effects, such as potion generation, random generation, card selection, transformation, and similar mechanics, may not yet be quantified reliably.

Requirements and usage:

- Requires BaseLib.
- Displays card values above cards.
- Real-time calc and dEV may briefly show `...` while the background simulation is running. `n/a` means the card or situation could not be evaluated reliably.
- Real-time simulation parameters can be adjusted in the BaseLib mod configuration.

This is a client-side display mod. It does not alter multiplayer game state, and other players do not need to install it.

Display guide:

short / mid / long: 4 / 8 / 14-turn horizons

est: Static card play value, calculated from the built-in static value model, card data, and calibration parameters.
calc: Card play value in the context of your current deck, calculated in real time by the simulator. It uses the same scale as est.
dEV: The estimated deck-level value change after adding this card. In deck view or upgrade preview, the baseline instead compares "having this card" against "removing this one copy."
after: Estimated total deck value after the displayed comparison.

calc and dEV are local and somewhat short-sighted: they evaluate your current deck and the selected short/mid/long horizon, not the whole future run. A card with very high dEV at the start of a run may still be a poor choice for a later deck.

deck: Based on publicly available run data from 956 Regent A10 solo standard wins on v0.107.0/v0.107.1. It is the percentage of winning final decks containing this card, ignoring upgrade form.
p+0 / p+1: From the same data source, the probability that this card was picked when offered as an unupgraded/upgraded card reward. Enchantments are not split out in this historical pick statistic.
b+0 / b+1: The probability that this card was bought when offered unupgraded/upgraded in a merchant shop. Enchantments are not split out in this historical buy statistic.
copy: Among final decks that already contain this card, the average number of copies. +0 and +1 copies are combined. Copy color thresholds exclude starter cards and use only cards that appeared in at least 30 final decks, to reduce distortion from extreme copy-generation runs.

p and b use separate offer denominators and separate Q25/Q75 color thresholds.

For est, calc, and dEV, green/red simply means positive/negative. For deck, pick, and buy, green/red are based on Q25/Q75 empirical percentiles after filtering unsuitable data, such as off-character cards that appeared only once or a few times. Green means the top 25%, and red means the bottom 25%. Copy also uses filtered data: a green copy value means the card is in the top 25% among cards that appeared in enough final decks, suggesting that it was historically more worth taking additional copies.

For Ancient options, pick means the historical probability that the option was chosen when offered.

Future plans:

- Support relics in value calculations.
- Add value calculations for other characters.
- Further improve simulator logic.

Simulator details:

The simulator's detailed calculation logic will be documented separately. Feel free to inspect the source code and data-processing scripts as well.

---

# Card Value Overlay

本 Mod 的思路受到 Solisora 的价值化卡牌体系启发，但具体建模、实现和数值计算都是我基于自己的理解完成的。若你认为其中的数值、计算过程或结论有误，问题应归因于我的建模和实现能力不足，并不代表 Solisora 的方法或价值体系不成立。欢迎在评论区或通过其他方式向我提出建议。

本 Mod 会通过数学建模和模拟计算，将卡牌打出价值、当前卡组价值变化，以及历史选择数据展示到游戏内卡牌上。目前主要支持储君与无色卡；部分复杂效果，例如炼药、随机生成、选择、变形等，仍可能无法可靠量化。

前置与使用：

- 前置需求：BaseLib。
- 在卡牌上方显示价值数据。
- calc 和 dEV 需要后台实时模拟，刚出现时可能短暂显示 `...`；`n/a` 表示当前卡牌或场景暂时无法可靠计算。
- 可在 BaseLib 的 Mod 配置中调整实时模拟参数。

本 Mod 仅在本地显示信息，不会修改多人游戏状态，联机队友不需要安装。

显示内容：

short / mid / long：4 / 8 / 14 回合视角

est：静态卡牌打出价值。基于当前内置的静态价值模型、卡牌数据和校准参数计算。
calc：当前卡组语境下的卡牌打出价值，由模拟器实时计算。它和 est 使用相同量纲。
dEV：当前卡组在加入这张牌后产生的整体价值变化。若是在卡组查看或升级预览界面，则基准会变为“拥有这张牌”相对于“移除这一张牌”的变化。
after：当前显示比较下的卡组总价值估计。

需要注意的是，calc 和 dEV 都是局部且相对短视的指标：它们只评估当前卡组和所选 short / mid / long 视角，而不是整局游戏的长期发展。因此，出门时 dEV 很高的一张卡，未必适合中后期卡组。

deck：根据公开可得的 run 数据，从 956 场 v0.107.0/v0.107.1、A10、单人、标准模式、储君胜利局中统计。表示该卡出现在最终通关卡组中的比例，不区分升级形态。
p+0 / p+1：同一数据源下，该卡以未升级/升级形态出现在卡牌奖励中时被选择的概率。这个历史 pick 统计不按附魔拆分。
b+0 / b+1：该卡以未升级/升级形态出现在商店中时，被购买的概率。这个历史 buy 统计不按附魔拆分。
copy：在最终卡组已经包含该卡的情况下，平均有几张该卡；+0 和 +1 合并计算。copy 的颜色分位会排除初始牌，并只使用至少出现在 30 个最终卡组中的卡，以减少极端复制局对阈值的影响。

p 与 b 使用各自独立的展示分母和 Q25/Q75 颜色阈值。

est、calc 和 dEV 的绿色/红色只是简单表示正数/负数。deck、pick 和 buy 的绿色/红色，则是在过滤掉不适合参与分位计算的数据后，例如只出现过一次或几次的其他职业卡，按经验 Q25/Q75 分位区分：绿色表示前 25%，红色表示后 25%。copy 也使用过滤后的数据：绿色表示在足够多最终卡组中出现过的卡里，copy 位于前 25%，也就是历史上更值得多抓的卡。

先古选项上显示的 pick 表示该选项出现时被选择的历史概率。

未来计划：

- 数值计算支持遗物。
- 追加其他职业的数值计算。
- 进一步优化模拟器逻辑。

模拟器详细逻辑：

模拟器的详细计算逻辑会另行说明。源码和数据处理脚本也欢迎自行查看。
