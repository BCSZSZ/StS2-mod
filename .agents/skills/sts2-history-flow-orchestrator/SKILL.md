---
name: sts2-history-flow-orchestrator
description: "Coordinate the full StS2 history-analysis workflow from V1/V2 regeneration through evidence chaining, conclusion review, and final strategy report. Use when planning or executing the whole pipeline."
---

# StS2 History Flow Orchestrator

Use this skill to coordinate the full analysis pipeline. The key distinction is: Python generates facts, candidates, validations, and report scaffolds; conclusion review supplies explicit reasoning.

## Atomic Task Flow

| Step | Skill | Main output | Automation level |
|---|---|---|---|
| 1 | `$sts2-history-v1-refresh` | Correct V1 fact tables and V1 summary | Code-generated |
| 2 | `$sts2-history-v2-refresh` | V2 strategy tables and V2 summary | Code-generated |
| 3 | `$sts2-history-credibility-guardrails` | Strength guardrails for downstream claims | Mostly code |
| 4 | `$sts2-history-macro-profile` | Macro playstyle candidates | Mixed |
| 5 | `$sts2-history-opening-profile` | Opening pick priority candidates | Mixed |
| 6 | `$sts2-history-card-adoption` | Single-card adoption candidates | Mixed |
| 7 | `$sts2-history-pairwise-profile` | Same-screen matchup candidates | Mixed |
| 8 | `$sts2-history-rhythm-profile` | Upgrade/delete/skip/special-event candidates | Mixed |
| 9 | `$sts2-history-stratification` | Stable/fragile/reversed rule checks | Mostly code |
| 10 | `$sts2-history-evidence-chain` | Evidence-linked conclusion candidates | Code-generated scaffold |
| 11 | `$sts2-history-conclusion-review` | Accepted/rejected/downgraded conclusions | Reasoning required |
| 12 | `$sts2-history-final-report` | Final strategy report | Reasoning plus generated assembly |

## Orchestration Rules

1. If a source metric or table is wrong, return to V1 or V2 code. Do not patch generated CSVs.
2. If a candidate lacks evidence references, run the evidence-chain task before review.
3. If a conclusion requires causality, context, or strategy wording, route it through conclusion review.
4. If V1 and V2 disagree, trust V1 only after parser/table tests pass, then regenerate V2.
5. Keep summaries separated: V1 `reports/summary.md`, V2 `reports/strategy/summary.md`, final report `reports/strategy/final_strategy_report.md`.
6. Keep special quest cards out of active deletion strategy. 藏宝图 and 灯火钥匙 belong in special-event analysis.

## Definition Of Done

- `uv run pytest` passes from `history-analysis`.
- V1 and V2 reports are regenerated after code changes.
- New metrics are implemented as reusable Python code in `analysis.py` or `strategy.py`.
- Generated candidates identify source tables and limitations.
- Reviewed conclusions explicitly say why they were accepted, downgraded, rejected, or left as sample hints.
- Final report uses accepted conclusions rather than raw candidate lists.
