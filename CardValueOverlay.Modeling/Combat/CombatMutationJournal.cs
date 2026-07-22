namespace CardValueOverlay.Modeling.Combat;

public enum CombatScalarField
{
    Turn,
    Energy,
    EnergyNextTurn,
    Stars,
    StarsNextTurn,
    BlockNextTurn,
    DrawNextTurn,
    RetainHandTurns,
    ChildOfTheStarsBlockPerStar,
    PaleBlueDotDraw,
    FastenDefendBlock,
    CardsPlayed,
    CardsPlayedPreviousTurn,
    AttacksPlayed,
    SkillsPlayed,
    NextCardInstanceId,
    PlayerHp,
    PlayerBlock,
    PlayerStrength,
    PlayerDexterity,
    PlayerWeak,
    PlayerVulnerable,
    PlayerFrail,
    MonsterHp,
    MonsterBlock,
    MonsterStrength,
    MonsterWeak,
    MonsterVulnerable,
    MonsterFrail
}

public enum CombatLedgerField
{
    ActualEnemyHpDamage,
    EnemyHpRestored,
    AttemptedDamage,
    EnemyBlockBroken,
    OverkillDamage,
    PlayerHpLost,
    PlayerHpHealed,
    UnusedPlayerBlock
}

public sealed class CombatMutationJournal
{
    private enum UndoKind
    {
        Scalar,
        Ledger,
        Intent,
        InsertPile,
        RemovePile,
        RemoveCardInstance,
        CardInstanceState,
        PendingCardSelection,
        RemoveOrbitPower,
        OrbitProgress
    }

    private readonly record struct UndoEntry(
        UndoKind Kind,
        CombatScalarField ScalarField = default,
        CombatLedgerField LedgerField = default,
        CombatPile Pile = default,
        int MonsterIndex = -1,
        int Position = -1,
        int IntValue = 0,
        int SecondIntValue = 0,
        double DoubleValue = 0,
        string? TextValue = null,
        CombatPendingCardSelectionState? PendingSelectionValue = null);

    private readonly List<UndoEntry> _entries = [];

    public int Mark() => _entries.Count;

    public void EnsureEmpty()
    {
        if (_entries.Count != 0)
        {
            throw new InvalidOperationException("Combat mutation journal was not empty before a new solve.");
        }
    }

    public void SetScalar(
        CombatInformationState state,
        CombatScalarField field,
        int value,
        int monsterIndex = -1)
    {
        int oldValue = GetScalar(state, field, monsterIndex);
        if (oldValue == value)
        {
            return;
        }

        _entries.Add(new UndoEntry(UndoKind.Scalar, field, MonsterIndex: monsterIndex, IntValue: oldValue));
        SetScalarDirect(state, field, value, monsterIndex);
    }

    public void AddScalar(
        CombatInformationState state,
        CombatScalarField field,
        int delta,
        int monsterIndex = -1) =>
        SetScalar(state, field, GetScalar(state, field, monsterIndex) + delta, monsterIndex);

    public void SetLedger(CombatInformationState state, CombatLedgerField field, double value)
    {
        double oldValue = GetLedger(state.Ledger, field);
        if (oldValue.Equals(value))
        {
            return;
        }

        _entries.Add(new UndoEntry(UndoKind.Ledger, LedgerField: field, DoubleValue: oldValue));
        SetLedgerDirect(state.Ledger, field, value);
    }

    public void AddLedger(CombatInformationState state, CombatLedgerField field, double delta) =>
        SetLedger(state, field, GetLedger(state.Ledger, field) + delta);

    public void SetIntent(CombatInformationState state, int monsterIndex, string intentStateId)
    {
        string oldValue = state.Monsters[monsterIndex].IntentStateId;
        if (string.Equals(oldValue, intentStateId, StringComparison.Ordinal))
        {
            return;
        }

        _entries.Add(new UndoEntry(UndoKind.Intent, MonsterIndex: monsterIndex, TextValue: oldValue));
        state.Monsters[monsterIndex].IntentStateId = intentStateId;
    }

