namespace NLayer.Decoder
{
    public struct VBRInfo
    {
        public int SampleCount;
        public int SampleRate;
        public int Channels;
        public int VBRFrames;
        public int VBRBytes;
        public int VBRQuality;
        public int VBRDelay;

        // we assume the entire stream is consistent wrt samples per frame
        public readonly long VBRStreamSampleCount => VBRFrames * SampleCount;

        public readonly int VBRAverageBitrate =>
            (int)(VBRBytes / (VBRStreamSampleCount / (double)SampleRate) * 8);
    }
}
