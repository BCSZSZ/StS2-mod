# Card Value Overlay v0.2.0

This update focuses on making real-time deck-value comparisons faster, more
stable, and easier to trust during play.

## Highlights

- Added side-by-side global and local pick statistics for card rewards, shops,
  and Ancient options. Card and shop rates retain the A10-win scope. Ancient
  rates use one all-outcome solo A10 standard cohort for both displayed lines:
  picks/offers, such as `15.2% (93/612)`, then wins/picks after taking the
  option. The second denominator is therefore always the first numerator.
  Global and local statistics use only the current character's runs.
- Made upgrade comparisons more compact so the center dEV panel no longer
  overlaps the two cards.
- Refined the 4 / 8 / 12-turn dEV model, confidence reporting, and play-search
  decisions, including conservative selective branching for expensive decks.
- Reworked the simulation hot path to sharply reduce allocation and garbage
  collection on complex decks.
- Added cooperative, horizon-aware background scheduling: long simulations now
  advance in small deterministic slices and run serially during combat.
- Added horizon-aware search budgets that bound pathological 8 / 12-turn
  simulation tails while preserving full shortline search.

The overlay remains client-side only and continues to focus on Regent and
Colorless cards. Complex or unsupported effects may still be estimated
imperfectly.
