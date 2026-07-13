# Card-Specific Behavior Framework

Use `CardBehaviorCatalog` for every simulator rule whose applicability depends
on a specific card identity. Do not add isolated card-name branches to the
library builder or Monte Carlo simulator.

## Boundary

Generic mechanics belong on parsed `SimulationCard` facts: damage, block,
costs, keywords, card actions, and enchantments. Card-specific interpretation
belongs in a `CardBehaviorDefinition`, including:

- lifecycle behavior keys;
- transform target, count, and selection policy;
- generated-card behavior;
- dynamic setup descriptors;
- scaling-damage metadata;
- source-less pile-move support and beam-floor overrides.

The catalog strips upgrade suffixes, so one definition applies to both base and
upgraded forms. Scenario-local cards also receive registered behavior when they
reuse the same type name.

## Extension Workflow

1. Add or extend the card's `CardBehaviorDefinition`.
2. Add a strongly typed behavior key or parameter type when the existing
   facets cannot express the rule.
3. Implement the behavior in the matching simulator lifecycle stage. The hook
   checks the registered behavior, never the card name.
4. Add a catalog test and a behavioral simulation test.
5. Search the modeling simulation code for the card name; it should appear only
   in the catalog, tests, comments, generic power keys, or generated-card object
   construction.

The catalog is intentionally declarative. It identifies which behavior applies
and stores stable parameters, while combat state stays inside the simulator.
This avoids a universal callback interface coupled to private simulation state
and keeps each lifecycle stage readable.
