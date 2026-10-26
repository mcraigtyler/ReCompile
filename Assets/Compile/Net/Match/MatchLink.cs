using System;
using System.Collections.Generic;

namespace Compile.Net
{
    // One end of a reliable, ordered connection to the other player (or, for the host's own screen, to the
    // host). Received messages arrive from Pump, on the main thread, never during Send.
    public interface IMatchLink
    {
        void Send(string message);
        event Action<string> Received;

        // Delivers what has arrived since the last call.
        void Pump();
    }

    // Two ends joined in memory: the host's own screen talks to the host this way, and tests connect a host
    // and a guest without a network. Messages wait until the receiving end pumps, as they would over a network.
    public sealed class LoopbackLink : IMatchLink
    {
        readonly Queue<string> _inbox = new Queue<string>();
        LoopbackLink _other;

        public event Action<string> Received;

        // Every message sent each way, for tests that look at what crossed.
        public List<string> Sent { get; } = new List<string>();

        public static (LoopbackLink a, LoopbackLink b) Pair()
        {
            var a = new LoopbackLink();
            var b = new LoopbackLink();
            a._other = b;
            b._other = a;
            return (a, b);
        }

        public void Send(string message)
        {
            Sent.Add(message);
            _other._inbox.Enqueue(message);
        }

        public void Pump()
        {
            while (_inbox.Count > 0)
                Received?.Invoke(_inbox.Dequeue());
        }
    }
}
