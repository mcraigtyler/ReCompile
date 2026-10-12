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
                    LocalPick = draft.CurrentSeat == viewer,
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
                    Local = new StackInfo { Cards = line.StackCards[viewer].Select(c => Info(c, viewer)).ToList() },
                    Opponent = new StackInfo { Cards = line.StackCards[other].Select(c => Info(c, viewer)).ToList() },
                };
            }).ToList();

            if (Pending != null && Pending.Seat == viewer)
            {
                view.Choice = new ChoiceInfo
                {
                    Kind = Pending.Kind,
                    Prompt = Pending.Prompt,
                    ValidCards = Pending.ValidCards.ToList(),
                    ValidLines = Pending.ValidLines.ToList(),
                    Min = Pending.Min,
                    Max = Pending.Max,
                    Optional = Pending.Optional,
                };
            }
            return view;
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
            };
        }

        static ProtocolInfo Protocol(ProtocolSlot slot) =>
            slot == null ? null : new ProtocolInfo { Name = slot.Name, Compiled = slot.Compiled };

        // Face-up cards are public; a face-down card is known only to its owner.
        static CardInfo Info(CardInstance card, int viewer)
        {
            var visible = card.FaceUp || card.Owner == viewer;
            return new CardInfo
            {
                InstanceId = card.Id,
                CardId = visible ? card.Spec.Id : null,
                Protocol = visible ? card.Spec.Protocol : null,
                PrintedValue = visible ? card.Spec.Value : 0,
                FaceUp = card.FaceUp,
                LocalOwner = card.Owner == viewer,
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
