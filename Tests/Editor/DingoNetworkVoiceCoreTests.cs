#if UNITY_EDITOR

using System.Collections.Generic;
using DingoNetworkVoice;
using NUnit.Framework;

namespace DingoNetworkVoiceTests
{
    public class DingoNetworkVoiceCoreTests
    {
        [Test]
        public void Configuration_RejectsInvalidBudgets()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new VoiceSessionConfiguration(maxEncodedFrameBytes: 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new VoiceSessionConfiguration(maxCaptureFramesPerTick: 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new VoiceSessionConfiguration(maxIncomingFramesPerTick: 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new VoiceSessionConfiguration(incomingQueueCapacity: 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new VoiceSessionConfiguration(
                    maxIncomingFramesPerTick: 3,
                    incomingQueueCapacity: 2));
        }

        [Test]
        public void Lifecycle_OwnsProvidersAndUnsubscribesBorrowedTransport()
        {
            var context = new VoiceSessionTestContext();
            var states = new List<VoiceSessionState>();
            context.Session.StateChanged += states.Add;

            Assert.That(context.Session.StartCapture().Accepted, Is.False);
            Assert.That(
                context.Session.LastError.Code,
                Is.EqualTo(VoiceErrorCode.InvalidState));

            Assert.That(context.Session.Activate().Accepted, Is.True);
            Assert.That(context.Session.LastError, Is.Null);
            Assert.That(context.Transport.SubscriptionCount, Is.EqualTo(1));
            Assert.That(context.Session.StartCapture().Accepted, Is.True);
            Assert.That(
                context.Session.State,
                Is.EqualTo(VoiceSessionState.Capturing));

            Assert.That(context.Session.Deactivate().Accepted, Is.True);
            Assert.That(context.Capture.StopCount, Is.EqualTo(1));
            Assert.That(context.Transport.SubscriptionCount, Is.Zero);
            Assert.That(context.Playback.ClearCount, Is.EqualTo(1));
            Assert.That(
                context.Session.State,
                Is.EqualTo(VoiceSessionState.Inactive));

            Assert.That(context.Session.Activate().Accepted, Is.True);
            context.Session.Dispose();

            Assert.That(context.Capture.IsDisposed, Is.True);
            Assert.That(context.Decoder.IsDisposed, Is.True);
            Assert.That(context.Playback.IsDisposed, Is.True);
            Assert.That(context.Transport.SubscriptionCount, Is.Zero);
            Assert.That(
                context.Session.State,
                Is.EqualTo(VoiceSessionState.Disposed));
            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    VoiceSessionState.Listening,
                    VoiceSessionState.Capturing,
                    VoiceSessionState.Inactive,
                    VoiceSessionState.Listening,
                    VoiceSessionState.Disposed,
                }));
        }

        [Test]
        public void Capture_AssignsSequencesAndHonorsPerTickBudget()
        {
            using var context = new VoiceSessionTestContext(
                new VoiceSessionConfiguration(
                    maxCaptureFramesPerTick: 2));
            context.Capture.Enqueue(1);
            context.Capture.Enqueue(2);
            context.Capture.Enqueue(3);

            Assert.That(context.Session.Activate().Accepted, Is.True);
            Assert.That(context.Session.StartCapture().Accepted, Is.True);

            context.Session.Tick();
            Assert.That(context.Transport.SentPackets.Count, Is.EqualTo(2));
            Assert.That(context.Transport.SentPackets[0].Sequence, Is.EqualTo(1));
            Assert.That(context.Transport.SentPackets[1].Sequence, Is.EqualTo(2));

            context.Session.Tick();
            Assert.That(context.Transport.SentPackets.Count, Is.EqualTo(3));
            Assert.That(context.Transport.SentPackets[2].Sequence, Is.EqualTo(3));
        }

