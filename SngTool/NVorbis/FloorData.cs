namespace NVorbis
{
    internal abstract class FloorData
    {
        public abstract bool ExecuteChannel { get; }
        public bool ForceEnergy { get; set; }
        public bool ForceNoEnergy { get; set; }

        public abstract void Reset();
    }
}
