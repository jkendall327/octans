namespace Octans.Tests;

public static class TestingConstants
{
    public static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8, // SOI marker
        0xFF, 0xE0, // APP0 marker
        0x00, 0x10, // Length of APP0 segment
        0x4A, 0x46, 0x49, 0x46, 0x00, // JFIF identifier
        0x01, 0x01, // JFIF version
        0x00, // Units
        0x00, 0x01, // X density
        0x00, 0x01, // Y density
        0x00, 0x00, // Thumbnail width and height
        0xFF, 0xDB, // DQT marker
        0x00, 0x43, // DQT length
        0x00, // Table ID and precision
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, // Quantization table (64 values)
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        0xFF, 0xC0, // SOF0 marker
        0x00, 0x0B, // Length of segment
        0x08, // Data precision
        0x00, 0x01, // Image height (1 pixel)
        0x00, 0x01, // Image width (1 pixel)
        0x01, // Number of components
        0x01, 0x11, 0x00, // Component 1 parameters (ID, sampling factors, quant. table no.)
        0xFF, 0xC4, // DHT marker
        0x00, 0x1F, // Length of DHT segment
        0x00, // DHT info
        0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B,
        0xFF, 0xDA, // SOS marker
        0x00, 0x08, // Length of SOS header
        0x01, // Number of components
        0x01, 0x00, // Component 1 parameters
        0x00, 0x3F, 0x00, // Spectral selection
        0x80, // Image data (single gray pixel)
        0xFF, 0xD9 // EOI marker
    ];
}