    public int RemovePileAt(CombatInformationState state, CombatPile pile, int position)
    {
        List<int> values = state.GetPile(pile);
        int card = values[position];
        _entries.Add(new UndoEntry(UndoKind.InsertPile, Pile: pile, Position: position, IntValue: card));
        values.RemoveAt(position);
        return card;
    }

    public void InsertPile(CombatInformationState state, CombatPile pile, int position, int cardInstanceId)
    {
        List<int> values = state.GetPile(pile);
        _entries.Add(new UndoEntry(UndoKind.RemovePile, Pile: pile, Position: position));
        values.Insert(position, cardInstanceId);
    }

    public void AddPile(CombatInformationState state, CombatPile pile, int cardInstanceId) =>
        InsertPile(state, pile, state.GetPile(pile).Count, cardInstanceId);

    public int AddCardInstance(
        CombatInformationState state,
        int definitionId,
        CombatPile pile,
        int forgeDamageBonus = 0,
        int position = -1)
    {
        if (definitionId < 0 || forgeDamageBonus < 0)
        {
            throw new InvalidOperationException("Generated card instance state is invalid.");
        }

        int instanceId = state.NextCardInstanceId;
        SetScalar(state, CombatScalarField.NextCardInstanceId, instanceId + 1);
        _entries.Add(new UndoEntry(UndoKind.RemoveCardInstance, Position: instanceId));
        state.MutableCardInstances.Add(instanceId, new CombatCardInstanceState
        {
            InstanceId = instanceId,
            DefinitionId = definitionId,
            ForgeDamageBonus = forgeDamageBonus
        });
        int insertion = position < 0 ? state.GetPile(pile).Count : position;
        InsertPile(state, pile, insertion, instanceId);
        return instanceId;
    }

    public void SetCardInstance(
        CombatInformationState state,
        int instanceId,
        int definitionId,
        int forgeDamageBonus)
    {
        if (definitionId < 0 || forgeDamageBonus < 0)
        {
            throw new InvalidOperationException("Card instance state is invalid.");
        }

        CombatCardInstanceState card = state.GetCardInstance(instanceId);
        if (card.DefinitionId == definitionId && card.ForgeDamageBonus == forgeDamageBonus)
        {
            return;
        }

        _entries.Add(new UndoEntry(
            UndoKind.CardInstanceState,
            Position: instanceId,
            IntValue: card.DefinitionId,
            SecondIntValue: card.ForgeDamageBonus));
        card.DefinitionId = definitionId;
        card.ForgeDamageBonus = forgeDamageBonus;
    }

    public void SetPendingCardSelection(
        CombatInformationState state,
        CombatPendingCardSelectionState? value)
    {
        if (Equals(state.PendingCardSelection, value))
        {
            return;
        }

        _entries.Add(new UndoEntry(
            UndoKind.PendingCardSelection,
            PendingSelectionValue: state.PendingCardSelection));
        state.PendingCardSelection = value;
    }

