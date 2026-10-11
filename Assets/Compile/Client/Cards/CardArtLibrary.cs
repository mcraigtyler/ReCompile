using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Compile.Client.Cards
{
    // Sprites for every Protocol and card, keyed the same way as the card data. Built by
    // Compile.Editor.ClientSetup from Assets/Compile/Art/Cards.
    [CreateAssetMenu(menuName = "Compile/Card Art Library")]
    public sealed class CardArtLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class CardArt
        {
            public int value;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class ProtocolArt
        {
            public string protocol;
            public Sprite loading;     // "A" side
            public Sprite compiled;    // "B" side
            public List<CardArt> cards = new List<CardArt>();
        }

        public Sprite missing;
        public List<ProtocolArt> protocols = new List<ProtocolArt>();

        public ProtocolArt Get(string protocol) => protocols.FirstOrDefault(p => p.protocol == protocol);

        public Sprite ProtocolSide(string protocol, bool compiled)
        {
            var art = Get(protocol);
            var sprite = art == null ? null : compiled ? art.compiled : art.loading;
            return sprite != null ? sprite : missing;
        }

        public Sprite Card(string protocol, int value)
        {
            var sprite = Get(protocol)?.cards.FirstOrDefault(c => c.value == value)?.sprite;
            return sprite != null ? sprite : missing;
        }
    }
}
