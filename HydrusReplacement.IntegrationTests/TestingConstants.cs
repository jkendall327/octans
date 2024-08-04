namespace HydrusReplacement.IntegrationTests;

public class TestingConstants
{
    public static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8,             // SOI marker
        0xFF, 0xE0,             // APP0 marker
        0x00, 0x10,             // Length of APP0 segment
        0x4A, 0x46, 0x49, 0x46, 0x00, // JFIF identifier
        0x01, 0x01,             // JFIF version
        0x00,                   // Units
        0x00, 0x01,             // X density
        0x00, 0x01,             // Y density
        0x00, 0x00,             // Thumbnail width and height
        0xFF, 0xD9              // EOI marker
    ];
}