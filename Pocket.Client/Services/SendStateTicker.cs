using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;

namespace Pocket.Client.Services
{
    public static class SendTiming
    {
        /// <summary>Still not on the server after this long → yellow hourglass.</summary>
        public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(4);

        /// <summary>A direct message still not on the server after this long is given up: red, manual retry only.</summary>
        public static readonly TimeSpan MessageGiveUpAfter = TimeSpan.FromHours(24);
    }

    /// <summary>An outgoing item whose look changes with time while it waits for the server (e.g. the slow-send hourglass).</summary>
    public interface ISendStateTicking
    {
        /// <summary>True while a later tick could still change how the item looks.</summary>
        bool NeedsSendStateTick { get; }

        /// <summary>Re-evaluates the time-based state and raises change notifications for what changed.</summary>
        void RefreshSendState();
    }

    /// <summary>
    /// One UI-thread timer per page: ticks every second while any listed item is still waiting,
    /// and stops by itself once none is. Call <see cref="Ensure"/> whenever an item may have started waiting.
    /// </summary>
    public sealed class SendStateTicker
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
        private readonly Func<IEnumerable<ISendStateTicking>> _items;
        private bool _running;

        public SendStateTicker(Func<IEnumerable<ISendStateTicking>> items) => _items = items;

        public void Ensure()
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_running || !_items().Any(i => i.NeedsSendStateTick))
                    return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    return;

                _running = true;
                dispatcher.StartTimer(Interval, Tick);
            });
        }

        private bool Tick()
        {
            bool keepTicking = false;
            foreach (var item in _items().ToList())
            {
                item.RefreshSendState();
                keepTicking |= item.NeedsSendStateTick;
            }

            _running = keepTicking;
            return keepTicking;
        }
    }
}
