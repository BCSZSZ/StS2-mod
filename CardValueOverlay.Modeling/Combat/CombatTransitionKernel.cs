using CardValueOverlay.Modeling.Simulation;

namespace CardValueOverlay.Modeling.Combat;

public sealed record CombatPreparedTransition(
    double ImmediateReward,
    int DrawCount,
    IReadOnlyList<IReadOnlyList<MonsterIntentTransition>> MonsterTransitions);

public sealed class CombatTransitionKernel
{
    private readonly CombatCardCatalog _cards;
    private readonly IReadOnlyDictionary<string, CombatMonsterDefinition> _monsters;
    private readonly CombatSimulationOptions _options;
    private readonly CombatDamageResolver _damage = new();

    public CombatTransitionKernel(
        CombatCardCatalog cards,
        IReadOnlyDictionary<string, CombatMonsterDefinition> monsters,
        CombatSimulationOptions options)
    {
        _cards = cards;
        _monsters = monsters;
        _options = options;
        options.Validate();
    }

    public IReadOnlyList<CombatAction> GetLegalActions(CombatInformationState state)
    {
        List<CombatAction> actions = [];
        FillLegalActions(state, actions);
        return actions;
    }

    public void FillLegalActions(CombatInformationState state, List<CombatAction> actions)
    {
        actions.Clear();
        if (state.PendingCardSelection is not null)
        {
            FillPendingCardSelectionActions(state, actions);
            actions.Sort(CombatAction.CompareStable);
            return;
        }

        for (int handIndex = 0; handIndex < state.Hand.Count; handIndex++)
        {
            int instanceId = state.Hand[handIndex];
            CombatCardInstanceState cardInstance = state.GetCardInstance(instanceId);
            CombatCardInstanceKey physicalKey = cardInstance.PhysicalKey;
            bool alreadySeen = false;
            for (int earlier = 0; earlier < handIndex; earlier++)
            {
                if (state.GetPhysicalKey(state.Hand[earlier]) == physicalKey)
                {
                    alreadySeen = true;
                    break;
                }
            }
            if (alreadySeen) continue;

            CombatCardDefinition card = _cards.Get(cardInstance.DefinitionId);
            if (!card.IsSupported || !card.IsPlayable ||
                card.EnergyCost > state.Energy || card.StarCost > state.Stars)
            {
                continue;
            }

            bool needsEnemyTarget = card.Target == CombatCardTarget.Enemy ||
                card.Effects.Any(effect => effect.Target == CombatCardTarget.Enemy);
            if (needsEnemyTarget)
            {
                for (int target = 0; target < state.Monsters.Count; target++)
                {
                    if (state.Monsters[target].IsAlive)
                    {
                        actions.Add(new CombatAction(
                            CombatActionKind.PlayCard,
                            card.DefinitionId,
                            target,
                            cardInstance.ForgeDamageBonus));
                    }
                }
            }
            else
            {
                actions.Add(new CombatAction(
                    CombatActionKind.PlayCard,
                    card.DefinitionId,
                    CardForgeDamageBonus: cardInstance.ForgeDamageBonus));
            }
        }

        actions.Add(CombatAction.EndTurn);
        actions.Sort(CombatAction.CompareStable);
    }

    private static void FillPendingCardSelectionActions(
        CombatInformationState state,
        List<CombatAction> actions)
    {
        CombatPendingCardSelectionState pending = state.PendingCardSelection
            ?? throw new InvalidOperationException("No card selection is pending.");
        IReadOnlyList<int> source = state.GetPile(pending.Spec.SourcePile);
        if (source.Count == 0)
        {
            actions.Add(new CombatAction(CombatActionKind.ResolveEmptyCardChoice));
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            CombatCardInstanceState candidate = state.GetCardInstance(source[index]);
            CombatCardInstanceKey physicalKey = candidate.PhysicalKey;
            bool alreadySeen = false;
            for (int earlier = 0; earlier < index; earlier++)
            {
                if (state.GetPhysicalKey(source[earlier]) == physicalKey)
                {
                    alreadySeen = true;
                    break;
                }
            }
            if (!alreadySeen)
            {
                actions.Add(new CombatAction(
                    CombatActionKind.ChooseCard,
                    candidate.DefinitionId,
                    CardForgeDamageBonus: candidate.ForgeDamageBonus));
            }
        }
    }

