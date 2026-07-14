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
- card-object decision horizon and target-branch width;
- generated-card behavior;
- generated-choice continuation pools and resource requirements;
- one-shot search-admission policy for opaque generated-card effects;
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

`CardObjectDecisionProfile` is the shared declaration for stateful object
choices. It selects current-turn or through-next-turn continuation, target
branch width, and whether action-aware evaluation replaces the source card's
static play-setup prior. `GeneratedChoiceContinuationBehavior` supplies key-card
keep value through an actual generated pool. Neither facet contains a callback
or private simulation state.

`CardTransformBehavior.TargetConstraints` is owned by the transform source
card. Each source can therefore declare a different permanent-protection list,
resource kind and reserve, reusable-effect coverage requirement, or card-type
timing window. Constraints are strongly typed and evaluated by shared simulator
hooks; they are not global target tags. Resource and effect-coverage constraints
are checked against the complete selected target plan, so two individually safe
targets cannot jointly break the declared balance. A source with no constraints
keeps its previous behavior.

`SearchAdmissionPolicy.OncePerHandAvailability` handles cards whose payoff is
opaque until their generated choice or card-object mutation is resolved. When
such a card is first legal after entering the hand (or at a new turn while
retained), the search candidate set is `Top-B` plus at most one
continuation-ranked missing flagged card. The normal Top-B entries are never
displaced. Other missing flagged cards remain armed for descendant nodes, while
an unplayable card remains armed until resources or a suitable target make it
legal. All cards with supported `GeneratedCardBehavior`, including `Splash`,
and `Charge` currently use this policy.
