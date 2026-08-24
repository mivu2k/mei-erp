using MeiErp.Platform.Printing;
using Xunit;

namespace MeiErp.Modules.Hr.Tests;

public sealed class QrFrameDecoderTests
{
    [Fact]
    public void Decodes_a_real_attendance_QR_image()
    {
        const string payload = "MEIATT1:42:123456:ABCDEF01";
        Assert.Equal(payload, new QrFrameDecoder().Decode(Symbology.QrCode(payload, 320)));
    }

    [Fact]
    public void Invalid_or_empty_frames_are_ordinary_no_code_results()
    {
        var decoder = new QrFrameDecoder();
        Assert.Null(decoder.Decode([]));
        Assert.Null(decoder.Decode([1, 2, 3, 4]));
    }
}
