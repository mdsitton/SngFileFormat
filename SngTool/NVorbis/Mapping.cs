using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using NVorbis.Contracts;

namespace NVorbis
{
    internal sealed class Mapping
    {
        private int[] _couplingAngle;
        private int[] _couplingMangitude;
        private IFloor[] _submapFloor;
        private Residue0[] _submapResidue;
        private IFloor[] _channelFloor;
        private FloorData[] _channelFloorData;
        private Residue0[] _channelResidue;
        private float[] _buf2;

        public Mapping(ref VorbisPacket packet, int channels, IFloor[] floors, Residue0[] residues)
        {
            int submapCount = 1;
            if (packet.ReadBit())
            {
                submapCount += (int)packet.ReadBits(4);
            }

            // square polar mapping
            int couplingSteps = 0;
            if (packet.ReadBit())
            {
                couplingSteps = (int)packet.ReadBits(8) + 1;
            }

            int couplingBits = Utils.ilog(channels - 1);
            _couplingAngle = new int[couplingSteps];
            _couplingMangitude = new int[couplingSteps];
            for (int j = 0; j < couplingSteps; j++)
            {
                int magnitude = (int)packet.ReadBits(couplingBits);
                int angle = (int)packet.ReadBits(couplingBits);
                if (magnitude == angle || magnitude > channels - 1 || angle > channels - 1)
                {
                    throw new System.IO.InvalidDataException("Invalid magnitude or angle in mapping header!");
                }
                _couplingAngle[j] = angle;
                _couplingMangitude[j] = magnitude;
            }

            if (0 != packet.ReadBits(2))
            {
                throw new System.IO.InvalidDataException("Reserved bits not 0 in mapping header.");
            }

            int[] mux = new int[channels];
            if (submapCount > 1)
            {
                for (int c = 0; c < channels; c++)
                {
                    mux[c] = (int)packet.ReadBits(4);
                    if (mux[c] > submapCount)
                    {
                        throw new System.IO.InvalidDataException("Invalid channel mux submap index in mapping header!");
                    }
                }
            }

            _submapFloor = new IFloor[submapCount];
            _submapResidue = new Residue0[submapCount];
            for (int j = 0; j < submapCount; j++)
            {
                packet.SkipBits(8); // unused placeholder
                int floorNum = (int)packet.ReadBits(8);
                if (floorNum >= floors.Length)
                {
                    throw new System.IO.InvalidDataException("Invalid floor number in mapping header!");
                }
                int residueNum = (int)packet.ReadBits(8);
                if (residueNum >= residues.Length)
                {
                    throw new System.IO.InvalidDataException("Invalid residue number in mapping header!");
                }

                _submapFloor[j] = floors[floorNum];
                _submapResidue[j] = residues[residueNum];
            }

            _channelFloor = new IFloor[channels];
            _channelFloorData = new FloorData[channels];
            _channelResidue = new Residue0[channels];
            for (int c = 0; c < channels; c++)
            {
                _channelFloor[c] = _submapFloor[mux[c]];
                _channelFloorData[c] = _channelFloor[c].CreateFloorData();
                _channelResidue[c] = _submapResidue[mux[c]];
            }

            _buf2 = Array.Empty<float>();
        }

