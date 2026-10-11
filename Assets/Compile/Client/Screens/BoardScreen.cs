using System;
using System.Collections.Generic;
using System.Linq;
using Compile.Client.App;
using Compile.Client.Board;
using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine.Views;
using Compile.Net;
using UnityEngine;

namespace Compile.Client.Screens
{
    // The game board for the local player. Runs on BoardSandbox (a stand-in for the rules engine) with
    // a simulated opponent, so the layout, card handling, and prompts can be built before Phase 3.
    public sealed class BoardScreen : TableScreen
    {
        const float Pitch = 40f;
        const float PhaseStep = 0.35f;
        const float HandSpace = 0.5f;
        const float OpponentStackReach = 9.6f;   // far edge of the opponent's first card in each lane (table units)   // table units below the mat; the hand overlay may cover the near end of the lanes

        BoardSandbox _sandbox;
        BoardTable _table;
        BoardHud _hud;
        PlayerView _view;

        int _selected = -1;
        bool _faceUp = true;
        readonly List<(float at, Action action)> _timeline = new List<(float, Action)>();
        float _time;
        string _promptKey;

        bool _rearranging;
        CardView[] _rearrangeSlots;
        int[] _rearrangeOrder;
        int _rearrangePick = -1;

        bool TurnRunning => _timeline.Count > 0;
        bool CanAct => _view != null && _view.LocalTurn && _view.Phase == TurnPhase.Action && _view.Choice == null && !_rearranging && !TurnRunning;

        protected override void OnEnter()
        {
            var match = App.Match;
            var names = App.Cards.Protocols.Select(p => p.Name).OrderBy(n => n).ToList();
            var local = match.LocalProtocols ?? names.Take(3).ToArray();
            var opponent = match.OpponentProtocols ?? names.Skip(3).Take(3).ToArray();
            _sandbox = new BoardSandbox(App.Cards, local, opponent, match.Start.LocalName, match.Start.OpponentName, match.Start.Seed);
            _sandbox.Changed += Render;

            _table = new GameObject("BoardTable").AddComponent<BoardTable>();
            _table.transform.SetParent(App.Table, false);
            _table.Init(App.Mat, App.Art, App.Theme);
            _table.CardClicked += OnTableCardClicked;
            _table.CardHovered += (card, on) => ZoomTableCard(card, on, pinned: false);
            _table.CardLongPressed += card => ZoomTableCard(card, true, pinned: true);
            _table.ProtocolHovered += (card, on) => ZoomProtocol(card, on, pinned: false);
            _table.ProtocolLongPressed += card => ZoomProtocol(card, true, pinned: true);
            _table.ProtocolClicked += OnProtocolClicked;
            _table.LaneClicked += PlaySelected;
            _table.LaneDropped += PlaySelected;
            _table.TrashClicked += ShowTrash;

            _hud = BoardHud.Create(App.Hud.Root, App.Art, App.Theme);
            _hud.Hand.CardTapped += OnHandCardTapped;
            _hud.Hand.CardRightClicked += OnHandCardRightClicked;
            _hud.Hand.CardHovered += (id, on) => ZoomHandCard(id, on, pinned: false);
            _hud.Hand.CardLongPressed += id => ZoomHandCard(id, true, pinned: true);
            _hud.Hand.DragStarted += id => Select(id, DefaultOrientation(id));
            _hud.OrientationChosen += SetOrientation;
            _hud.RefreshPressed += Refresh;
            _hud.ZoomPressed += () => ZoomHandCard(_selected, true, pinned: true);
            _hud.TrashPressed += ShowTrash;
            AddDebugActions();

            App.CameraRig.Show(Frame);
            Render();

            if (match.Start.FirstSeat != match.Start.LocalSeat)
                RunOpponentTurn(startDelay: 0.6f);
            else
                _sandbox.SetPhase(TurnPhase.Action, localTurn: true);
        }

