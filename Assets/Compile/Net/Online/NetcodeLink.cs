using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;

namespace Compile.Net
{
    // A link to the other browser over Netcode for GameObjects' named messages: reliable, in order, and
    // split into pieces by the transport when long. Text goes as UTF-8. Nothing in the game is a networked
    // object, so no prefabs need registering.
    public sealed class NetcodeLink : IMatchLink
    {
        public const string Channel = "compile";

        // The largest message either side will send. A whole view is a few kilobytes.
        public const int MaxMessageBytes = 128 * 1024;

        readonly NetworkManager _network;
        readonly Queue<string> _inbox = new Queue<string>();

        public ulong Peer { get; }
        public event Action<string> Received;

        public NetcodeLink(NetworkManager network, ulong peer)
        {
            _network = network;
            Peer = peer;
        }

        public void Send(string message)
        {
            if (_network == null || !_network.IsListening)
                return;
            var bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length > MaxMessageBytes)
                throw new InvalidOperationException($"A {bytes.Length}-byte message is too long to send.");
            using var writer = new FastBufferWriter(bytes.Length + sizeof(int), Allocator.Temp);
            writer.WriteValueSafe(bytes.Length);
            writer.WriteBytesSafe(bytes);
            _network.CustomMessagingManager.SendNamedMessage(Channel, Peer, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }

        // From the channel's handler: kept until the next Pump.
        public void Deliver(string message) => _inbox.Enqueue(message);

        public void Pump()
        {
            while (_inbox.Count > 0)
                Received?.Invoke(_inbox.Dequeue());
        }

        // A named message's text, or null if it isn't one of ours.
        public static string Read(FastBufferReader reader)
        {
            if (!reader.TryBeginRead(sizeof(int)))
                return null;
            reader.ReadValue(out int length);
            if (length < 0 || length > MaxMessageBytes || !reader.TryBeginRead(length))
                return null;
            var bytes = new byte[length];
            reader.ReadBytes(ref bytes, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