        [SkipLocalsInit]
        public void DecodePacket(ref VorbisPacket packet, int blockSize, ReadOnlySpan<float[]> buffers)
        {
            Span<bool> noExecuteChannel = stackalloc bool[256];
            int halfBlockSize = blockSize >> 1;

            // read the noise floor data
            FloorData[] floorData = _channelFloorData;
            int channelCount = _channelFloor.Length;
            noExecuteChannel = noExecuteChannel.Slice(0, channelCount);
            for (int i = 0; i < _channelFloor.Length; i++)
            {
                floorData[i].Reset();
                _channelFloor[i].Unpack(ref packet, floorData[i], blockSize, i);
                noExecuteChannel[i] = !floorData[i].ExecuteChannel;

                // pre-clear the residue buffers
                Array.Clear(buffers[i], 0, halfBlockSize);
            }

            // make sure we handle no-energy channels correctly given the couplings..
            for (int i = 0; i < _couplingAngle.Length; i++)
            {
                if (floorData[_couplingAngle[i]].ExecuteChannel || floorData[_couplingMangitude[i]].ExecuteChannel)
                {
                    floorData[_couplingAngle[i]].ForceEnergy = true;
                    floorData[_couplingMangitude[i]].ForceEnergy = true;
                }
            }

            // decode the submaps into the residue buffer
            for (int i = 0; i < _submapFloor.Length; i++)
            {
                for (int j = 0; j < _channelFloor.Length; j++)
                {
                    if (_submapFloor[i] != _channelFloor[j] || _submapResidue[i] != _channelResidue[j])
                    {
                        // the submap doesn't match, so this floor doesn't contribute
                        floorData[j].ForceNoEnergy = true;
                    }
                }

                _submapResidue[i].Decode(ref packet, noExecuteChannel, blockSize, buffers);
            }

            // inverse coupling
            for (int i = _couplingAngle.Length - 1; i >= 0; i--)
            {
                if (!floorData[_couplingAngle[i]].ExecuteChannel &&
                    !floorData[_couplingMangitude[i]].ExecuteChannel)
                {
                    continue;
                }

                // we only have to do the first half; MDCT ignores the last half
                Span<float> magnitudeSpan = buffers[_couplingMangitude[i]].AsSpan(0, halfBlockSize);
                Span<float> angleSpan = buffers[_couplingAngle[i]].AsSpan(0, halfBlockSize);
                ApplyCoupling(magnitudeSpan, angleSpan);
            }

            if (halfBlockSize > _buf2.Length)
            {
                Array.Resize(ref _buf2, halfBlockSize);
            }

            // apply floor / dot product / MDCT (only run if we have sound energy in that channel)
            for (int c = 0; c < _channelFloor.Length; c++)
            {
                if (floorData[c].ExecuteChannel)
                {
                    _channelFloor[c].Apply(floorData[c], blockSize, buffers[c]);
                    Mdct.Reverse(buffers[c], _buf2, blockSize);
                }
                else
                {
                    // since we aren't doing the IMDCT, we have to explicitly clear the back half of the block
                    Array.Clear(buffers[c], halfBlockSize, halfBlockSize);
                }
            }
        }

        private static void ApplyCoupling(Span<float> magnitudeSpan, Span<float> angleSpan)
        {
            int length = Math.Min(magnitudeSpan.Length, angleSpan.Length);

            ref float magnitude = ref MemoryMarshal.GetReference(magnitudeSpan);
            ref float angle = ref MemoryMarshal.GetReference(angleSpan);

            int j = 0;
            if (Vector.IsHardwareAccelerated)
            {
                for (; j + Vector<float>.Count <= length; j += Vector<float>.Count)
                {
                    Vector<float> oldM = VectorHelper.LoadUnsafe(ref magnitude, j);
                    Vector<float> oldA = VectorHelper.LoadUnsafe(ref angle, j);

                    Vector<float> posM = Vector.GreaterThan<float>(oldM, Vector<float>.Zero);
                    Vector<float> posA = Vector.GreaterThan<float>(oldA, Vector<float>.Zero);

                    /*             newM; newA;
                         m &  a ==    0    -1
                         m & !a ==    1     0
                        !m &  a ==    0     1
                        !m & !a ==   -1     0
                    */

                    Vector<float> signMask = new Vector<uint>(1u << 31).As<uint, float>() & posM;
                    Vector<float> signedA = oldA ^ signMask;
                    Vector<float> newM = oldM - Vector.AndNot(signedA, posA);
                    Vector<float> newA = oldM + (signedA & posA);

                    newM.StoreUnsafe(ref magnitude, (nuint) j);
                    newA.StoreUnsafe(ref angle, (nuint) j);
                }
            }

            for (; j < length; j++)
            {
                float oldM = Unsafe.Add(ref magnitude, j);
                float oldA = Unsafe.Add(ref angle, j);

                float newM = oldM;
                float newA = oldM;

                if (oldM > 0)
                {
                    if (oldA > 0)
                    {
                        newA = oldM - oldA;
                    }
                    else
                    {
                        newM = oldM + oldA;
                    }
                }
                else
                {
                    if (oldA > 0)
                    {
                        newA = oldM + oldA;
                    }
                    else
                    {
                        newM = oldM - oldA;
                    }
                }

                Unsafe.Add(ref magnitude, j) = newM;
                Unsafe.Add(ref angle, j) = newA;
            }
        }
    }
}
