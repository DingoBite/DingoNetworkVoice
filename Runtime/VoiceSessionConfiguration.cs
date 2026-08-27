using System;

namespace DingoNetworkVoice
{
    public class VoiceSessionConfiguration
    {
        public const int DEFAULT_MAX_ENCODED_FRAME_BYTES = 32 * 1024;
        public const int DEFAULT_MAX_CAPTURE_FRAMES_PER_TICK = 4;
        public const int DEFAULT_MAX_INCOMING_FRAMES_PER_TICK = 16;
        public const int DEFAULT_INCOMING_QUEUE_CAPACITY = 64;

        public readonly int MaxEncodedFrameBytes;
        public readonly int MaxCaptureFramesPerTick;
        public readonly int MaxIncomingFramesPerTick;
        public readonly int IncomingQueueCapacity;

        public VoiceSessionConfiguration(
            int maxEncodedFrameBytes = DEFAULT_MAX_ENCODED_FRAME_BYTES,
            int maxCaptureFramesPerTick = DEFAULT_MAX_CAPTURE_FRAMES_PER_TICK,
            int maxIncomingFramesPerTick =
                DEFAULT_MAX_INCOMING_FRAMES_PER_TICK,
            int incomingQueueCapacity = DEFAULT_INCOMING_QUEUE_CAPACITY)
        {
            if (maxEncodedFrameBytes < 1 || maxEncodedFrameBytes > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxEncodedFrameBytes),
                    "Encoded voice frame limit must be between 1 byte and 1 MB.");
            }

            if (maxCaptureFramesPerTick < 1
                || maxCaptureFramesPerTick > 256)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxCaptureFramesPerTick),
                    "Capture frame budget must be between 1 and 256.");
            }

            if (maxIncomingFramesPerTick < 1
                || maxIncomingFramesPerTick > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxIncomingFramesPerTick),
                    "Incoming frame budget must be between 1 and 1024.");
            }

            if (incomingQueueCapacity < 1 || incomingQueueCapacity > 4096)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(incomingQueueCapacity),
                    "Incoming queue capacity must be between 1 and 4096.");
            }

            if (maxIncomingFramesPerTick > incomingQueueCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxIncomingFramesPerTick),
                    "Incoming frame budget cannot exceed queue capacity.");
            }

            MaxEncodedFrameBytes = maxEncodedFrameBytes;
            MaxCaptureFramesPerTick = maxCaptureFramesPerTick;
            MaxIncomingFramesPerTick = maxIncomingFramesPerTick;
            IncomingQueueCapacity = incomingQueueCapacity;
        }
    }
}
