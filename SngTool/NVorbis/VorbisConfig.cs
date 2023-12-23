namespace NVorbis
{
    /// <summary>
    /// Holds configuration and state used by the library.
    /// </summary>
    public class VorbisConfig
    {
        /// <summary>
        /// Gets the global config instance.
        /// </summary>
        public static VorbisConfig Default { get; } = new()
        {
            PageDataPool = new PageDataPool(),
        };

        internal PageDataPool PageDataPool { get; init; }

        private VorbisConfig()
        {
            PageDataPool = null!;
        }

        /// <summary>
        /// Clones the config instance.
        /// </summary>
        public VorbisConfig Clone()
        {
            return new VorbisConfig()
            {
                PageDataPool = PageDataPool,
            };
        }
    }
}
