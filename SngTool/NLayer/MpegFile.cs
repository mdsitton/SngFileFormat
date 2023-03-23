using System;
using System.Diagnostics;
using System.IO;

namespace NLayer
{
    public class MpegFile : IDisposable
    {
        private Stream _stream;
        private bool _leaveOpen, _eofFound;
        private Decoder.MpegStreamReader _reader;
        private MpegFrameDecoder _decoder;
        private long _position;

        private float[] _readBuf = new float[1152 * 2];
        private int _readBufLen;
        private int _readBufOfs;

        /// <summary>
        /// Construct Mpeg file representation from filename.
        /// </summary>
        /// <param name="fileName">The file which contains Mpeg data.</param>
        public MpegFile(string fileName) :
            this(File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read), false)
        {
        }

        /// <summary>
        /// Construct Mpeg file representation from stream.
        /// </summary>
        /// <param name="stream">The input stream which contains Mpeg data.</param>
        public MpegFile(Stream stream, bool leaveOpen)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;

            _reader = new Decoder.MpegStreamReader(_stream);
            _decoder = new MpegFrameDecoder();
        }

        /// <summary>
        /// Stereo mode used in decoding.
        /// </summary>
        public StereoMode StereoMode
        {
            get => _decoder.StereoMode;
            set => _decoder.StereoMode = value;
        }

        /// <summary>
        /// Sample rate of source Mpeg, in Hertz.
        /// </summary>
        public int SampleRate => _reader.SampleRate;

        /// <summary>
        /// Channel count of source Mpeg.
        /// </summary>
        public int Channels => _reader.Channels;

        /// <summary>
        /// Whether the Mpeg stream supports seek operation.
        /// </summary>
        public bool CanSeek => _reader.CanSeek;

        /// <summary>
        /// Whether the Mpeg stream has ended.
        /// </summary>
        public bool EndOfFile => _eofFound;

        /// <summary>
        /// Data length of decoded data in samples.
        /// </summary>
        public long? Length
        {
            get
            {
                long? sampleCount = _reader.SampleCount;
                if (sampleCount.HasValue)
                    return sampleCount.GetValueOrDefault() * _reader.Channels;
                return default;
            }
        }

        /// <summary>
        /// Media duration of the Mpeg file.
        /// </summary>
        public TimeSpan? Duration
        {
            get
            {
                long? sampleCount = _reader.SampleCount;
                if (sampleCount.HasValue)
                    return TimeSpan.FromSeconds((double)sampleCount.GetValueOrDefault() / _reader.SampleRate);
                return default;
            }
        }

        /// <summary>
        /// Current decode position, represented by time. Calling the setter will result in a seeking operation.
        /// </summary>
        public TimeSpan Time
        {
            get => TimeSpan.FromSeconds((double)_position / _reader.Channels / _reader.SampleRate);
            set => Position = (long)(value.TotalSeconds * _reader.SampleRate * _reader.Channels);
        }

        /// <summary>
        /// Current decode position, in number of sample.
        /// Calling the setter will result in a seeking operation.
        /// </summary>
        public long Position
        {
            get => _position;
            set
            {
                if (!_reader.CanSeek)
                    throw new InvalidOperationException("The stream is not seekable.");
                if (value < 0L)
                    throw new ArgumentOutOfRangeException(nameof(value));

                // we're thinking in pcmStep interleaved samples...  adjust accordingly
                long samples = value / _reader.Channels;
                int sampleOffset = 0;

                // seek to the frame preceding the one we want (unless we're seeking to the first frame)
                if (samples >= _reader.FirstFrameSampleCount)
                {
                    sampleOffset = _reader.FirstFrameSampleCount;
                    samples -= sampleOffset;
                }

                // seek the stream
                long newPos = _reader.SeekTo(samples);
                if (newPos == -1)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _decoder.Reset();

                // if we have a sample offset, decode the next frame
                if (sampleOffset != 0)
                {
                    // throw away a frame (but allow the decoder to resync)
                    Decoder.MpegFrame? frame = _reader.NextFrame();
                    _decoder.DecodeFrame(frame!, _readBuf);
                    newPos += sampleOffset;
                }

                _position = newPos * _reader.Channels;
                _eofFound = false;

                // clear the decoder & buffer
                _readBufOfs = 0;
                _readBufLen = 0;
            }
        }

        /// <summary>
        /// Set the equalizer.
        /// </summary>
        /// <param name="eq">The equalizer, represented by an array of 32 adjustments in dB.</param>
        public void SetEQ(float[]? eq)
        {
            _decoder.SetEQ(eq);
        }

        /// <summary>
        /// Read specified samples into provided buffer, as PCM format.
        /// Result varies with diffirent <see cref="StereoMode"/>:
        /// <list type="bullet">
        /// <item>
        /// <description>For <see cref="StereoMode.Both"/>, sample data on both two channels will occur in turn (left first).</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="StereoMode.LeftOnly"/> and <see cref="StereoMode.RightOnly"/>, only data on
        /// specified channel will occur.</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="StereoMode.DownmixToMono"/>, two channels will be down-mixed into single channel.</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="destination">The buffer to fill with PCM samples.</param>
        /// <returns>The actual amount of samples read.</returns>
        public int ReadSamples(Span<float> destination)
        {
            int samplesRead = 0;
            var readBuffer = _readBuf.AsSpan();

            while (destination.Length > 0)
            {
                if (_readBufLen > _readBufOfs)
                {
                    // we have bytes in the buffer, so copy them first
                    int bufferedCount = _readBufLen - _readBufOfs;
                    if (bufferedCount > destination.Length)
                        bufferedCount = destination.Length;

                    readBuffer.Slice(_readBufOfs, bufferedCount).CopyTo(destination);
                    destination = destination.Slice(bufferedCount);

                    // now update our counters...
                    samplesRead += bufferedCount;

                    _position += bufferedCount;
                    _readBufOfs += bufferedCount;

                    // finally, mark the buffer as empty if we've read everything in it
                    if (_readBufOfs == _readBufLen)
                        _readBufLen = 0;
                }

                // if the buffer is empty, try to fill it
                //  NB: If we've already satisfied the read request, we'll still try to fill the buffer.
                //      This ensures there's data in the pipe on the next call
                if (_readBufLen == 0)
                {
                    if (_eofFound)
                        break;

                    // decode the next frame (update _readBuf)
                    Decoder.MpegFrame? frame = _reader.NextFrame();
                    if (frame == null)
                    {
                        _eofFound = true;
                        break;
                    }

                    try
                    {
                        _readBufLen = _decoder.DecodeFrame(frame, readBuffer);
                        _readBufOfs = 0;
                    }
                    catch (InvalidDataException)
                    {
                        // bad frame...  try again...
                        _decoder.Reset();

                        _readBufOfs = 0;
                        _readBufLen = 0;
                        continue;
                    }
                    catch (EndOfStreamException)
                    {
                        // no more frames
                        _eofFound = true;
                        break;
                    }
                    finally
                    {
                        frame.ClearBuffer();
                    }
                }
            }

            return samplesRead;
        }

        /// <summary>
        /// Disposes underlying resources.
        /// </summary>
        public void Dispose()
        {
            if (!_leaveOpen)
            {
                _stream.Dispose();
                _leaveOpen = false;
            }
        }
    }
}