    public void AddOrbitPower(CombatInformationState state, int amount)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Orbit power amount must be positive.");
        }

        int position = state.MutableOrbitPowers.Count;
        _entries.Add(new UndoEntry(UndoKind.RemoveOrbitPower, Position: position));
        state.MutableOrbitPowers.Add(new CombatOrbitPowerState { Amount = amount });
    }

    public void SetOrbitProgress(CombatInformationState state, int position, int progress)
    {
        if (progress is < 0 or >= 4)
        {
            throw new InvalidOperationException("Orbit energy progress must be in [0, 4).");
        }

        int oldValue = state.MutableOrbitPowers[position].EnergyProgress;
        if (oldValue == progress)
        {
            return;
        }

        _entries.Add(new UndoEntry(UndoKind.OrbitProgress, Position: position, IntValue: oldValue));
        state.MutableOrbitPowers[position].EnergyProgress = progress;
    }

    public void UndoTo(CombatInformationState state, int mark)
    {
        if (mark < 0 || mark > _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(mark));
        }

        for (int index = _entries.Count - 1; index >= mark; index--)
        {
            UndoEntry entry = _entries[index];
            switch (entry.Kind)
            {
                case UndoKind.Scalar:
                    SetScalarDirect(state, entry.ScalarField, entry.IntValue, entry.MonsterIndex);
                    break;
                case UndoKind.Ledger:
                    SetLedgerDirect(state.Ledger, entry.LedgerField, entry.DoubleValue);
                    break;
                case UndoKind.Intent:
                    state.Monsters[entry.MonsterIndex].IntentStateId = entry.TextValue!;
                    break;
                case UndoKind.InsertPile:
                    state.GetPile(entry.Pile).Insert(entry.Position, entry.IntValue);
                    break;
                case UndoKind.RemovePile:
                    state.GetPile(entry.Pile).RemoveAt(entry.Position);
                    break;
                case UndoKind.RemoveCardInstance:
                    state.MutableCardInstances.Remove(entry.Position);
                    break;
                case UndoKind.CardInstanceState:
                    CombatCardInstanceState card = state.GetCardInstance(entry.Position);
                    card.DefinitionId = entry.IntValue;
                    card.ForgeDamageBonus = entry.SecondIntValue;
                    break;
                case UndoKind.PendingCardSelection:
                    state.PendingCardSelection = entry.PendingSelectionValue;
                    break;
                case UndoKind.RemoveOrbitPower:
                    state.MutableOrbitPowers.RemoveAt(entry.Position);
                    break;
                case UndoKind.OrbitProgress:
                    state.MutableOrbitPowers[entry.Position].EnergyProgress = entry.IntValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        _entries.RemoveRange(mark, _entries.Count - mark);
    }

    private static int GetScalar(CombatInformationState state, CombatScalarField field, int monsterIndex) => field switch
    {
        CombatScalarField.Turn => state.Turn,
        CombatScalarField.Energy => state.Energy,
        CombatScalarField.EnergyNextTurn => state.EnergyNextTurn,
        CombatScalarField.Stars => state.Stars,
        CombatScalarField.StarsNextTurn => state.StarsNextTurn,
        CombatScalarField.BlockNextTurn => state.BlockNextTurn,
        CombatScalarField.DrawNextTurn => state.DrawNextTurn,
        CombatScalarField.RetainHandTurns => state.RetainHandTurns,
        CombatScalarField.ChildOfTheStarsBlockPerStar => state.ChildOfTheStarsBlockPerStar,
        CombatScalarField.PaleBlueDotDraw => state.PaleBlueDotDraw,
        CombatScalarField.FastenDefendBlock => state.FastenDefendBlock,
        CombatScalarField.CardsPlayed => state.CardsPlayedThisTurn,
        CombatScalarField.CardsPlayedPreviousTurn => state.CardsPlayedPreviousTurn,
        CombatScalarField.AttacksPlayed => state.AttacksPlayedThisTurn,
        CombatScalarField.SkillsPlayed => state.SkillsPlayedThisTurn,
        CombatScalarField.NextCardInstanceId => state.NextCardInstanceId,
        CombatScalarField.PlayerHp => state.Player.Hp,
        CombatScalarField.PlayerBlock => state.Player.Block,
        CombatScalarField.PlayerStrength => state.Player.Strength,
        CombatScalarField.PlayerDexterity => state.Player.Dexterity,
        CombatScalarField.PlayerWeak => state.Player.Weak,
        CombatScalarField.PlayerVulnerable => state.Player.Vulnerable,
        CombatScalarField.PlayerFrail => state.Player.Frail,
        CombatScalarField.MonsterHp => state.Monsters[monsterIndex].Hp,
        CombatScalarField.MonsterBlock => state.Monsters[monsterIndex].Block,
        CombatScalarField.MonsterStrength => state.Monsters[monsterIndex].Strength,
        CombatScalarField.MonsterWeak => state.Monsters[monsterIndex].Weak,
        CombatScalarField.MonsterVulnerable => state.Monsters[monsterIndex].Vulnerable,
        CombatScalarField.MonsterFrail => state.Monsters[monsterIndex].Frail,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static void SetScalarDirect(CombatInformationState state, CombatScalarField field, int value, int monsterIndex)
    {
        switch (field)
        {
            case CombatScalarField.Turn: state.Turn = value; break;
            case CombatScalarField.Energy: state.Energy = value; break;
            case CombatScalarField.EnergyNextTurn: state.EnergyNextTurn = value; break;
            case CombatScalarField.Stars: state.Stars = value; break;
            case CombatScalarField.StarsNextTurn: state.StarsNextTurn = value; break;
            case CombatScalarField.BlockNextTurn: state.BlockNextTurn = value; break;
            case CombatScalarField.DrawNextTurn: state.DrawNextTurn = value; break;
            case CombatScalarField.RetainHandTurns: state.RetainHandTurns = value; break;
            case CombatScalarField.ChildOfTheStarsBlockPerStar: state.ChildOfTheStarsBlockPerStar = value; break;
            case CombatScalarField.PaleBlueDotDraw: state.PaleBlueDotDraw = value; break;
            case CombatScalarField.FastenDefendBlock: state.FastenDefendBlock = value; break;
            case CombatScalarField.CardsPlayed: state.CardsPlayedThisTurn = value; break;
            case CombatScalarField.CardsPlayedPreviousTurn: state.CardsPlayedPreviousTurn = value; break;
            case CombatScalarField.AttacksPlayed: state.AttacksPlayedThisTurn = value; break;
            case CombatScalarField.SkillsPlayed: state.SkillsPlayedThisTurn = value; break;
            case CombatScalarField.NextCardInstanceId: state.NextCardInstanceId = value; break;
            case CombatScalarField.PlayerHp: state.Player.Hp = value; break;
            case CombatScalarField.PlayerBlock: state.Player.Block = value; break;
            case CombatScalarField.PlayerStrength: state.Player.Strength = value; break;
            case CombatScalarField.PlayerDexterity: state.Player.Dexterity = value; break;
            case CombatScalarField.PlayerWeak: state.Player.Weak = value; break;
            case CombatScalarField.PlayerVulnerable: state.Player.Vulnerable = value; break;
            case CombatScalarField.PlayerFrail: state.Player.Frail = value; break;
            case CombatScalarField.MonsterHp: state.Monsters[monsterIndex].Hp = value; break;
            case CombatScalarField.MonsterBlock: state.Monsters[monsterIndex].Block = value; break;
            case CombatScalarField.MonsterStrength: state.Monsters[monsterIndex].Strength = value; break;
            case CombatScalarField.MonsterWeak: state.Monsters[monsterIndex].Weak = value; break;
            case CombatScalarField.MonsterVulnerable: state.Monsters[monsterIndex].Vulnerable = value; break;
            case CombatScalarField.MonsterFrail: state.Monsters[monsterIndex].Frail = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static double GetLedger(CombatRewardLedger ledger, CombatLedgerField field) => field switch
    {
        CombatLedgerField.ActualEnemyHpDamage => ledger.ActualEnemyHpDamage,
        CombatLedgerField.EnemyHpRestored => ledger.EnemyHpRestored,
        CombatLedgerField.AttemptedDamage => ledger.AttemptedDamage,
        CombatLedgerField.EnemyBlockBroken => ledger.EnemyBlockBroken,
        CombatLedgerField.OverkillDamage => ledger.OverkillDamage,
        CombatLedgerField.PlayerHpLost => ledger.PlayerHpLost,
        CombatLedgerField.PlayerHpHealed => ledger.PlayerHpHealed,
        CombatLedgerField.UnusedPlayerBlock => ledger.UnusedPlayerBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static void SetLedgerDirect(CombatRewardLedger ledger, CombatLedgerField field, double value)
    {
        switch (field)
        {
            case CombatLedgerField.ActualEnemyHpDamage: ledger.ActualEnemyHpDamage = value; break;
            case CombatLedgerField.EnemyHpRestored: ledger.EnemyHpRestored = value; break;
            case CombatLedgerField.AttemptedDamage: ledger.AttemptedDamage = value; break;
            case CombatLedgerField.EnemyBlockBroken: ledger.EnemyBlockBroken = value; break;
            case CombatLedgerField.OverkillDamage: ledger.OverkillDamage = value; break;
            case CombatLedgerField.PlayerHpLost: ledger.PlayerHpLost = value; break;
            case CombatLedgerField.PlayerHpHealed: ledger.PlayerHpHealed = value; break;
            case CombatLedgerField.UnusedPlayerBlock: ledger.UnusedPlayerBlock = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

}
