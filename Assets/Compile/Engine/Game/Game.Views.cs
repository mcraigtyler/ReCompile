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
                Seat = viewer,
                Stage = state.Stage,
                Local = Side(viewer, viewer),
                Opponent = Side(other, viewer),
                Hand = state.Players[viewer].HandCards.Select(c => HandInfo(c, viewer)).ToList(),
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
                    Complete = draft.IsComplete,
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

        CardInfo HandInfo(CardInstance card, int viewer)
        {
            var info = Info(card, viewer);
            if (State.Stage == GameStage.Playing)
            {
                info.FaceUpLines = Placements(card, viewer, true);
                info.FaceDownLines = Placements(card, viewer, false);
            }
            return info;
        }

        bool[] Placements(CardInstance card, int viewer, bool faceUp) => Enumerable.Range(0, GameState.LineCount)
            .Select(local => CheckPlacement(viewer, card, GameState.ToField(viewer, local), faceUp) == null).ToArray();

        // One player's update after a change: the view, and the events the board needs, redacted for them.
        public MatchUpdate UpdateFor(int viewer, IReadOnlyList<GameEvent> events, int version) => new MatchUpdate
        {
            Version = version,
            View = ViewFor(viewer),
            Events = EventsFor(viewer, events),
        };

        public List<EventInfo> EventsFor(int viewer, IReadOnlyList<GameEvent> events)
        {
            var infos = new List<EventInfo>();
            foreach (var e in events)
            {
                switch (e)
                {
                    case CardsReturned r:
                        infos.Add(new EventInfo { Kind = EventKind.Returned, Seat = r.Seat, Cards = r.Cards.Select(c => new CardInfo { InstanceId = c.InstanceId }).ToList() });
                        break;
                    // Hand cards only by id, and only the viewer's own: the other hand stays hidden.
                    case CardsGiven g:
                        infos.Add(new EventInfo
                        {
                            Kind = EventKind.Given, Seat = g.Seat, ToSeat = g.ToSeat,
                            Cards = g.Seat == viewer ? g.Cards.Select(c => new CardInfo { InstanceId = c.InstanceId }).ToList() : new List<CardInfo>(),
                        });
                        break;
                    case HandCardsTaken t:
                        infos.Add(new EventInfo
                        {
                            Kind = EventKind.Taken, Seat = t.Seat, FromSeat = t.FromSeat,
                            Cards = t.FromSeat == viewer ? t.Cards.Select(c => new CardInfo { InstanceId = c.InstanceId }).ToList() : new List<CardInfo>(),
                        });
                        break;
                    // Shown to both players, so their identities are no secret.
                    case CardsRevealed v:
                        infos.Add(new EventInfo
                        {
                            Kind = EventKind.Revealed, Seat = v.Seat, Owner = v.Owner, InHand = v.InHand, Text = e.Describe(State, viewer),
                            Cards = v.Cards.Select(c => new CardInfo
                            {
                                InstanceId = c.InstanceId, CardId = c.CardId, Protocol = _catalog.Get(c.CardId).Protocol,
                                PrintedValue = _catalog.Get(c.CardId).Value, FaceUp = true, LocalOwner = v.Owner == viewer,
                            }).ToList(),
                        });
                        break;
                    case CardTextRuns _:
                    case ChoiceMadeFor _:
                        var text = e.Describe(State, viewer);
                        if (!string.IsNullOrEmpty(text))
                            infos.Add(new EventInfo { Kind = EventKind.Note, Seat = e.Actor, Text = text });
                        break;
                }
            }
            return infos;
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
