# Card Value Overlay v0.2.0

This update focuses on making real-time deck-value comparisons faster, more
stable, and easier to trust during play.

## Highlights

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