        [Test]
        public void IncomingQueue_DropsOldestAndHonorsProcessingBudget()
        {
            using var context = new VoiceSessionTestContext(
                new VoiceSessionConfiguration(
                    maxIncomingFramesPerTick: 1,
                    incomingQueueCapacity: 2));
            var peerId = new VoicePeerId("remote-one");
            Assert.That(context.Session.Activate().Accepted, Is.True);

            context.Transport.Emit(peerId, 1, 1);
            context.Transport.Emit(peerId, 2, 2);
            context.Transport.Emit(peerId, 3, 3);

            Assert.That(context.Session.QueuedIncomingFrames, Is.EqualTo(2));
            Assert.That(context.Session.DroppedIncomingFrames, Is.EqualTo(1));

            context.Session.Tick();
            Assert.That(context.Playback.SubmittedPeers.Count, Is.EqualTo(1));
            Assert.That(
                context.Playback.SubmittedFrames[0].Samples.Span[0],
                Is.EqualTo(2));
            Assert.That(context.Session.QueuedIncomingFrames, Is.EqualTo(1));

            context.Session.Tick();
            Assert.That(context.Playback.SubmittedPeers.Count, Is.EqualTo(2));
            Assert.That(
                context.Playback.SubmittedFrames[1].Samples.Span[0],
                Is.EqualTo(3));
            Assert.That(context.Session.QueuedIncomingFrames, Is.Zero);

            Assert.That(
                context.Session.TryGetPeerState(peerId, out var state),
                Is.True);
            Assert.That(state.ReceivedFrames, Is.EqualTo(2));
            Assert.That(state.DroppedFrames, Is.EqualTo(1));
        }

        [Test]
        public void PeerMute_SkipsDecodeAndDuplicateFramesStayDropped()
        {
            using var context = new VoiceSessionTestContext();
            var peerId = new VoicePeerId("remote-two");
            var removedPeers = new List<VoicePeerId>();
            context.Session.PeerRemoved += removedPeers.Add;
            Assert.That(context.Session.Activate().Accepted, Is.True);

            context.Transport.Emit(peerId, 1, 10);
            context.Session.Tick();
            Assert.That(context.Decoder.DecodeCount, Is.EqualTo(1));
            Assert.That(context.Playback.SubmittedPeers.Count, Is.EqualTo(1));

            context.Transport.Emit(peerId, 1, 11);
            context.Session.Tick();
            Assert.That(context.Decoder.DecodeCount, Is.EqualTo(1));

            Assert.That(
                context.Session.SetPeerMuted(peerId, true).Accepted,
                Is.True);
            Assert.That(context.Playback.RemovedPeers, Does.Contain(peerId));
            context.Transport.Emit(peerId, 2, 12);
            context.Session.Tick();
            Assert.That(context.Decoder.DecodeCount, Is.EqualTo(1));

            Assert.That(
                context.Session.SetPeerMuted(peerId, false).Accepted,
                Is.True);
            context.Transport.Emit(peerId, 3, 13);
            context.Session.Tick();
            Assert.That(context.Decoder.DecodeCount, Is.EqualTo(2));
            Assert.That(context.Playback.SubmittedPeers.Count, Is.EqualTo(2));

            Assert.That(
                context.Session.TryGetPeerState(peerId, out var state),
                Is.True);
            Assert.That(state.IsMuted, Is.False);
            Assert.That(state.ReceivedFrames, Is.EqualTo(3));
            Assert.That(state.DroppedFrames, Is.EqualTo(1));

            Assert.That(context.Session.RemovePeer(peerId), Is.True);
            Assert.That(
                context.Session.TryGetPeerState(peerId, out _),
                Is.False);
            Assert.That(removedPeers, Is.EqualTo(new[] { peerId }));
        }