    public CombatPreparedTransition Prepare(
        CombatInformationState state,
        CombatAction action,
        CombatMutationJournal journal)
    {
        if (state.PendingCardSelection is not null &&
            action.Kind is not CombatActionKind.ChooseCard and not CombatActionKind.ResolveEmptyCardChoice)
        {
            throw new InvalidOperationException("The pending card selection must resolve before any other combat action.");
        }
        return action.Kind switch
        {
            CombatActionKind.PlayCard => PrepareCard(state, action, journal),
            CombatActionKind.ChooseCard => PrepareCardSelection(state, action, journal),
            CombatActionKind.ResolveEmptyCardChoice => PrepareEmptyCardSelection(state, journal),
            CombatActionKind.EndTurn => PrepareEndTurn(state, journal),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private CombatPreparedTransition PrepareCard(
        CombatInformationState state,
        CombatAction action,
        CombatMutationJournal journal)
    {
        CombatCardDefinition card = _cards.Get(action.CardDefinitionId);
        if (!card.IsSupported || !card.IsPlayable ||
            card.EnergyCost > state.Energy || card.StarCost > state.Stars)
        {
            throw new InvalidOperationException($"Card '{card.StableKey}' is not legally playable.");
        }

        int handIndex = state.MutableHand.FindIndex(
            instanceId => state.GetPhysicalKey(instanceId) == action.CardPhysicalKey);
        if (handIndex < 0)
        {
            throw new InvalidOperationException($"Card instance {action.CardPhysicalKey} was not found in the visible hand.");
        }
        int playedInstanceId = journal.RemovePileAt(state, CombatPile.Hand, handIndex);
        journal.AddPile(state, CombatPile.Play, playedInstanceId);
        CombatCardInstanceState playedInstance = state.GetCardInstance(playedInstanceId);

        journal.AddScalar(state, CombatScalarField.Energy, -card.EnergyCost);
        TriggerOrbitAfterEnergySpent(state, card.EnergyCost, journal);
        journal.AddScalar(state, CombatScalarField.Stars, -card.StarCost);
        TriggerChildOfTheStarsAfterStarsSpent(state, card.StarCost, journal);
        journal.AddScalar(state, CombatScalarField.CardsPlayed, 1);
        if (string.Equals(card.CardType, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            journal.AddScalar(state, CombatScalarField.AttacksPlayed, 1);
        }
        else if (string.Equals(card.CardType, "Skill", StringComparison.OrdinalIgnoreCase))
        {
            journal.AddScalar(state, CombatScalarField.SkillsPlayed, 1);
        }

        int drawCount = 0;
        double immediateReward = 0d;
        foreach (CombatCardEffect effect in card.Effects)
        {
            immediateReward += ApplyCardEffect(
                state,
                card,
                playedInstance,
                effect,
                action.TargetMonsterIndex,
                journal,
                ref drawCount);
            if (state.IsTerminal || state.PendingCardSelection is not null)
            {
                break;
            }
        }

        if (state.PendingCardSelection is null)
        {
            FinalizePlayedCard(
                state,
                playedInstanceId,
                card.Exhausts ? CombatPile.Exhaust : CombatPile.Discard,
                journal);
        }
        return new CombatPreparedTransition(immediateReward, drawCount, EmptyMonsterTransitions(state.Monsters.Count));
    }

    private CombatPreparedTransition PrepareCardSelection(
        CombatInformationState state,
        CombatAction action,
        CombatMutationJournal journal)
    {
        CombatPendingCardSelectionState pending = state.PendingCardSelection
            ?? throw new InvalidOperationException("No card selection is pending.");
        List<int> source = state.GetPile(pending.Spec.SourcePile);
        int sourceIndex = source.FindIndex(
            instanceId => state.GetPhysicalKey(instanceId) == action.CardPhysicalKey);
        if (sourceIndex < 0)
        {
            throw new InvalidOperationException(
                $"Selected card {action.CardPhysicalKey} was not found in {pending.Spec.SourcePile}.");
        }

        int selectedInstanceId = journal.RemovePileAt(state, pending.Spec.SourcePile, sourceIndex);
        InsertSelectedCard(state, pending.Spec, selectedInstanceId, journal);
        CompleteOrContinueCardSelection(state, pending, journal);
        return new CombatPreparedTransition(0d, 0, EmptyMonsterTransitions(state.Monsters.Count));
    }

    private CombatPreparedTransition PrepareEmptyCardSelection(
        CombatInformationState state,
        CombatMutationJournal journal)
    {
        CombatPendingCardSelectionState pending = state.PendingCardSelection
            ?? throw new InvalidOperationException("No card selection is pending.");
        if (state.GetPile(pending.Spec.SourcePile).Count != 0)
        {
            throw new InvalidOperationException("An empty card choice is legal only when the source pile is empty.");
        }

        journal.SetPendingCardSelection(state, null);
        FinalizePlayedCard(state, pending.PlayedInstanceId, pending.PlayedCardFinalPile, journal);
        return new CombatPreparedTransition(0d, 0, EmptyMonsterTransitions(state.Monsters.Count));
    }

    private static void InsertSelectedCard(
        CombatInformationState state,
        CombatCardSelectionSpec selection,
        int instanceId,
        CombatMutationJournal journal)
    {
        int position = selection.DestinationPosition switch
        {
            CombatCardInsertionPosition.Top => 0,
            CombatCardInsertionPosition.Bottom => state.GetPile(selection.DestinationPile).Count,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        journal.InsertPile(state, selection.DestinationPile, position, instanceId);
    }

    private static void CompleteOrContinueCardSelection(
        CombatInformationState state,
        CombatPendingCardSelectionState pending,
        CombatMutationJournal journal)
    {
        int remaining = pending.RemainingCount - 1;
        if (remaining > 0 && state.GetPile(pending.Spec.SourcePile).Count > 0)
        {
            journal.SetPendingCardSelection(state, pending with { RemainingCount = remaining });
            return;
        }

        journal.SetPendingCardSelection(state, null);
        FinalizePlayedCard(state, pending.PlayedInstanceId, pending.PlayedCardFinalPile, journal);
    }

    private static void FinalizePlayedCard(
        CombatInformationState state,
        int playedInstanceId,
        CombatPile destination,
        CombatMutationJournal journal)
    {
        int playIndex = state.MutablePlay.IndexOf(playedInstanceId);
        if (playIndex < 0)
        {
            throw new InvalidOperationException($"Played card instance {playedInstanceId} left the play zone unexpectedly.");
        }
        journal.RemovePileAt(state, CombatPile.Play, playIndex);
        journal.AddPile(state, destination, playedInstanceId);
    }

    private CombatPreparedTransition PrepareEndTurn(CombatInformationState state, CombatMutationJournal journal)
    {
        CleanupPlayerHand(state, journal);
        DecrementPlayerEndTurnStatuses(state, journal);
        List<IReadOnlyList<MonsterIntentTransition>> transitions = [];
        double immediateReward = 0d;
        for (int index = 0; index < state.Monsters.Count; index++)
        {
            CombatMonsterState monsterState = state.Monsters[index];
            if (!monsterState.IsAlive)
            {
                transitions.Add([]);
                continue;
            }

            CombatMonsterDefinition monster = GetMonster(monsterState.TypeName);
            if (!monster.IsSupported || !monster.Intents.TryGetValue(monsterState.IntentStateId, out MonsterIntentDefinition? intent))
            {
                throw new InvalidOperationException($"Unsupported current intent {monsterState.TypeName}/{monsterState.IntentStateId}.");
            }

            journal.SetScalar(state, CombatScalarField.MonsterBlock, 0, index);
            foreach (MonsterIntentEffect effect in intent.Effects)
            {
                immediateReward += ApplyMonsterEffect(state, index, effect, journal);
                if (!state.Player.IsAlive)
                {
                    break;
                }
            }

            DecrementMonsterEndTurnStatuses(state, index, journal);
            transitions.Add(intent.Transitions);
            if (!state.Player.IsAlive)
            {
                for (int remaining = index + 1; remaining < state.Monsters.Count; remaining++)
                {
                    transitions.Add([]);
                }
                break;
            }
        }

        journal.AddLedger(state, CombatLedgerField.UnusedPlayerBlock, state.Player.Block);
        journal.SetScalar(state, CombatScalarField.PlayerBlock, 0);
        if (state.Player.IsAlive && state.BlockNextTurn > 0)
        {
            int blockNextTurn = state.BlockNextTurn;
            journal.AddScalar(state, CombatScalarField.PlayerBlock, blockNextTurn);
            journal.SetScalar(state, CombatScalarField.BlockNextTurn, 0);
        }
        journal.AddScalar(state, CombatScalarField.Turn, 1);
        journal.SetScalar(state, CombatScalarField.Energy, state.MaxEnergy);
        if (state.Player.IsAlive && state.EnergyNextTurn > 0)
        {
            int energyNextTurn = state.EnergyNextTurn;
            journal.AddScalar(state, CombatScalarField.Energy, energyNextTurn);
            journal.SetScalar(state, CombatScalarField.EnergyNextTurn, 0);
        }
        if (state.Player.IsAlive && state.StarsNextTurn > 0)
        {
            int starsNextTurn = state.StarsNextTurn;
            journal.AddScalar(state, CombatScalarField.Stars, starsNextTurn);
            journal.SetScalar(state, CombatScalarField.StarsNextTurn, 0);
        }
        journal.SetScalar(
            state,
            CombatScalarField.CardsPlayedPreviousTurn,
            state.PaleBlueDotDraw > 0 ? state.CardsPlayedThisTurn : 0);
        journal.SetScalar(state, CombatScalarField.CardsPlayed, 0);
        journal.SetScalar(state, CombatScalarField.AttacksPlayed, 0);
        journal.SetScalar(state, CombatScalarField.SkillsPlayed, 0);
        int drawCount = 0;
        if (state.Player.IsAlive)
        {
            drawCount = _options.HandSize + state.DrawNextTurn;
            if (state.CardsPlayedPreviousTurn >= 5)
            {
                drawCount += state.PaleBlueDotDraw;
            }
            journal.SetScalar(state, CombatScalarField.DrawNextTurn, 0);
        }
        return new CombatPreparedTransition(immediateReward, drawCount, transitions);
    }

    private double ApplyCardEffect(
        CombatInformationState state,
        CombatCardDefinition card,
        CombatCardInstanceState cardInstance,
        CombatCardEffect effect,
        int selectedTarget,
        CombatMutationJournal journal,
        ref int drawCount)
    {
        switch (effect.Kind)
        {
            case CombatCardEffectKind.Damage:
                int damageAmount = effect.Amount +
                    (IsSovereignBlade(card) && string.Equals(effect.DynamicVarName, "Damage", StringComparison.OrdinalIgnoreCase)
                        ? cardInstance.ForgeDamageBonus
                        : 0);
                if (effect.Target == CombatCardTarget.AllEnemies)
                {
                    double reward = 0d;
                    for (int index = 0; index < state.Monsters.Count; index++)
                    {
                        reward += _damage.ResolvePlayerAttack(state, index, damageAmount, effect.HitCount, journal).OffenseReward;
                    }
                    return reward;
                }
                return _damage.ResolvePlayerAttack(state, selectedTarget, damageAmount, effect.HitCount, journal).OffenseReward;
            case CombatCardEffectKind.Block:
                _damage.GainPlayerBlock(
                    state,
                    effect.Amount + (card.DefendTagged ? state.FastenDefendBlock : 0),
                    journal);
                return 0d;
            case CombatCardEffectKind.Draw:
                drawCount += effect.Amount;
                return 0d;
            case CombatCardEffectKind.DrawNextTurn:
                journal.AddScalar(state, CombatScalarField.DrawNextTurn, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainEnergy:
                journal.AddScalar(state, CombatScalarField.Energy, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainEnergyNextTurn:
                journal.AddScalar(state, CombatScalarField.EnergyNextTurn, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainStars:
                journal.AddScalar(state, CombatScalarField.Stars, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainStarsNextTurn:
                journal.AddScalar(state, CombatScalarField.StarsNextTurn, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainBlockNextTurn:
                journal.AddScalar(
                    state,
                    CombatScalarField.BlockNextTurn,
                    _damage.CalculatePlayerBlockAmount(
                        state,
                        effect.Amount + (card.DefendTagged ? state.FastenDefendBlock : 0)));
                return 0d;
            case CombatCardEffectKind.GainRetainHand:
                journal.AddScalar(state, CombatScalarField.RetainHandTurns, effect.Amount);
                return 0d;
            case CombatCardEffectKind.Forge:
                ApplyForge(state, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.SelectAndMoveCard:
                CombatCardSelectionSpec selection = effect.CardSelection
                    ?? throw new InvalidOperationException("Select/move effect has no physical selection specification.");
                if (state.PendingCardSelection is not null)
                {
                    throw new InvalidOperationException("Nested card selections are outside the Phase 1 exact slice.");
                }
                journal.SetPendingCardSelection(
                    state,
                    new CombatPendingCardSelectionState(
                        selection,
                        cardInstance.InstanceId,
                        card.Exhausts ? CombatPile.Exhaust : CombatPile.Discard,
                        selection.Count));
                return 0d;
            case CombatCardEffectKind.InstallChildOfTheStars:
                journal.AddScalar(state, CombatScalarField.ChildOfTheStarsBlockPerStar, effect.Amount);
                return 0d;
            case CombatCardEffectKind.InstallOrbit:
                journal.AddOrbitPower(state, effect.Amount);
                return 0d;
            case CombatCardEffectKind.InstallPaleBlueDot:
                journal.AddScalar(state, CombatScalarField.PaleBlueDotDraw, effect.Amount);
                return 0d;
            case CombatCardEffectKind.InstallFasten:
                journal.AddScalar(state, CombatScalarField.FastenDefendBlock, effect.Amount);
                return 0d;
            case CombatCardEffectKind.HealPlayer:
                _damage.HealPlayer(state, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.LosePlayerHp:
                _damage.LosePlayerHp(state, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.ApplyWeak:
                ApplyMonsterStatus(state, selectedTarget, CombatScalarField.MonsterWeak, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.ApplyVulnerable:
                ApplyMonsterStatus(state, selectedTarget, CombatScalarField.MonsterVulnerable, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.ApplyFrail:
                ApplyMonsterStatus(state, selectedTarget, CombatScalarField.MonsterFrail, effect.Amount, journal);
                return 0d;
            case CombatCardEffectKind.GainStrength:
                journal.AddScalar(state, CombatScalarField.PlayerStrength, effect.Amount);
                return 0d;
            case CombatCardEffectKind.GainDexterity:
                journal.AddScalar(state, CombatScalarField.PlayerDexterity, effect.Amount);
                return 0d;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect.Kind));
        }
    }

    private double ApplyMonsterEffect(
        CombatInformationState state,
        int monsterIndex,
        MonsterIntentEffect effect,
        CombatMutationJournal journal)
    {
        switch (effect.Kind)
        {
            case MonsterIntentEffectKind.Attack:
                _damage.ResolveMonsterAttack(state, monsterIndex, effect.Amount, effect.HitCount, journal);
                return 0d;
            case MonsterIntentEffectKind.Block:
                _damage.GainMonsterBlock(state, monsterIndex, effect.Amount, journal);
                return 0d;
            case MonsterIntentEffectKind.ApplyWeak:
                journal.AddScalar(state, CombatScalarField.PlayerWeak, effect.Amount);
                return 0d;
            case MonsterIntentEffectKind.ApplyVulnerable:
                journal.AddScalar(state, CombatScalarField.PlayerVulnerable, effect.Amount);
                return 0d;
            case MonsterIntentEffectKind.ApplyFrail:
                journal.AddScalar(state, CombatScalarField.PlayerFrail, effect.Amount);
                return 0d;
            case MonsterIntentEffectKind.GainStrength:
                journal.AddScalar(state, CombatScalarField.MonsterStrength, effect.Amount, monsterIndex);
                return 0d;
            case MonsterIntentEffectKind.HealSelf:
                return -_damage.HealMonster(state, monsterIndex, effect.Amount, journal);
            default:
                throw new ArgumentOutOfRangeException(nameof(effect.Kind));
        }
    }

    private static void ApplyMonsterStatus(
        CombatInformationState state,
        int target,
        CombatScalarField field,
        int amount,
        CombatMutationJournal journal)
    {
        if (target < 0 || target >= state.Monsters.Count || !state.Monsters[target].IsAlive)
        {
            throw new InvalidOperationException("A living monster target is required.");
        }
        journal.AddScalar(state, field, amount, target);
    }

    private static void TriggerChildOfTheStarsAfterStarsSpent(
        CombatInformationState state,
        int starsSpent,
        CombatMutationJournal journal)
    {
        if (starsSpent <= 0 || state.ChildOfTheStarsBlockPerStar <= 0)
        {
            return;
        }

        journal.AddScalar(
            state,
            CombatScalarField.PlayerBlock,
            state.ChildOfTheStarsBlockPerStar * starsSpent);
    }

    private void ApplyForge(
        CombatInformationState state,
        int amount,
        CombatMutationJournal journal)
    {
        if (amount <= 0)
        {
            return;
        }
        if (!_cards.TryGet("CARD.SOVEREIGN_BLADE+0", out CombatCardDefinition? bladeDefinition) ||
            !bladeDefinition.IsSupported)
        {
            throw new InvalidOperationException("Forge requires the supported unupgraded Sovereign Blade definition.");
        }

        bool hasUnexhaustedBlade = state.EnumerateMutablePiles()
            .Where(pile => !ReferenceEquals(pile, state.MutableExhaust))
            .SelectMany(pile => pile)
            .Any(instanceId => IsSovereignBlade(_cards.Get(state.GetDefinitionId(instanceId))));
        if (!hasUnexhaustedBlade)
        {
            CombatPile destination = state.MutableHand.Count < _options.MaxHandSize
                ? CombatPile.Hand
                : CombatPile.Discard;
            journal.AddCardInstance(state, bladeDefinition.DefinitionId, destination);
        }

        int[] blades = state.CardInstances.Values
            .Where(instance => IsSovereignBlade(_cards.Get(instance.DefinitionId)))
            .Select(instance => instance.InstanceId)
            .Order()
            .ToArray();
        foreach (int instanceId in blades)
        {
            CombatCardInstanceState blade = state.GetCardInstance(instanceId);
            journal.SetCardInstance(
                state,
                instanceId,
                blade.DefinitionId,
                checked(blade.ForgeDamageBonus + amount));
        }
    }

    private static bool IsSovereignBlade(CombatCardDefinition card) =>
        CardBehaviorCatalog.ForCardIdentity(card.TypeName, card.ModelId)
            .Has(CardBehaviorKind.SovereignBlade);

    private static void TriggerOrbitAfterEnergySpent(
        CombatInformationState state,
        int energySpent,
        CombatMutationJournal journal)
    {
        if (energySpent <= 0 || state.MutableOrbitPowers.Count == 0)
        {
            return;
        }

        int energyGained = 0;
        for (int index = 0; index < state.MutableOrbitPowers.Count; index++)
        {
            CombatOrbitPowerState power = state.MutableOrbitPowers[index];
            int total = power.EnergyProgress + energySpent;
            energyGained += power.Amount * (total / 4);
            journal.SetOrbitProgress(state, index, total % 4);
        }

        if (energyGained > 0)
        {
            journal.AddScalar(state, CombatScalarField.Energy, energyGained);
        }
    }

    private void CleanupPlayerHand(CombatInformationState state, CombatMutationJournal journal)
    {
        bool retainWholeHand = state.RetainHandTurns > 0;
        for (int index = state.MutableHand.Count - 1; index >= 0; index--)
        {
            CombatCardDefinition card = _cards.Get(state.GetDefinitionId(state.MutableHand[index]));
            if (card.Ethereal)
            {
                int exhausted = journal.RemovePileAt(state, CombatPile.Hand, index);
                journal.AddPile(state, CombatPile.Exhaust, exhausted);
            }
            else if (!retainWholeHand && !card.Retains)
            {
                int discarded = journal.RemovePileAt(state, CombatPile.Hand, index);
                journal.AddPile(state, CombatPile.Discard, discarded);
            }
        }

        if (state.RetainHandTurns > 0)
        {
            journal.AddScalar(state, CombatScalarField.RetainHandTurns, -1);
        }
    }

    private static void DecrementPlayerEndTurnStatuses(CombatInformationState state, CombatMutationJournal journal)
    {
        if (state.Player.Weak > 0) journal.AddScalar(state, CombatScalarField.PlayerWeak, -1);
        if (state.Player.Vulnerable > 0) journal.AddScalar(state, CombatScalarField.PlayerVulnerable, -1);
        if (state.Player.Frail > 0) journal.AddScalar(state, CombatScalarField.PlayerFrail, -1);
    }

    private static void DecrementMonsterEndTurnStatuses(
        CombatInformationState state,
        int monsterIndex,
        CombatMutationJournal journal)
    {
        CombatMonsterState monster = state.Monsters[monsterIndex];
        if (monster.Weak > 0) journal.AddScalar(state, CombatScalarField.MonsterWeak, -1, monsterIndex);
        if (monster.Vulnerable > 0) journal.AddScalar(state, CombatScalarField.MonsterVulnerable, -1, monsterIndex);
        if (monster.Frail > 0) journal.AddScalar(state, CombatScalarField.MonsterFrail, -1, monsterIndex);
    }

    private CombatMonsterDefinition GetMonster(string typeName) =>
        _monsters.TryGetValue(typeName, out CombatMonsterDefinition? monster)
            ? monster
            : throw new KeyNotFoundException($"Monster '{typeName}' was not compiled.");

    private static IReadOnlyList<IReadOnlyList<MonsterIntentTransition>> EmptyMonsterTransitions(int count) =>
        [];
}
