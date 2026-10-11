using System.Collections.Generic;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine;
using UnityEngine;

namespace Compile.Client.Screens
{
    // The Protocol draft, played on the mat: the pool is spread over the local player's lanes and each
    // pick moves into its owner's Protocol slot. The simulated opponent picks at random.
    public sealed class DraftScreen : TableScreen
    {
        const int Columns = 4;
        const float PoolScale = 0.6f;
        const float Pitch = 18f;
        const float OpponentThinkMin = 0.9f, OpponentThinkMax = 1.7f;
        const float PauseAfterDraft = 1.4f;

        // Pool area over the local lanes (mat pixels).
        static readonly Rect PoolPx = Rect.MinMaxRect(30f, 2420f, 1368f, 3720f);

        // From the far Protocol row to the bottom of the pool, in table units, plus room above for the HUD status bar.
        static readonly Rect ViewArea = Rect.MinMaxRect(-7f, MatLayout.ToTable(0f, 3740f).y, 7f, MatLayout.ToTable(0f, 1230f).y);

        readonly Dictionary<string, CardView> _pool = new Dictionary<string, CardView>();
        Draft _draft;
        System.Random _random;
        float _time;
        float _opponentPicksAt = -1f;
        float _finishesAt = -1f;
        CardReader _reader;

        int LocalSeat => App.Match.Start.LocalSeat;
        bool LocalTurn => !_draft.IsComplete && _draft.CurrentSeat == LocalSeat;

        protected override void OnEnter()
        {
            App.Mat.ClearProtocols();
            _random = new System.Random(App.Match.Start.Seed);
            var names = App.Cards.Protocols.Select(p => p.Name).OrderBy(n => n).ToList();
            _draft = new Draft(names, App.Match.Start.FirstSeat);

            // Hovered Protocols show in a docked panel beside the pool, not over it.
            _reader = CardReader.Create(App.Hud.Root);
            _reader.Rect.SetAsFirstSibling();
            LayoutReader();

            DealPool(names);
            App.CameraRig.Show(new CameraShot(ViewArea, Pitch, margin: 0.04f));
            InMatch.AddLeaveAction(App);
            UpdateTurn();
        }

        protected override void OnExit()
        {
            foreach (var card in _pool.Values)
                Object.Destroy(card.gameObject);
            _pool.Clear();
            Object.Destroy(_reader.gameObject);
            App.Hud.Zoom.Hide();
            App.Mat.HighlightSlots(true, -1);
        }

        void DealPool(IReadOnlyList<string> names)
        {
            var rows = Mathf.CeilToInt(names.Count / (float)Columns);
            var cell = new Vector2(PoolPx.width / Columns, PoolPx.height / rows);
            var deckPosition = MatLayout.ToCanvas(PoolPx.center.x, PoolPx.yMax + 400f);

            for (var i = 0; i < names.Count; i++)
            {
                var row = i / Columns;
                var inRow = Mathf.Min(Columns, names.Count - row * Columns);
                var column = i % Columns + (Columns - inRow) / 2f;
                var target = MatLayout.ToCanvas(PoolPx.xMin + cell.x * (column + 0.5f), PoolPx.yMin + cell.y * (row + 0.5f));

                var card = CardView.Create(App.Mat.Cards, App.Art.ProtocolSide(names[i], compiled: false), names[i]);
                card.Protocol = names[i];
                card.Clicked += OnCardClicked;
                card.Hovered += OnCardHovered;
                card.LongPressed += OnCardLongPressed;
                card.Place(deckPosition, -90f, PoolScale);
                card.MoveTo(target, -90f, PoolScale, 0.45f + i * 0.03f);
                _pool[names[i]] = card;
            }
        }