        [Test]
        public void CaptureFailure_StopsCaptureButKeepsRemoteListeningActive()
        {
            using var context = new VoiceSessionTestContext();
            var expectedError = new VoiceError(
                VoiceErrorCode.CaptureFailure,
                "Microphone unavailable.");
            context.Capture.NextError = expectedError;

            Assert.That(context.Session.Activate().Accepted, Is.True);
            Assert.That(context.Session.StartCapture().Accepted, Is.True);
            context.Session.Tick();

            Assert.That(context.Capture.StopCount, Is.EqualTo(1));
            Assert.That(
                context.Session.State,
                Is.EqualTo(VoiceSessionState.Listening));
            Assert.That(context.Session.LastError, Is.SameAs(expectedError));

            var peerId = new VoicePeerId("remote-three");
            context.Transport.Emit(peerId, 1, 21);
            context.Session.Tick();
            Assert.That(context.Playback.SubmittedPeers, Is.EqualTo(new[] { peerId }));
        }
    }

    public class VoiceSessionTestContext : System.IDisposable
    {
        public readonly FakeVoiceCapture Capture = new();
        public readonly FakeVoiceTransport Transport = new();
        public readonly FakeVoiceDecoder Decoder = new();
        public readonly FakeVoicePlayback Playback = new();
        public readonly VoiceSession Session;

        public VoiceSessionTestContext(
            VoiceSessionConfiguration configuration = null)
        {
            Session = new VoiceSession(
                Capture,
                Transport,
                Decoder,
                Playback,
                configuration);
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    public class FakeVoiceCapture : IVoiceCapture
    {
        private readonly Queue<VoiceEncodedFrame> _frames = new();

        public VoiceCommandResult StartResult = VoiceCommandResult.Success();
        public VoiceCommandResult StopResult = VoiceCommandResult.Success();
        public VoiceError NextError;
        public int StartCount;
        public int StopCount;
        public bool IsDisposed;

        public void Enqueue(byte value)
        {
            _frames.Enqueue(new VoiceEncodedFrame(new[] { value }));
        }

        public VoiceCommandResult StartCapture()
        {
            StartCount++;
            return StartResult;
        }

        public VoiceCommandResult StopCapture()
        {
            StopCount++;
            return StopResult;
        }

        public bool TryCapture(
            out VoiceEncodedFrame frame,
            out VoiceError error)
        {
            if (NextError != null)
            {
                frame = null;
                error = NextError;
                NextError = null;
                return false;
            }

            error = null;
            if (_frames.Count == 0)
            {
                frame = null;
                return false;
            }

            frame = _frames.Dequeue();
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    public class FakeVoiceTransport : IVoiceTransport
    {
        private System.Action<VoiceIncomingPacket> _packetReceived;

        public readonly List<VoicePacket> SentPackets = new();
        public VoiceCommandResult BroadcastResult = VoiceCommandResult.Success();
        public int SubscriptionCount { get; private set; }

        public event System.Action<VoiceIncomingPacket> PacketReceived
        {
            add
            {
                _packetReceived += value;
                SubscriptionCount++;
            }
            remove
            {
                _packetReceived -= value;
                SubscriptionCount--;
            }
        }

        public VoiceCommandResult Broadcast(VoicePacket packet)
        {
            SentPackets.Add(packet);
            return BroadcastResult;
        }

        public void Emit(
            VoicePeerId senderId,
            uint sequence,
            byte value)
        {
            _packetReceived?.Invoke(new VoiceIncomingPacket(
                senderId,
                new VoicePacket(
                    sequence,
                    new VoiceEncodedFrame(new[] { value }))));
        }
    }

    public class FakeVoiceDecoder : IVoiceDecoder
    {
        public VoiceError NextError;
        public int DecodeCount;
        public bool IsDisposed;

        public bool TryDecode(
            VoiceEncodedFrame encodedFrame,
            out VoicePcmFrame pcmFrame,
            out VoiceError error)
        {
            DecodeCount++;
            if (NextError != null)
            {
                pcmFrame = null;
                error = NextError;
                NextError = null;
                return false;
            }

            error = null;
            pcmFrame = new VoicePcmFrame(
                new[] { (short)encodedFrame.Payload.Span[0] },
                48000,
                1);
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    public class FakeVoicePlayback : IVoicePlayback
    {
        public readonly List<VoicePeerId> SubmittedPeers = new();
        public readonly List<VoicePcmFrame> SubmittedFrames = new();
        public readonly List<VoicePeerId> RemovedPeers = new();
        public VoiceCommandResult SubmitResult = VoiceCommandResult.Success();
        public int ClearCount;
        public int TickCount;
        public bool IsDisposed;

        public VoiceCommandResult Submit(
            VoicePeerId peerId,
            VoicePcmFrame pcmFrame)
        {
            SubmittedPeers.Add(peerId);
            SubmittedFrames.Add(pcmFrame);
            return SubmitResult;
        }

        public void RemovePeer(VoicePeerId peerId)
        {
            RemovedPeers.Add(peerId);
        }

        public void Clear()
        {
            ClearCount++;
        }

        public void Tick()
        {
            TickCount++;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}

#endif