        protected override void OnExit()
        {
            _sandbox.Changed -= Render;
            _table.Dispose();
            UnityEngine.Object.Destroy(_hud.gameObject);
            App.Hud.Zoom.Hide();
        }

        // Your lanes and both Protocol rows from a seat at the near edge, leaving room for the hand
        // below and the phase bar above. Wide screens also show the decks, trashes, and Control beside the mat.
        CameraShot Frame()
        {
            var wide = Screen.width >= Screen.height;
            var halfWidth = wide ? BoardTable.DeskReach : MatLayout.SizeUnits.x / 2f + 0.2f;
            var bottom = -MatLayout.SizeUnits.y / 2f - HandSpace;
            var top = OpponentStackReach + (wide ? 1.0f : 2.4f);
            return new CameraShot(Rect.MinMaxRect(-halfWidth, bottom, halfWidth, top), Pitch, margin: 0.01f);
        }

        public override void Tick(float deltaTime)
        {
            _time += deltaTime;
            _table.SetDeskVisible(Screen.width >= Screen.height);
            while (_timeline.Count > 0 && _timeline[0].at <= _time)
            {
                var action = _timeline[0].action;
                _timeline.RemoveAt(0);
                action();
            }
        }

        void Render()
        {
            _view = _sandbox.View();
            _table.Render(_view);
            _hud.Render(_view, App.Match.Start.LocalName);

            if (_selected >= 0 && _view.Hand.All(c => c.InstanceId != _selected))
                _selected = -1;
            _hud.Hand.Select(_selected, _faceUp);
            UpdateTargets();
            UpdatePrompt();
        }

        void UpdateTargets()
        {
            _hud.SetActionState(_selected >= 0, _faceUp, _selected >= 0 && _sandbox.CanPlayFaceUpAnywhere(_selected), CanAct && _sandbox.CanRefresh);

            if (_selected >= 0 && CanAct)
                _table.ShowLanes(Enumerable.Range(0, 3).Select(line => !_faceUp || _sandbox.CanPlayFaceUp(_selected, line)).ToArray());
            else
                _table.ShowLanes(null);

            if (!_rearranging)
                _table.ShowChoice(_view.Choice?.ValidCards);
        }

        void UpdatePrompt()
        {
            string key = null;
            if (_view.Choice != null)
                key = "choice:" + _view.Choice.Prompt;
            else if (_rearranging)
                key = "rearrange";

            if (key == _promptKey)
                return;
            _promptKey = key;

            if (_view.Choice != null)
            {
                if (_view.Choice.Optional)
                    _hud.ShowPrompt(_view.Choice.Prompt, ("Skip", ButtonStyle.Secondary, () => _sandbox.AnswerChoice(-1)));
                else
                    _hud.ShowPrompt(_view.Choice.Prompt);
            }
            else if (!_rearranging)
            {
                _hud.HidePrompt();
            }
        }

        // ---------- Selecting and playing ----------

        bool DefaultOrientation(int id) => _sandbox.CanPlayFaceUpAnywhere(id);

        void OnHandCardTapped(int id)
        {
            if (_selected == id)
                Select(-1, true);
            else
                Select(id, DefaultOrientation(id));

            if (_selected >= 0 && !CanAct && !_rearranging && _view.Choice == null)
                App.Hud.Toast(_view.LocalTurn ? "You can play a card during your Action step." : "Wait for your turn to play a card.");
        }

        void OnHandCardRightClicked(int id)
        {
            if (_selected != id)
                Select(id, DefaultOrientation(id));
            SetOrientation(!_faceUp);
        }

        void Select(int id, bool faceUp)
        {
            _selected = id;
            _faceUp = faceUp;
            _hud.Hand.Select(id, faceUp);
            UpdateTargets();
        }

        void SetOrientation(bool faceUp)
        {
            if (_selected < 0)
                return;
            if (faceUp && !_sandbox.CanPlayFaceUpAnywhere(_selected))
            {
                var protocol = _hud.Hand.InfoFor(_selected)?.Protocol;
                App.Hud.Toast($"{protocol} cards can only be played face-up into a {protocol} line.", error: true);
                return;
            }
            _faceUp = faceUp;
            _hud.Hand.SetSelectedFaceUp(faceUp);
            UpdateTargets();
        }

