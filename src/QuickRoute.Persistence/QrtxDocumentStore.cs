using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using QuickRoute.BusinessEntities;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Reads and writes the versioned QuickRoute2 document package (.qrtx): a ZIP archive containing
  /// <c>document.json</c> with the document data and the original, unmodified map image bytes.
  /// </summary>
  public static class QrtxDocumentStore
  {
    public const string FileExtension = ".qrtx";
    public const string DocumentEntryName = "document.json";
    private const long MaxDocumentEntryLength = 512L * 1024 * 1024;
    private const long MaxMapEntryLength = 2L * 1024 * 1024 * 1024 - 1;
    private static readonly DateTimeOffset entryTimestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool HasPackageHeader(ReadOnlySpan<byte> data)
    {
      return data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4b && data[2] == 0x03 && data[3] == 0x04;
    }

    public static void Save(Document document, Stream output)
    {
      if (document == null) throw new ArgumentNullException(nameof(document));
      var mapEntryName = "map" + GetImageExtension(document.Map.EncodedImage);
      using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

      var documentEntry = archive.CreateEntry(DocumentEntryName, CompressionLevel.Optimal);
      documentEntry.LastWriteTime = entryTimestamp;
      using (var entryStream = documentEntry.Open())
      using (var writer = new Utf8JsonWriter(entryStream, new JsonWriterOptions { Indented = true }))
      {
        DocumentJson.Write(writer, document, mapEntryName);
      }

      var mapEntry = archive.CreateEntry(mapEntryName, CompressionLevel.NoCompression);
      mapEntry.LastWriteTime = entryTimestamp;
      using (var entryStream = mapEntry.Open())
      {
        entryStream.Write(document.Map.EncodedImage, 0, document.Map.EncodedImage.Length);
      }
    }

    /// <summary>
    /// Saves the document to a file. The file is written to a temporary file first so that an existing file is never left half-written.
    /// </summary>
    public static void Save(Document document, string fileName)
    {
      var fullPath = Path.GetFullPath(fileName);
      var temporaryFileName = fullPath + ".tmp";
      try
      {
        using (var stream = new FileStream(temporaryFileName, FileMode.Create, FileAccess.Write, FileShare.None))
        {
          Save(document, stream);
        }
        File.Move(temporaryFileName, fullPath, overwrite: true);
      }
      finally
      {
        if (File.Exists(temporaryFileName)) File.Delete(temporaryFileName);
      }
    }

    public static Document Load(Stream input, string fileName = null)
    {
      try
      {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var documentEntry = archive.GetEntry(DocumentEntryName) ?? throw new InvalidDataException("The file is not a QuickRoute2 document.");
        if (documentEntry.Length > MaxDocumentEntryLength) throw new InvalidDataException("The QuickRoute2 document is too large.");
        using var json = JsonDocument.Parse(ReadEntry(documentEntry, MaxDocumentEntryLength));
        var document = DocumentJson.Read(json.RootElement, (entryName, size, source, sourceType) =>
        {
          var mapEntry = archive.GetEntry(entryName) ?? throw new InvalidDataException("The map image is missing from the document.");
          if (mapEntry.Length > MaxMapEntryLength) throw new InvalidDataException("The map image is too large.");
          return new Map(ReadEntry(mapEntry, MaxMapEntryLength), size, source, sourceType);
        });
        document.FileName = fileName;
        document.FileFormat = QuickRouteFileFormat.Qrtx;
        Document.UpdateDocumentToCurrentVersion(document);
        return document;
      }
      catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException || ex is System.Collections.Generic.KeyNotFoundException)
      {
        throw new InvalidDataException("The QuickRoute2 document is damaged or contains unexpected data.", ex);
      }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maxLength)
    {
      using var stream = entry.Open();
      using var memory = new MemoryStream(entry.Length > 0 && entry.Length <= int.MaxValue ? (int)entry.Length : 0);
      var buffer = new byte[81920];
      int read;
      while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
      {
        if (memory.Length + read > maxLength) throw new InvalidDataException("The QuickRoute2 document entry is too large.");
        memory.Write(buffer, 0, read);
      }
      return memory.ToArray();
    }

    internal static string GetImageExtension(byte[] data)
    {
      if (data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return ".jpg";
      if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47) return ".png";
      if (data.Length >= 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return ".gif";
      if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4d) return ".bmp";
      if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return ".webp";
      return ".bin";
    }
  }
}