        public override void Tick(float deltaTime)
        {
            _time += deltaTime;
            LayoutReader();

            if (_opponentPicksAt >= 0f && _time >= _opponentPicksAt)
            {
                _opponentPicksAt = -1f;
                Pick(_draft.Available[_random.Next(_draft.Available.Count)]);
            }

            if (_finishesAt >= 0f && _time >= _finishesAt)
            {
                _finishesAt = -1f;
                App.Match.LocalProtocols = _draft.PicksFor(LocalSeat).ToArray();
                App.Match.OpponentProtocols = _draft.PicksFor(1 - LocalSeat).ToArray();
                App.Nav.Go(new BoardScreen());
            }
        }

        void OnCardClicked(CardView card)
        {
            if (LocalTurn && _pool.ContainsKey(card.Protocol))
                Pick(card.Protocol);
        }

        // Resting the pointer on a Protocol (desktop) shows it in the docked reader; long-press (touch)
        // opens the large zoom until it's closed.
        void OnCardHovered(CardView card, bool hovered)
        {
            if (hovered)
                _reader.Hover(card, App.Art.ProtocolSide(card.Protocol, compiled: false),
                    CardDescription.ForProtocol(App.Cards.GetProtocol(card.Protocol), compiled: false));
            else
                _reader.Unhover(card);
        }

        void OnCardLongPressed(CardView card) =>
            App.Hud.Zoom.ShowProtocol(card, App.Art.ProtocolSide(card.Protocol, compiled: false), App.Cards.GetProtocol(card.Protocol),
                compiled: false, pinned: true);

        // Wide screens: on the desk at the left of the mat. Tall screens: under the status bar, over the
        // Protocol rows (the pool stays clear).
        void LayoutReader()
        {
            var root = App.Hud.Root.rect;
            if (Screen.width >= Screen.height)
            {
                _reader.SetLayout(true, 460f, root.height - 120f, root.height - 120f);
                _reader.Dock(new Vector2(0f, 0.5f), new Vector2(28f, 0f));
            }
            else
            {
                var top = 32f + App.Hud.StatusBox.rect.height + 12f;
                _reader.SetLayout(false, root.width - 48f, 420f, 420f);
                _reader.Dock(new Vector2(0.5f, 1f), new Vector2(0f, -top));
            }
        }

        void Pick(string protocol)
        {
            var seat = _draft.CurrentSeat;
            var slot = _draft.Pick(protocol);

            var card = _pool[protocol];
            _pool.Remove(protocol);
            card.Clicked -= OnCardClicked;
            card.Hovered -= OnCardHovered;
            card.LongPressed -= OnCardLongPressed;
            App.Mat.PlaceProtocol(card, App.Match.IsLocal(seat), slot);
            _reader.Unhover(card);
            App.Hud.Zoom.Release(card);

            UpdateTurn();
        }

        void UpdateTurn()
        {
            var pickNumber = Mathf.Min(6, App.Cards.Protocols.Count - _draft.Available.Count + 1);

            foreach (var card in _pool.Values)
            {
                card.Interactive = LocalTurn;
                card.SetHighlight(false);
            }

            if (_draft.IsComplete)
            {
                App.Mat.HighlightSlots(true, -1);
                App.Hud.SetStatus(":\\> draft complete", "Protocols loaded. Preparing the field...");
                _finishesAt = _time + PauseAfterDraft;
                return;
            }

            var seat = _draft.CurrentSeat;
            var local = App.Match.IsLocal(seat);
            App.Mat.HighlightSlots(local, _draft.PicksFor(seat).Count);

            var remaining = _draft.PicksRemainingThisTurn;
            var label = $":\\> pick {pickNumber} of 6";
            if (local)
            {
                App.Hud.SetStatus(label, $"Your pick: choose {remaining} Protocol{(remaining > 1 ? "s" : "")}");
            }
            else
            {
                App.Hud.SetStatus(label, $"{App.Match.NameOf(seat)} is choosing...");
                _opponentPicksAt = _time + Mathf.Lerp(OpponentThinkMin, OpponentThinkMax, (float)_random.NextDouble());
            }
        }
    }
}