        void PlaySelected(int line)
        {
            if (_selected < 0 || !CanAct)
                return;
            if (!_sandbox.Play(_selected, line, _faceUp, out var error))
            {
                App.Hud.Toast(error, error: true);
                return;
            }
            Select(-1, true);
            FinishLocalTurn();
        }

        void Refresh()
        {
            if (!CanAct || !_sandbox.CanRefresh)
                return;
            _sandbox.Refresh();
            FinishLocalTurn();
        }

        // ---------- Simulated turn flow ----------

        void Schedule(float delay, Action action)
        {
            var at = (_timeline.Count > 0 ? _timeline[_timeline.Count - 1].at : _time) + delay;
            _timeline.Add((at, action));
        }

        void FinishLocalTurn()
        {
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Cache, true));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.End, true));
            RunOpponentTurn(PhaseStep);
        }

        void RunOpponentTurn(float startDelay)
        {
            Schedule(startDelay, () => _sandbox.SetPhase(TurnPhase.Start, false));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Control, false));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Compile, false));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Action, false));
            Schedule(1.0f, () => _sandbox.OpponentPlays());
            Schedule(PhaseStep * 2f, () => _sandbox.SetPhase(TurnPhase.Cache, false));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.End, false));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Start, true));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Control, true));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Compile, true));
            Schedule(PhaseStep, () => _sandbox.SetPhase(TurnPhase.Action, true));
        }

        // ---------- Zoom ----------

        void ZoomHandCard(int id, bool on, bool pinned)
        {
            var info = _hud.Hand.InfoFor(id);
            if (info == null)
                return;
            var subject = (object)("hand:" + id);
            if (!on)
            {
                App.Hud.Zoom.Release(subject);
                return;
            }
            App.Hud.Zoom.ShowCard(subject, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                faceUp: true, hiddenFromViewer: false, pinned);
        }

        void ZoomTableCard(CardView card, bool on, bool pinned)
        {
            var info = _table.InfoFor(card);
            if (info == null)
                return;
            if (!on)
            {
                App.Hud.Zoom.Release(card);
                return;
            }
            if (info.IsHidden)
                App.Hud.Zoom.ShowCard(card, App.Theme.cardBack, null, faceUp: false, hiddenFromViewer: true, pinned);
            else
                App.Hud.Zoom.ShowCard(card, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                    info.FaceUp, hiddenFromViewer: false, pinned);
        }

        void ZoomProtocol(CardView card, bool on, bool pinned)
        {
            if (!on)
            {
                App.Hud.Zoom.Release(card);
                return;
            }
            App.Hud.Zoom.ShowProtocol(card, App.Art.ProtocolSide(card.Protocol, card.Compiled), App.Cards.GetProtocol(card.Protocol),
                card.Compiled, pinned);
        }

        void ShowTrash(bool local)
        {
            var side = local ? _view.Local : _view.Opponent;
            _hud.ShowTrash(local ? "Your trash" : $"{side.Name}'s trash", side.Trash,
                info => App.Hud.Zoom.ShowCard(info, App.Art.Card(info.Protocol, info.PrintedValue), App.Cards.GetCard(info.CardId),
                    faceUp: true, hiddenFromViewer: false, pinned: true));
        }

        // ---------- Choices ----------

        void OnTableCardClicked(CardView card)
        {
            if (_view.Choice != null)
                _sandbox.AnswerChoice(card.InstanceId);
        }

        // ---------- Rearranging ----------

        void StartRearrange()
        {
            if (_rearranging || TurnRunning)
                return;
            Select(-1, true);
            _rearranging = true;
            _rearrangeOrder = new[] { 0, 1, 2 };
            _rearrangeSlots = Enumerable.Range(0, 3).Select(i => App.Mat.ProtocolCard(true, i)).ToArray();
            _rearrangePick = -1;
            foreach (var card in _rearrangeSlots)
                card.Interactive = true;

            _hud.ShowPrompt("Rearrange your Protocols\n<size=70%><color=#A397BA>Tap two Protocols to swap them. Cards in the lines stay put.</color></size>",
                ("Don't rearrange", ButtonStyle.Quiet, () => EndRearrange(confirm: false)),
                ("Confirm", ButtonStyle.Primary, () => EndRearrange(confirm: true)));
            _hud.PromptButton(1).Interactable = false;
            UpdateTargets();
            UpdatePrompt();
        }

        void OnProtocolClicked(bool local, int slot)
        {
            if (!_rearranging || !local)
                return;
            if (_rearrangePick < 0)
            {
                _rearrangePick = slot;
                _rearrangeSlots[slot].SetHighlight(true, Palette.Accent);
                return;
            }

            var other = _rearrangePick;
            _rearrangePick = -1;
            _rearrangeSlots[other].SetHighlight(false);
            if (other == slot)
                return;

            (_rearrangeSlots[other], _rearrangeSlots[slot]) = (_rearrangeSlots[slot], _rearrangeSlots[other]);
            (_rearrangeOrder[other], _rearrangeOrder[slot]) = (_rearrangeOrder[slot], _rearrangeOrder[other]);
            App.Mat.PreviewProtocolSlot(_rearrangeSlots[other], true, other);
            App.Mat.PreviewProtocolSlot(_rearrangeSlots[slot], true, slot);
            _hud.PromptButton(1).Interactable = !_rearrangeOrder.SequenceEqual(new[] { 0, 1, 2 });
        }

        void EndRearrange(bool confirm)
        {
            foreach (var card in _rearrangeSlots)
            {
                card.Interactive = false;
                card.SetHighlight(false);
            }
            _rearranging = false;
            _promptKey = null;
            _hud.HidePrompt();

            if (confirm && _sandbox.Rearrange(_rearrangeOrder))
                return;     // the sandbox re-renders with the new order

            // Put the cards back where they were.
            var original = Enumerable.Range(0, 3).Select(i => _rearrangeSlots[Array.IndexOf(_rearrangeOrder, i)]).ToArray();
            for (var i = 0; i < 3; i++)
                App.Mat.PreviewProtocolSlot(original[i], true, i);
            Render();
        }

        // ---------- Debug ----------

        void AddDebugActions()
        {
            _hud.AddDebugAction("Deal sample board", _sandbox.DealSampleBoard);
            _hud.AddDebugAction("Fill your left stack", () => _sandbox.FillStack(0, local: true, count: 7));
            _hud.AddDebugAction("Fill their middle stack", () => _sandbox.FillStack(1, local: false, count: 6));
            _hud.AddDebugAction("Opponent plays a card", _sandbox.OpponentPlays);
            _hud.AddDebugAction("Cycle Control", _sandbox.CycleControl);
            _hud.AddDebugAction("Compile your left line", () => _sandbox.Compile(0));
            _hud.AddDebugAction("Choose a card to flip", () =>
                _sandbox.RequestChoice("Choose 1 card to flip", optional: true, id =>
                {
                    if (id >= 0)
                        _sandbox.Flip(id);
                }));
            _hud.AddDebugAction("Rearrange Protocols", StartRearrange);
            _hud.AddDebugAction("Simulate win", () => Finish(App.Match.Start.LocalSeat));
            _hud.AddDebugAction("Simulate loss", () => Finish(1 - App.Match.Start.LocalSeat));
            if (App.Session is FakeSessionService fake)
                _hud.AddDebugAction("Opponent leaves", fake.SimulateOpponentLeft);
            _hud.AddDebugAction("Leave game", () => InMatch.Leave(App));
        }

        void Finish(int winnerSeat)
        {
            App.Match.WinnerSeat = winnerSeat;
            App.Nav.Go(new ResultScreen());
        }
    }
}
