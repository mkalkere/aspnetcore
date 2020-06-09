// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    internal enum KeepAliveState
    {
        None,
        SendPing,
        PingSent,
        Timeout
    }

    internal class Http2KeepAlive
    {
        // An empty ping payload
        internal static readonly ReadOnlySequence<byte> PingPayload = new ReadOnlySequence<byte>(new byte[8]);

        private readonly TimeSpan _keepAliveInterval;
        private readonly TimeSpan? _keepAliveTimeout;
        private readonly ISystemClock _systemClock;
        private long _lastFrameReceivedTimestamp;
        private long _pingSentTimestamp;

        // Internal for testing
        internal KeepAliveState _state;

        public Http2KeepAlive(TimeSpan keepAliveInterval, TimeSpan? keepAliveTimeout, ISystemClock systemClock)
        {
            _keepAliveInterval = keepAliveInterval;
            _keepAliveTimeout = keepAliveTimeout;
            _systemClock = systemClock;
        }

        public void PingSent()
        {
            _state = KeepAliveState.PingSent;

            // System clock only has 1 second of precision, so the clock could be up to 1 second in the past.
            // To err on the side of caution, add a second to the clock when calculating the ping sent time.
            _pingSentTimestamp = _systemClock.UtcNowTicks + TimeSpan.TicksPerSecond;
        }

        public KeepAliveState ProcessKeepAlive(bool frameReceived)
        {
            var timestamp = _systemClock.UtcNowTicks;

            if (frameReceived)
            {
                // System clock only has 1 second of precision, so the clock could be up to 1 second in the past.
                // To err on the side of caution, add a second to the clock when calculating the ping sent time.
                _lastFrameReceivedTimestamp = timestamp + TimeSpan.TicksPerSecond;

                // Any frame received after the keep alive interval is exceeded resets the state back to none.
                if (_state == KeepAliveState.PingSent)
                {
                    _pingSentTimestamp = 0;
                    _state = KeepAliveState.None;
                }
            }
            else
            {
                switch (_state)
                {
                    case KeepAliveState.None:
                        // Check whether keep alive interval has passed since last frame received
                        if (timestamp > (_lastFrameReceivedTimestamp + _keepAliveInterval.Ticks))
                        {
                            _state = KeepAliveState.SendPing;
                        }
                        break;
                    case KeepAliveState.PingSent:
                        if (_keepAliveTimeout != null)
                        {
                            if (timestamp > (_pingSentTimestamp + _keepAliveTimeout.GetValueOrDefault().Ticks))
                            {
                                _state = KeepAliveState.Timeout;
                            }
                        }
                        break;
                }
            }

            return _state;
        }
    }
}
