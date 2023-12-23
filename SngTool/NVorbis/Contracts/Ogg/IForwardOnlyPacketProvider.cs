using NVorbis.Ogg;

namespace NVorbis.Contracts.Ogg
{
    internal interface IForwardOnlyPacketProvider : IPacketProvider
    {
        bool AddPage(PageData pageData);

        void SetEndOfStream();
    }
}
