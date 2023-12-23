namespace NVorbis.Contracts
{
    internal interface IFloor
    {
        FloorData CreateFloorData();

        void Unpack(ref VorbisPacket packet, FloorData floorData, int blockSize, int channel);

        void Apply(FloorData floorData, int blockSize, float[] residue);
    }
}
