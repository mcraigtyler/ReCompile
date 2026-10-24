using System.Collections.Generic;
using System.Linq;
using Compile.Engine.Views;

namespace Compile.Engine
{
    // Builds what one player is allowed to see. Hidden information never reaches a view: the opponent's
    // hand and deck are counts, the opponent's face-down cards have no identity, and hidden cards carry
    // ids that were re-issued when they were hidden (see Rekey), so they can't be matched to earlier sightings.
    public sealed partial class Game
    {
        const int LogLength = 40;

        public PlayerView ViewFor(int viewer)
        {
            var state = State;
            var other = GameState.Other(viewer);
            var view = new PlayerView
            {
                Stage = state.Stage,
                Local = Side(viewer, viewer),
                Opponent = Side(other, viewer),
                Hand = state.Players[viewer].HandCards.Select(c => Info(c, viewer)).ToList(),
                Turn = state.Turn,
                LocalTurn = state.ActiveSeat == viewer,
                Phase = state.Phase,
                Control = state.Control == GameState.Neutral ? ControlHolder.Neutral
                    : state.Control == viewer ? ControlHolder.Local : ControlHolder.Opponent,
                CanAct = AwaitingAction && state.ActiveSeat == viewer,
                CanRefresh = CheckRefresh(viewer) == null,
                CanEndTurn = AwaitingEndTurn && state.ActiveSeat == viewer || DraftConfirmSeat == viewer,
                CanUndo = CheckUndo(viewer) == null,
                // Only a real barrier has a reason worth showing ("You drew cards this turn").
                UndoBlockedReason = state.ActiveSeat == viewer && CheckUndo(viewer) != null ? _undoBlockedBy : null,
                Log = Log(viewer),
                EndReason = state.EndReason,
            };

            if (state.Stage == GameStage.Draft)
            {
                var draft = state.Draft;
                view.Draft = new DraftInfo
                {
                    Available = draft.Available.ToList(),
                    LocalPicks = draft.PicksFor(viewer).ToList(),
                    OpponentPicks = draft.PicksFor(other).ToList(),
                    LocalPick = DraftConfirmSeat < 0 && draft.CurrentSeat == viewer,
                    LocalConfirm = DraftConfirmSeat == viewer,
                    CurrentSeatIsLocal = DraftSeat == viewer,
                    PicksRemaining = draft.PicksRemainingThisTurn,
                };
                return view;
            }

            if (state.Stage == GameStage.Over)
                view.Outcome = state.Winner < 0 ? Outcome.Draw : state.Winner == viewer ? Outcome.LocalWon : Outcome.OpponentWon;

            view.Lines = Enumerable.Range(0, GameState.LineCount).Select(local =>
            {
                var fieldLine = GameState.ToField(viewer, local);
                var line = state.Lines[fieldLine];
                return new LineInfo
                {
                    Index = local,
                    LocalProtocol = Protocol(state.ProtocolIn(viewer, fieldLine)),
                    OpponentProtocol = Protocol(state.ProtocolIn(other, fieldLine)),
                    Local = Stack(viewer, fieldLine, viewer),
                    Opponent = Stack(other, fieldLine, viewer),
                };
            }).ToList();

            if (Pending != null && Pending.Seat == viewer)
            {
                var choice = Pending;
                view.Choice = new ChoiceInfo
                {
                    Kind = choice.Kind,
                    Prompt = choice.Prompt,
                    ValidCards = choice.ValidCards.ToList(),
                    ValidLines = choice.ValidLines.ToList(),
                    Options = choice.Options.ToList(),
                    Min = choice.Min,
                    Max = choice.Max,
                    Optional = choice.Optional,
                    Side = choice.TargetSeat < 0 ? ChoiceSide.Either : choice.TargetSeat == viewer ? ChoiceSide.Local : ChoiceSide.Opponent,
                    Purpose = choice.Purpose,
                    Verb = choice.Verb,
                    FaceUpAllowed = choice.FaceUpAllowed,
                    FaceDownAllowed = choice.FaceDownAllowed,
                    SourceCardId = choice.Source?.Spec.Id,
                    SourceInstanceId = choice.Source?.Id ?? 0,
                    SubjectInstanceId = choice.Subject?.Id ?? 0,
                };
            }
            return view;
        }

        StackInfo Stack(int seat, int fieldLine, int viewer)
        {
            var cards = State.Lines[fieldLine].StackCards[seat].Select(c =>
            {
                var info = Info(c, viewer);
                info.Value = CardValue(c);
                return info;
            }).ToList();
            return new StackInfo { Cards = cards, Total = LineValue(seat, fieldLine) };
        }

        SideInfo Side(int seat, int viewer)
        {
            var player = State.Players[seat];
            return new SideInfo
            {
                Name = player.Name,
                HandCount = player.HandCards.Count,
                DeckCount = player.DeckCards.Count,
                Trash = player.TrashCards.Select(c => Info(c, viewer)).ToList(),    // the trash is public
                RevealedHand = player.HandCards.Where(State.Revealed.Contains).Select(c => Info(c, viewer)).ToList(),
            };
        }

        static ProtocolInfo Protocol(ProtocolSlot slot) =>
            slot == null ? null : new ProtocolInfo { Name = slot.Name, Compiled = slot.Compiled };

        // Face-up cards are public; a face-down card is known only to its owner, unless it's revealed.
        CardInfo Info(CardInstance card, int viewer)
        {
            var revealed = State.Revealed.Contains(card);
            var visible = card.FaceUp || card.Owner == viewer || revealed;
            return new CardInfo
            {
                InstanceId = card.Id,
                CardId = visible ? card.Spec.Id : null,
                Protocol = visible ? card.Spec.Protocol : null,
                PrintedValue = visible ? card.Spec.Value : 0,
                FaceUp = card.FaceUp,
                LocalOwner = card.Owner == viewer,
                Revealed = revealed,
            };
        }

        List<LogEntry> Log(int viewer)
        {
            var entries = new List<LogEntry>();
            for (var i = _history.Count - 1; i >= 0 && entries.Count < LogLength; i--)
            {
                var text = _history[i].Describe(State, viewer);
                if (text != null)
                    entries.Add(new LogEntry { Text = text, Local = _history[i].Actor == viewer });
            }
            entries.Reverse();
            return entries;
        }
    }
}
