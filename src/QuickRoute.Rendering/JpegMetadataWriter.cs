using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Rendering
{
  /// <summary>
  /// Writes a minimal EXIF APP1 segment containing the software name and a GPS position into a JPEG stream.
  /// </summary>
  internal static class JpegMetadataWriter
  {
    private const ushort TagSoftware = 0x0131;
    private const ushort TagGpsInfo = 0x8825;
    private const ushort TagGpsVersion = 0x0000;
    private const ushort TagGpsLatitudeRef = 0x0001;
    private const ushort TagGpsLatitude = 0x0002;
    private const ushort TagGpsLongitudeRef = 0x0003;
    private const ushort TagGpsLongitude = 0x0004;

    /// <summary>
    /// Ensures that the JPEG data starts with a JFIF APP0 segment, which the QuickRoute JPEG extension data relies on.
    /// </summary>
    public static byte[] EnsureJfifHeader(byte[] jpeg)
    {
      if (jpeg.Length < 4 || jpeg[0] != 0xff || jpeg[1] != 0xd8) throw new InvalidDataException("Not a JPEG image.");
      if (jpeg[2] == 0xff && jpeg[3] == 0xe0) return jpeg;
      var jfif = new byte[]
      {
        0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00
      };
      var result = new byte[jpeg.Length + jfif.Length];
      Buffer.BlockCopy(jpeg, 0, result, 0, 2);
      Buffer.BlockCopy(jfif, 0, result, 2, jfif.Length);
      Buffer.BlockCopy(jpeg, 2, result, 2 + jfif.Length, jpeg.Length - 2);
      return result;
    }

    /// <summary>
    /// Inserts an EXIF segment directly after the leading JFIF APP0 segment.
    /// </summary>
    public static byte[] InsertExif(byte[] jpeg, string software, LongLat position)
    {
      jpeg = EnsureJfifHeader(jpeg);
      var app0Length = (jpeg[4] << 8) + jpeg[5];
      var insertAt = 4 + app0Length;
      var exif = CreateExifSegment(software, position);
      var result = new byte[jpeg.Length + exif.Length];
      Buffer.BlockCopy(jpeg, 0, result, 0, insertAt);
      Buffer.BlockCopy(exif, 0, result, insertAt, exif.Length);
      Buffer.BlockCopy(jpeg, insertAt, result, insertAt + exif.Length, jpeg.Length - insertAt);
      return result;
    }

    private static byte[] CreateExifSegment(string software, LongLat position)
    {
      var softwareBytes = Encoding.ASCII.GetBytes((software ?? "") + "\0");
      var tiff = new MemoryStream();
      var w = new BinaryWriter(tiff);

      // TIFF header, little endian
      w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write(8u);

      // IFD0: Software, GPS IFD pointer
      const int ifd0Offset = 8;
      const int ifd0EntryCount = 2;
      var ifd0Size = 2 + ifd0EntryCount * 12 + 4;
      var softwareOffset = ifd0Offset + ifd0Size;
      var gpsIfdOffset = softwareOffset + softwareBytes.Length + (softwareBytes.Length % 2);
      w.Write((ushort)ifd0EntryCount);
      WriteEntry(w, TagSoftware, 2, (uint)softwareBytes.Length, (uint)softwareOffset);
      WriteEntry(w, TagGpsInfo, 4, 1, (uint)gpsIfdOffset);
      w.Write(0u);
      w.Write(softwareBytes);
      if (softwareBytes.Length % 2 == 1) w.Write((byte)0);

      // GPS IFD
      const int gpsEntryCount = 5;
      var gpsIfdSize = 2 + gpsEntryCount * 12 + 4;
      var latitudeOffset = gpsIfdOffset + gpsIfdSize;
      var longitudeOffset = latitudeOffset + 24;
      w.Write((ushort)gpsEntryCount);
      WriteEntry(w, TagGpsVersion, 1, 4, BitConverter.ToUInt32(new byte[] { 2, 2, 0, 0 }, 0));
      WriteEntry(w, TagGpsLatitudeRef, 2, 2, (uint)(position.Latitude < 0 ? 'S' : 'N'));
      WriteEntry(w, TagGpsLatitude, 5, 3, (uint)latitudeOffset);
      WriteEntry(w, TagGpsLongitudeRef, 2, 2, (uint)(position.Longitude < 0 ? 'W' : 'E'));
      WriteEntry(w, TagGpsLongitude, 5, 3, (uint)longitudeOffset);
      w.Write(0u);
      WriteCoordinate(w, Math.Abs(position.Latitude));
      WriteCoordinate(w, Math.Abs(position.Longitude));
      w.Flush();

      var payload = new List<byte>();
      payload.AddRange(Encoding.ASCII.GetBytes("Exif\0\0"));
      payload.AddRange(tiff.ToArray());
      var length = payload.Count + 2;
      if (length > 0xffff) throw new InvalidOperationException("EXIF data is too large.");
      var segment = new byte[2 + length];
      segment[0] = 0xff;
      segment[1] = 0xe1;
      segment[2] = (byte)(length >> 8);
      segment[3] = (byte)(length & 0xff);
      payload.CopyTo(segment, 4);
      return segment;
    }

    private static void WriteEntry(BinaryWriter w, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
      w.Write(tag);
      w.Write(type);
      w.Write(count);
      w.Write(valueOrOffset);
    }

    /// <summary>
    /// Writes degrees, minutes and seconds as three rationals, matching the format used by earlier QuickRoute versions.
    /// </summary>
    private static void WriteCoordinate(BinaryWriter w, double coordinate)
    {
      var d = (uint)coordinate;
      var m = (uint)(60 * coordinate - 60 * d);
      var s = (uint)(1000000 * (3600 * coordinate - (3600 * d + 60 * m)));
      w.Write(d); w.Write(1u);
      w.Write(m); w.Write(1u);
      w.Write(s); w.Write(1000000u);
    }
  }
}
