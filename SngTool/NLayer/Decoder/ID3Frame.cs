﻿using System;

namespace NLayer.Decoder
{
    enum ID3FrameType
    {
        ID3v1,
        ID3v1Enh,
        ID3v2
    }

    internal sealed class ID3Frame : FrameBase
    {
        private ID3FrameType _version;

        public ID3Frame(ID3FrameType version)
        {
            _version = version;
        }

        protected override int ValidateFrameHeader()
        {
            switch (_version)
            {
                case ID3FrameType.ID3v2:
                    // v2, yay!
                    Span<byte> buf = stackalloc byte[7];
                    if (Read(3, buf) == 7)
                    {
                        byte flagsMask;
                        switch (buf[0])
                        {
                            case 2:
                                flagsMask = 0x3F;
                                break;
                            case 3:
                                flagsMask = 0x1F;
                                break;

                            case 4:
                                flagsMask = 0x0F;
                                break;

                            default:
                                return -1;
                        }

                        // ignore the flags (we don't need them for the validation)

                        // get the size (7 bits per byte [MSB cleared])
                        var size = (buf[3] << 21)
                                 | (buf[4] << 14)
                                 | (buf[5] << 7)
                                 | (buf[6]);

                        // finally, check to make sure that all the right bits are cleared
                        int flags =
                            (buf[2] & flagsMask) |
                            (buf[3] & 0x80) |
                            (buf[4] & 0x80) |
                            (buf[5] & 0x80) |
                            (buf[6] & 0x80);

                        if (!(flags != 0 || buf[1] == 0xFF))
                            return size + 10;   // don't forget the sync, flag & size bytes!
                    }
                    break;

                case ID3FrameType.ID3v1Enh:
                    // ID3v1 extended "TAG+"
                    return 227 + 128;

                case ID3FrameType.ID3v1:
                    // ID3v1 "TAG"
                    return 128;
            }

            return -1;
        }

        public override void Parse()
        {
            // assume we have to process it now or else...  
            // we can still read the whole frame, so no biggie
            switch (_version)
            {
                case ID3FrameType.ID3v2:
                    ParseV2();
                    break;

                case ID3FrameType.ID3v1Enh:
                    ParseV1Enh();
                    break;

                case ID3FrameType.ID3v1:
                    ParseV1(3);
                    break;
            }
        }

        private void ParseV1(int offset)
        {
            //var buffer = new byte[125];
            //if (Read(offset, buffer) == 125)
            //{
            //    // v1 tags use ASCII encoding... 
            //    // For now we'll use the built-in encoding, 
            //    // but for Win8 we'll have to build our own.
            //    var encoding = Encoding.ASCII;
            //
            //    // title (30)
            //    Title = encoding.GetString(buffer, 0, 30);
            //
            //    // artist (30)
            //    Artist = encoding.GetString(buffer, 30, 30);
            //
            //    // album (30)
            //    Album = encoding.GetString(buffer, 60, 30);
            //
            //    // year (4)
            //    Year = encoding.GetString(buffer, 90, 30);
            //
            //    // comment (30)*
            //    Comment = encoding.GetString(buffer, 94, 30);
            //
            //    if (buffer[122] == 0)
            //    {
            //        // track (1)*
            //        Track = (int)buffer[123];
            //    }
            //
            //    // genre (1)
            //    // ignore for now
            //
            //    // * if byte 29 of comment is 0, track is byte 30.  Otherwise, track is unknown.
            //}
        }

        private void ParseV1Enh()
        {
            ParseV1(230);

            //var buffer = new byte[223];
            //if (Read(4, buffer) == 223)
            //{
            //    // v1 tags use ASCII encoding... 
            //    // For now we'll use the built-in encoding, but for Win8 we'll have to build our own.
            //    var encoding = Encoding.ASCII;
            //
            //    // title (60)
            //    Title += encoding.GetString(buffer, 0, 60);
            //
            //    // artist (60)
            //    Artist += encoding.GetString(buffer, 60, 60);
            //
            //    // album (60)
            //    Album += encoding.GetString(buffer, 120, 60);
            //
            //    // speed (1)
            //    //var speed = buffer[180];
            //
            //    // genre (30)
            //    Genre = encoding.GetString(buffer, 181, 30);
            //
            //    // start-time (6)
            //    // 211
            //
            //    // end-time (6)
            //    // 217
            //}
        }

        private void ParseV2()
        {
            // v2 is much more complicated than v1...  don't worry about it for now
            // look for any merged frames, as well
        }

        public ID3FrameType Version => _version;

        //public string Title { get; private set; }
        //public string Artist { get; private set; }
        //public string Album { get; private set; }
        //public string Year { get; private set; }
        //public string Comment { get; private set; }
        //public int Track { get; private set; }
        //public string Genre { get; private set; }
        // speed
        //public TimeSpan StartTime { get; private set; }
        //public TimeSpan EndTime { get; private set; }

        public void Merge(ID3Frame newFrame)
        {
            // just save off the frame for parsing later
        }

        public static ID3Frame? TrySync(uint syncMark)
        {
            if ((syncMark & 0xFFFFFF00U) == 0x49443300)
                return new ID3Frame(ID3FrameType.ID3v2);

            if ((syncMark & 0xFFFFFF00U) == 0x54414700)
            {
                if ((syncMark & 0xFF) == 0x2B)
                    return new ID3Frame(ID3FrameType.ID3v1Enh);
                else
                    return new ID3Frame(ID3FrameType.ID3v1);
            }

            return null;
        }
    }
}
