using System.Collections.Generic;
using Compile.Client.Cards;
using Compile.Client.UI;
using UnityEngine;

namespace Compile.Client.Table
{
    // The play mat and everything lying on it. Cards live on a world-space canvas the size of the mat,
    // so canvas pixels are mat pixels (see MatLayout.ToCanvas).
    public sealed class MatView : MonoBehaviour
    {
        readonly CardView[,] _protocols = new CardView[2, 3];
        readonly UnityEngine.UI.Image[,] _slotHighlights = new UnityEngine.UI.Image[2, 3];
        CardArtLibrary _art;
        float _pulse;

        public RectTransform Cards { get; private set; }

        public void Init(Theme theme, CardArtLibrary art, Camera camera)
        {
            _art = art;

            var mat = new GameObject("PlayMat", typeof(SpriteRenderer));
            mat.transform.SetParent(transform, false);
            var renderer = mat.GetComponent<SpriteRenderer>();
            renderer.sprite = theme.playMat;
            renderer.sharedMaterial = theme.spriteUnlitMaterial;
            renderer.sortingOrder = -100;

            var canvas = Ui.WorldCanvas("MatCards", transform, camera, sortingOrder: 10);
            Cards = (RectTransform)canvas.transform;
            Cards.sizeDelta = MatLayout.SizePx;
            Cards.localPosition = new Vector3(0f, 0f, -0.01f);

            var slots = Ui.Rect("SlotHighlights", Cards);
            for (var seat = 0; seat < 2; seat++)
            for (var i = 0; i < 3; i++)
            {
                var local = seat == 0;
                var rt = Ui.Rect($"Slot{(local ? "Local" : "Opponent")}{i}", slots);
                rt.sizeDelta = MatLayout.ProtocolSlotSizePx;
                rt.anchoredPosition = MatLayout.ProtocolSlot(local, i);
                var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.sprite = theme.chamferFrame;
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.color = local ? Palette.Accent : Palette.Cyan;
                image.raycastTarget = false;
                image.enabled = false;
                _slotHighlights[seat, i] = image;
            }
        }

        static int Seat(bool local) => local ? 0 : 1;

        // Moves a card (for example, one picked in the draft) into a Protocol slot.
        public void PlaceProtocol(CardView card, bool local, int index, bool animate = true)
        {
            var existing = _protocols[Seat(local), index];
            if (existing != null && existing != card)
                Destroy(existing.gameObject);

            card.Interactive = false;
            card.SetHighlight(false);
            card.transform.SetParent(Cards, true);
            _protocols[Seat(local), index] = card;

            var position = MatLayout.ProtocolSlot(local, index);
            var rotation = MatLayout.ProtocolRotation(local);
            if (animate)
                card.MoveTo(position, rotation, MatLayout.ProtocolScale, 0.55f);
            else
                card.Place(position, rotation, MatLayout.ProtocolScale);
        }

        // Makes the Protocol slots match a player's Protocols (in that player's own left → right order):
        // creates missing cards, moves cards whose Protocol changed slot, and flips Compiled sides.
        public void ShowProtocols(bool local, IReadOnlyList<string> names, IReadOnlyList<bool> compiled, bool animate = true)
        {
            var seat = Seat(local);
            var existing = new List<CardView>();
            for (var i = 0; i < 3; i++)
                if (_protocols[seat, i] != null)
                    existing.Add(_protocols[seat, i]);

            for (var i = 0; i < 3; i++)
            {
                var card = existing.Find(c => c.Protocol == names[i]);
                if (card == null)
                {
                    card = CardView.Create(Cards, _art.ProtocolSide(names[i], compiled[i]), names[i]);
                    card.Protocol = names[i];
                    card.Compiled = compiled[i];
                    _protocols[seat, i] = card;
                    PlaceProtocol(card, local, i, animate: false);
                    continue;
                }

                existing.Remove(card);
                _protocols[seat, i] = card;
                if (card.Compiled != compiled[i])
                {
                    card.Compiled = compiled[i];
                    card.Flip(_art.ProtocolSide(card.Protocol, compiled[i]));
                }

                var position = MatLayout.ProtocolSlot(local, i);
                if (Vector2.Distance(card.Rect.anchoredPosition, position) > 1f && !card.IsMoving)
                    card.MoveTo(position, MatLayout.ProtocolRotation(local), MatLayout.ProtocolScale, animate ? 0.45f : 0.01f);
            }

            foreach (var leftover in existing)
                Destroy(leftover.gameObject);
        }

        public CardView ProtocolCard(bool local, int index) => _protocols[Seat(local), index];

        // Forgets a card placed in a slot without destroying it (e.g. a draft pick that was undone).
        public void ReleaseProtocol(CardView card)
        {
            for (var seat = 0; seat < 2; seat++)
            for (var i = 0; i < 3; i++)
                if (_protocols[seat, i] == card)
                    _protocols[seat, i] = null;
        }

        // Temporarily shows a Protocol card in another slot (used while rearranging, before it's confirmed).
        public void PreviewProtocolSlot(CardView card, bool local, int index) =>
            card.MoveTo(MatLayout.ProtocolSlot(local, index), MatLayout.ProtocolRotation(local), MatLayout.ProtocolScale, 0.3f);

        public IReadOnlyList<string> ProtocolsFor(bool local)
        {
            var list = new List<string>();
            for (var i = 0; i < 3; i++)
                if (_protocols[Seat(local), i] != null)
                    list.Add(_protocols[Seat(local), i].Protocol);
            return list;
        }

        public void SetCompiled(bool local, int index, bool compiled)
        {
            var card = _protocols[Seat(local), index];
            if (card != null && card.Compiled != compiled)
            {
                card.Compiled = compiled;
                card.Flip(_art.ProtocolSide(card.Protocol, compiled));
            }
        }

        public void ClearProtocols()
        {
            for (var seat = 0; seat < 2; seat++)
            for (var i = 0; i < 3; i++)
            {
                if (_protocols[seat, i] != null)
                    Destroy(_protocols[seat, i].gameObject);
                _protocols[seat, i] = null;
            }
            HighlightSlots(false, -1);
        }

        // Pulses the given player's slot to show where the next pick lands. index -1 clears all highlights.
        public void HighlightSlots(bool local, int index)
        {
            for (var seat = 0; seat < 2; seat++)
            for (var i = 0; i < 3; i++)
                _slotHighlights[seat, i].enabled = index >= 0 && seat == Seat(local) && i == index;
        }

        void Update()
        {
            _pulse += Time.unscaledDeltaTime;
            var alpha = 0.45f + 0.4f * (0.5f + 0.5f * Mathf.Sin(_pulse * 4f));
            foreach (var image in _slotHighlights)
            {
                if (!image.enabled)
                    continue;
                var c = image.color;
                image.color = new Color(c.r, c.g, c.b, alpha);
            }
        }
    }
}
