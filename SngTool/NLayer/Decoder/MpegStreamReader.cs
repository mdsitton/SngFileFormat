using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace NLayer.Decoder
{
    public class MpegStreamReader
    {
        private ID3Frame? _id3Frame, _id3v1Frame;
        private RiffHeaderFrame? _riffHeaderFrame;
        private VBRInfo _vbrInfo;
        private bool _hasVbrInfo;
        private MpegFrame? _first, _current, _last, _lastFree;
        private int _readOffset, _fileLength;
        private bool _endFound, _mixedFrameSize;
        private byte[] songData;

        public bool CanSeek => true;

        public MpegStreamReader(Stream source)
        {

            // Read all bytes from the stream
            songData = new byte[source.Length];
            source.Read(songData, 0, songData.Length);

            _readOffset = 0;
            _fileLength = songData.Length;

            // find the first Mpeg frame
            var frame = FindNextFrame();
            while (frame != null && !(frame is MpegFrame))
                frame = FindNextFrame();

            // if we still don't have a frame, we never sync'ed
            if (frame == null)
                throw new InvalidDataException("Not a valid MPEG file!");

            // the very next frame "should be" an mpeg frame
            frame = FindNextFrame();

            // if not, it's not a valid file
            if (frame == null || !(frame is MpegFrame))
                throw new InvalidDataException("Not a valid MPEG file!");

            // seek to the first frame
            _current = _first;
        }

        private int RemainingCount => _fileLength - _readOffset;

        private FrameBase? FindNextFrame()
        {
            // if we've found the end, don't bother looking for anything else
            if (_endFound)
                return null;

            var freeFrame = _lastFree;
            var lastFrameStart = _readOffset;

            try
            {
                // loop until a frame is found
                while (RemainingCount >= 4)
                {
                    Span<byte> syncBuf = songData.AsSpan(_readOffset, 4);
                    var sync = (uint)(syncBuf[0] << 24 | syncBuf[1] << 16 | syncBuf[2] << 8 | syncBuf[3]);

                    lastFrameStart = _readOffset;

                    // try ID3 first (for v2 frames)
                    if (_id3Frame == null)
                    {
                        var f = ID3Frame.TrySync(sync);
                        if (f != null)
                        {
                            if (f.ValidateFrameHeader(_readOffset, this))
                            {
                                if (!CanSeek)
                                    f.SaveBuffer();

                                _readOffset += f.Length;

                                _id3Frame = f;
                                return _id3Frame;
                            }
                        }
                    }

                    // now look for a RIFF header
                    if (_first == null && _riffHeaderFrame == null)
                    {
                        var riffFrame = RiffHeaderFrame.TrySync(sync);
                        if (riffFrame != null)
                        {
                            if (riffFrame.ValidateFrameHeader(_readOffset, this))
                            {
                                _readOffset += riffFrame.Length;
                                return _riffHeaderFrame = riffFrame;
                            }
                        }
                    }

                    // finally, just try for an MPEG frame
                    var frame = MpegFrame.TrySync(sync);
                    if (frame != null)
                    {
                        if (frame.ValidateFrameHeader(_readOffset, this) &&
                            !(freeFrame != null && (
                                frame.Layer != freeFrame.Layer ||
                                frame.Version != freeFrame.Version ||
                                frame.SampleRate != freeFrame.SampleRate ||
                                frame.BitRateIndex > 0)))
                        {
                            if (!CanSeek)
                            {
                                frame.SaveBuffer();
                            }

                            _readOffset += frame.FrameLength;

                            if (_first == null)
                            {
                                if (!_hasVbrInfo && (_hasVbrInfo = frame.ParseVBR(out _vbrInfo)))
                                {
                                    return FindNextFrame();
                                }
                                else
                                {
                                    frame.Number = 0;
                                    _first = _last = frame;
                                }
                            }
                            else
                            {
                                if (frame.SampleCount != _first.SampleCount)
                                    _mixedFrameSize = true;

                                Debug.Assert(_last != null);

                                frame.SampleOffset = _last.SampleCount + _last.SampleOffset;
                                frame.Number = _last.Number + 1;
                                _last = (_last.Next = frame);
                            }

                            if (frame.BitRateIndex == 0)
                            {
                                _lastFree = frame;
                            }

                            return frame;
                        }
                    }

                    // if we've read MPEG frames and can't figure out what frame type we have,
                    // try looking for a new ID3 tag
                    if (_last != null)
                    {
                        var id3Frame = ID3Frame.TrySync(sync);
                        if (id3Frame != null)
                        {
                            if (id3Frame.ValidateFrameHeader(_readOffset, this))
                            {
                                if (!CanSeek)
                                    id3Frame.SaveBuffer();

                                // if it's a v1 tag, go ahead and parse it
                                if (id3Frame.Version == ID3FrameType.ID3v1 || id3Frame.Version == ID3FrameType.ID3v1Enh)
                                {
                                    _id3v1Frame = id3Frame;
                                }
                                else
                                {
                                    // grrr...  the ID3 2.4 spec says tags can be anywhere in the file
                                    // and that later tags can override earlier ones...  boo
                                    Debug.Assert(_id3Frame != null);
                                    _id3Frame.Merge(id3Frame);
                                }

                                _readOffset += id3Frame.Length;
                                return id3Frame;
                            }
                        }
                    }

                    // we didn't find any valid frame, increment the read offset by 1 and try again
                    _readOffset++;
                }

                // move the "end of frame" marker for the last free format frame (in case we have one)
                // this is because we don't include the last four bytes otherwise
                lastFrameStart += 4;

                _endFound = true;
                return null;
            }
            finally
            {
                if (freeFrame != null)
                {
                    freeFrame.Length = (int)(lastFrameStart - freeFrame.Offset);

                    if (!CanSeek)
                    {
                        // gotta finish filling the buffer!!
                        throw new InvalidOperationException(
                            "Free frames cannot be read properly from non-seekable streams.");
                    }

                    // if _lastFree hasn't changed (we got a non-MPEG frame), clear it out
                    if (_lastFree == freeFrame)
                    {
                        _lastFree = null;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(long offset, Span<byte> destination)
        {
            var length = Math.Min(destination.Length, songData.Length - (int)offset);
            songData.AsSpan((int)offset, length).CopyTo(destination);
            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte(long offset)
        {
            Debug.Assert(offset >= 0);
            return songData[offset];
        }

        public void ReadToEnd()
        {
            while (!_endFound)
                FindNextFrame();
        }

        public long? SampleCount
        {
            get
            {
                if (_hasVbrInfo)
                    return _vbrInfo.VBRStreamSampleCount;

                if (!CanSeek)
                    return null;

                ReadToEnd();
                Debug.Assert(_last != null);
                return _last.SampleCount + _last.SampleOffset;
            }
        }

        public int SampleRate
        {
            get
            {
                if (_hasVbrInfo)
                    return _vbrInfo.SampleRate;
                Debug.Assert(_first != null);
                return _first.SampleRate;
            }
        }

        public int Channels
        {
            get
            {
                if (_hasVbrInfo)
                    return _vbrInfo.Channels;
                Debug.Assert(_first != null);
                return _first.Channels;
            }
        }

        public int FirstFrameSampleCount => _first != null ? _first.SampleCount : 0;

        public long SeekTo(long sampleIndex)
        {
            if (!CanSeek)
                throw new InvalidOperationException("The stream is not seekable.");

            Debug.Assert(_first != null);

            // first try to "seek" by calculating the frame number
            var cnt = (int)(sampleIndex / _first.SampleCount);
            var frame = _first;
            if (_current != null && _current.Number <= cnt && _current.SampleOffset <= sampleIndex)
            {
                // if this fires, we can short-circuit things a bit...
                frame = _current;
                cnt -= frame.Number;
            }
            while (!_mixedFrameSize && --cnt >= 0 && frame != null)
            {
                // make sure we have more frames to look at
                if (frame == _last && !_endFound)
                {
                    do
                    {
                        FindNextFrame();
                    } while (frame == _last && !_endFound);
                }

                // if we've found a different frame size, fall through...
                if (_mixedFrameSize)
                {
                    break;
                }

                frame = frame.Next;
            }

            // this should not run unless we found mixed frames...
            while (frame != null && frame.SampleOffset + frame.SampleCount < sampleIndex)
            {
                if (frame == _last && !_endFound)
                {
                    do
                    {
                        FindNextFrame();
                    } while (frame == _last && !_endFound);
                }

                frame = frame.Next;
            }
            if (frame == null)
                return -1;
            return (_current = frame).SampleOffset;
        }

        public MpegFrame? NextFrame()
        {
            // if _current is null, we've returned the last frame already
            var frame = _current;
            if (frame != null)
            {
                if (CanSeek)
                {
                    frame.SaveBuffer();
                }

                if (frame == _last && !_endFound)
                {
                    do
                    {
                        FindNextFrame();
                    }
                    while (frame == _last && !_endFound);
                }

                _current = frame.Next;

                if (!CanSeek)
                {
                    // if we're in a forward-only stream, 
                    // don't bother keeping the frames that have already been processed

                    Debug.Assert(_first != null);

                    var tmp = _first;
                    _first = tmp.Next;
                    tmp.Next = null;
                }
            }
            return frame;
        }

        public MpegFrame? GetCurrentFrame()
        {
            return _current;
        }
    }
}
