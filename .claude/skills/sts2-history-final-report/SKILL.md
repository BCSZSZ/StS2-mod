---
name: sts2-history-final-report
description: "Synthesize accepted StS2 history-analysis conclusions into the final Regent A10 strategy report. Use after V1/V2 refresh, evidence chaining, and conclusion review are complete."
---

# StS2 Final Report

Use this skill for stage 9: the final strategy report. This report should be built from reviewed conclusions, not directly from raw candidate tables.

## Inputs

- Reviewed conclusions or accepted rows from `30_rule_validation.csv` until a dedicated reviewed table exists
- V1 `reports/summary.md`
- V2 `reports/strategy/summary.md`
- `31_case_library.csv` and source rows for selected replay cases

## Target Sections

1. 连胜打法总论
2. 储君 A10 选牌优先级
3. 开局抓牌原则
4. 中后期跳过原则
5. 升级删除原则
6. 特殊事件原则
7. 可复盘案例库

## Task Flow

1. Include only reviewed/accepted conclusions as final claims.
2. Keep sample hints in a clearly labeled appendix or omit them from main rules.
3. Cite source evidence for each major principle.
4. Use case-library entries as replay anchors, then inspect source rows before writing narrative examples.
5. Generate or assemble the report through reusable Python when possible, so repeated refreshes do not rely on hand-edited markdown.

## Code Boundary

If the final report is a recurring artifact, implement a generator in `src/history_analysis/strategy.py` or a dedicated module and test that the report path exists. Manual prose may be used for reviewed reasoning, but raw numbers and accepted-claim selection should stay structured.

## Output Rule

Do not call V2 `summary.md` the final report. It is a strategy summary. The final report should be a separate artifact, such as `reports/strategy/final_strategy_report.md`.
