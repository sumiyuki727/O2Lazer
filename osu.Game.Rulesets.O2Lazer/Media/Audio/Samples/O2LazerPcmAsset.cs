using System;
using System.Threading;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

internal readonly record struct O2LazerPcmChunk(long StartFrame, int FrameCount, float[] Samples)
{
    internal long EndFrame => StartFrame + FrameCount;
}

internal enum O2LazerPcmAssetState
{
    Preparing,
    Ready,
    Complete,
    Failed,
    Disposed,
}

internal sealed class O2LazerPcmAsset
{
    private const int chunks_per_page = 64;

    private O2LazerPcmChunk[]?[] chunkPages = [];
    private int publishedChunkCount;
    private long publishedFrameCount;
    private long totalFrameCount = -1;
    private long residentBytes;
    private long originalDurationBits = BitConverter.DoubleToInt64Bits(double.NaN);
    private int state = (int)O2LazerPcmAssetState.Preparing;

    internal long TotalFrameCount => Interlocked.Read(ref totalFrameCount);

    internal long PublishedFrameCount => Interlocked.Read(ref publishedFrameCount);

    internal int SampleRate { get; }

    internal int Channels { get; }

    internal O2LazerPcmAssetState State => (O2LazerPcmAssetState)Volatile.Read(ref state);

    internal bool IsComplete => State == O2LazerPcmAssetState.Complete;

    internal long ResidentBytes => Interlocked.Read(ref residentBytes);

    internal double? OriginalDurationMilliseconds
    {
        get
        {
            var value = BitConverter.Int64BitsToDouble(Interlocked.Read(ref originalDurationBits));
            return double.IsFinite(value) && value >= 0 ? value : null;
        }
    }

    internal O2LazerPcmAsset(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        SampleRate = sampleRate;
        Channels = channels;
    }

    internal void Publish(O2LazerPcmChunk chunk)
    {
        if (State is O2LazerPcmAssetState.Complete or O2LazerPcmAssetState.Failed or O2LazerPcmAssetState.Disposed)
            throw new InvalidOperationException("PCM chunks cannot be published after processing has ended.");

        var expectedStart = Interlocked.Read(ref publishedFrameCount);
        if (chunk.StartFrame != expectedStart || chunk.FrameCount <= 0 || chunk.Samples.Length != chunk.FrameCount * Channels)
            throw new ArgumentException(@"PCM chunks must be contiguous and contain complete interleaved frames.", nameof(chunk));

        var chunkIndex = Volatile.Read(ref publishedChunkCount);
        var pageIndex = chunkIndex / chunks_per_page;
        var pageOffset = chunkIndex % chunks_per_page;
        var pages = chunkPages;

        if (pageIndex >= pages.Length)
        {
            var expanded = new O2LazerPcmChunk[]?[Math.Max(pageIndex + 1, Math.Max(1, pages.Length * 2))];
            Array.Copy(pages, expanded, pages.Length);
            pages = expanded;
            Volatile.Write(ref chunkPages, pages);
        }

        var page = pages[pageIndex] ??= new O2LazerPcmChunk[chunks_per_page];
        page[pageOffset] = chunk;

        Interlocked.Add(ref residentBytes, (long)chunk.Samples.Length * sizeof(float));
        Interlocked.Exchange(ref publishedFrameCount, chunk.EndFrame);
        Volatile.Write(ref publishedChunkCount, chunkIndex + 1);
    }

    internal void MarkReady()
    {
        if (State == O2LazerPcmAssetState.Preparing && PublishedFrameCount > 0)
            Volatile.Write(ref state, (int)O2LazerPcmAssetState.Ready);
    }

    internal void Complete(long frameCount)
    {
        if (frameCount < 0 || frameCount != PublishedFrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameCount), @"The completed length must match all published PCM chunks.");

        Interlocked.Exchange(ref totalFrameCount, frameCount);
        Volatile.Write(ref state, (int)O2LazerPcmAssetState.Complete);
    }

    internal void SetOriginalDuration(double? durationMilliseconds)
    {
        if (durationMilliseconds is not { } duration || !double.IsFinite(duration) || duration < 0)
            return;

        Interlocked.Exchange(ref originalDurationBits, BitConverter.DoubleToInt64Bits(duration));
    }

    internal void Fail()
    {
        if (State != O2LazerPcmAssetState.Disposed)
            Volatile.Write(ref state, (int)O2LazerPcmAssetState.Failed);
    }

    internal void DisposePublishedChunks()
    {
        Volatile.Write(ref state, (int)O2LazerPcmAssetState.Disposed);
        Volatile.Write(ref publishedChunkCount, 0);
        Interlocked.Exchange(ref publishedFrameCount, 0);
        Interlocked.Exchange(ref residentBytes, 0);
        Volatile.Write(ref chunkPages, []);
    }

    internal bool TryReadStereoFrame(long frame, out float left, out float right)
    {
        if (frame < 0 || frame >= PublishedFrameCount)
        {
            left = right = 0;
            return false;
        }

        var low = 0;
        var high = Volatile.Read(ref publishedChunkCount) - 1;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var chunk = getPublishedChunk(middle);

            if (frame < chunk.StartFrame)
            {
                high = middle - 1;
                continue;
            }

            if (frame >= chunk.EndFrame)
            {
                low = middle + 1;
                continue;
            }

            var index = checked((int)((frame - chunk.StartFrame) * Channels));
            left = chunk.Samples[index];
            right = Channels == 1 ? left : chunk.Samples[index + 1];
            return true;
        }

        left = right = 0;
        return false;
    }

    private O2LazerPcmChunk getPublishedChunk(int index)
    {
        var pages = Volatile.Read(ref chunkPages);
        return pages[index / chunks_per_page]![index % chunks_per_page];
    }
}
