﻿using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.FormatRipper.Impl;
using JetBrains.FormatRipper.Pe.Impl;

namespace JetBrains.FormatRipper.Pe
{
  public sealed class PeFile
  {
    public readonly IMAGE_FILE_MACHINE Machine;
    public readonly IMAGE_FILE Characteristics;
    public readonly IMAGE_SUBSYSTEM Subsystem;
    public readonly IMAGE_DLLCHARACTERISTICS DllCharacteristics;
    public readonly bool HasSignature;
    public readonly bool HasMetadata;
    public readonly byte[]? CmsSignatureBlob;
    public readonly ComputeHashInfo ComputeHashInfo;
    public readonly StreamRange SecurityDataDirectoryRange;

    private PeFile(
      IMAGE_FILE_MACHINE machine,
      IMAGE_FILE characteristics,
      IMAGE_SUBSYSTEM subsystem,
      IMAGE_DLLCHARACTERISTICS dllCharacteristics,
      bool hasSignature,
      byte[]? cmsSignatureBlob,
      bool hasMetadata,
      StreamRange securityDataDirectoryRange,
      ComputeHashInfo computeHashInfo)
    {
      Machine = machine;
      Characteristics = characteristics;
      Subsystem = subsystem;
      DllCharacteristics = dllCharacteristics;
      HasSignature = hasSignature;
      CmsSignatureBlob = cmsSignatureBlob;
      HasMetadata = hasMetadata;
      SecurityDataDirectoryRange = securityDataDirectoryRange;
      ComputeHashInfo = computeHashInfo;
    }

    public static unsafe bool Is(Stream stream)
    {
      stream.Position = 0;
      IMAGE_DOS_HEADER ids;
      StreamUtil.ReadBytes(stream, (byte*)&ids, sizeof(IMAGE_DOS_HEADER));
      if (MemoryUtil.GetLeU2(ids.e_magic) != Magic.IMAGE_DOS_SIGNATURE)
        return false;

      stream.Position = MemoryUtil.GetLeU4(ids.e_lfanew);

      uint peMagic;
      StreamUtil.ReadBytes(stream, (byte*)&peMagic, sizeof(uint));
      return MemoryUtil.GetLeU4(peMagic) == Magic.IMAGE_NT_SIGNATURE;
    }

    [Flags]
    public enum Mode : uint
    {
      Default = 0,
      ReadCodeSignature = 0x1
    }

    public static unsafe PeFile Parse(Stream stream, Mode mode = Mode.Default)
    {
      stream.Position = 0;
      IMAGE_DOS_HEADER ids;
      StreamUtil.ReadBytes(stream, (byte*)&ids, sizeof(IMAGE_DOS_HEADER));
      if (MemoryUtil.GetLeU2(ids.e_magic) != Magic.IMAGE_DOS_SIGNATURE)
        throw new FormatException("Invalid DOS magic");
      stream.Position = MemoryUtil.GetLeU4(ids.e_lfanew);

      uint peMagic;
      StreamUtil.ReadBytes(stream, (byte*)&peMagic, sizeof(uint));
      if (MemoryUtil.GetLeU4(peMagic) != Magic.IMAGE_NT_SIGNATURE)
        throw new InvalidDataException("Invalid PE magic");

      IMAGE_FILE_HEADER ifh;
      StreamUtil.ReadBytes(stream, (byte*)&ifh, sizeof(IMAGE_FILE_HEADER));

      ushort iohMagic;
      StreamUtil.ReadBytes(stream, (byte*)&iohMagic, sizeof(ushort));

      uint sizeOfHeaders;
      IMAGE_SUBSYSTEM subsystem;
      IMAGE_DLLCHARACTERISTICS dllCharacteristics;
      uint numberOfRvaAndSizes;
      StreamRange checkSumRange;
      switch (MemoryUtil.GetLeU2(iohMagic))
      {
      case Magic.IMAGE_NT_OPTIONAL_HDR32_MAGIC:
        {
          if (MemoryUtil.GetLeU4(ifh.SizeOfOptionalHeader) < sizeof(IMAGE_OPTIONAL_HEADER32))
            throw new FormatException("Invalid 32-bit option header size");

          IMAGE_OPTIONAL_HEADER32 ioh;
          checkSumRange = new StreamRange(
            checked(stream.Position + ((byte*)&ioh.CheckSum - (byte*)&ioh)),
            sizeof(UInt32));
          StreamUtil.ReadBytes(stream, (byte*)&ioh, sizeof(IMAGE_OPTIONAL_HEADER32));
          sizeOfHeaders = MemoryUtil.GetLeU4(ioh.SizeOfHeaders);
          subsystem = (IMAGE_SUBSYSTEM)MemoryUtil.GetLeU2(ioh.Subsystem);
          dllCharacteristics = (IMAGE_DLLCHARACTERISTICS)MemoryUtil.GetLeU2(ioh.DllCharacteristics);
          numberOfRvaAndSizes = Math.Max(MemoryUtil.GetLeU4(ioh.NumberOfRvaAndSizes), ImageDirectory.IMAGE_NUMBEROF_DIRECTORY_ENTRIES);
        }
        break;
      case Magic.IMAGE_NT_OPTIONAL_HDR64_MAGIC:
        {
          if (MemoryUtil.GetLeU4(ifh.SizeOfOptionalHeader) < sizeof(IMAGE_OPTIONAL_HEADER64))
            throw new FormatException("Invalid 64-bit option header size");
          IMAGE_OPTIONAL_HEADER64 ioh;
          checkSumRange = new StreamRange(
            checked(stream.Position + ((byte*)&ioh.CheckSum - (byte*)&ioh)),
            sizeof(UInt32));
          StreamUtil.ReadBytes(stream, (byte*)&ioh, sizeof(IMAGE_OPTIONAL_HEADER64));
          sizeOfHeaders = MemoryUtil.GetLeU4(ioh.SizeOfHeaders);
          subsystem = (IMAGE_SUBSYSTEM)MemoryUtil.GetLeU2(ioh.Subsystem);
          dllCharacteristics = (IMAGE_DLLCHARACTERISTICS)MemoryUtil.GetLeU2(ioh.DllCharacteristics);
          numberOfRvaAndSizes = Math.Max(MemoryUtil.GetLeU4(ioh.NumberOfRvaAndSizes), ImageDirectory.IMAGE_NUMBEROF_DIRECTORY_ENTRIES);
        }
        break;
      default:
        throw new FormatException("Unsupported PE image optional header");
      }

      IMAGE_DATA_DIRECTORY securityIdd;
      IMAGE_DATA_DIRECTORY corIdd;
      var securityIddRange = new StreamRange(
        checked(stream.Position + ImageDirectory.IMAGE_DIRECTORY_ENTRY_SECURITY * sizeof(IMAGE_DATA_DIRECTORY)),
        sizeof(IMAGE_DATA_DIRECTORY));
      fixed (IMAGE_DATA_DIRECTORY* iddsBuf = new IMAGE_DATA_DIRECTORY[numberOfRvaAndSizes])
      {
        StreamUtil.ReadBytes(stream, (byte*)iddsBuf, checked((int)numberOfRvaAndSizes * sizeof(IMAGE_DATA_DIRECTORY)));
        securityIdd = iddsBuf[ImageDirectory.IMAGE_DIRECTORY_ENTRY_SECURITY];
        corIdd = iddsBuf[ImageDirectory.IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR];
      }

      var numberOfSections = MemoryUtil.GetLeU4(ifh.NumberOfSections);
      var ishs = new IMAGE_SECTION_HEADER[numberOfSections];
      fixed (IMAGE_SECTION_HEADER* ishsBuf = ishs)
      {
        StreamUtil.ReadBytes(stream, (byte*)ishsBuf, checked((int)numberOfSections * sizeof(IMAGE_SECTION_HEADER)));
      }

      // Note(ww898): Taken from https://download.microsoft.com/download/9/c/5/9c5b2167-8017-4bae-9fde-d599bac8184a/Authenticode_PE.docx
      //
      // To calculate the hash value
      //
      // 1.	Load the image header into memory.
      // 2.	Initialize a hash algorithm context.
      // 3.	Hash the image header from its base to immediately before the start of the checksum address, as specified in Optional Header Windows-Specific Fields.
      // 4.	Skip over the checksum, which is a 4-byte field.
      // 5.	Hash everything from the end of the checksum field to immediately before the start of the Certificate Table entry, as specified in Optional Header Data Directories.
      // 6.	Get the Attribute Certificate Table address and size from the Certificate Table entry. For details, see section 5.7 of the PE/COFF specification.
      // 7.	Exclude the Certificate Table entry from the calculation and hash everything from the end of the Certificate Table entry to the end of image header, including Section Table (headers).The Certificate Table entry is 8 bytes long, as specified in Optional Header Data Directories.
      // 8.	Create a counter called SUM_OF_BYTES_HASHED, which is not part of the signature. Set this counter to the SizeOfHeaders field, as specified in Optional Header Windows-Specific Field.
      // 9.	Build a temporary table of pointers to all of the section headers in the image. The NumberOfSections field of COFF File Header indicates how big the table should be. Do not include any section headers in the table whose SizeOfRawData field is zero.
      // 10.	Using the PointerToRawData field (offset 20) in the referenced SectionHeader structure as a key, arrange the table's elements in ascending order. In other words, sort the section headers in ascending order according to the disk-file offset of the sections.
      // 11.	Walk through the sorted table, load the corresponding section into memory, and hash the entire section. Use the SizeOfRawData field in the SectionHeader structure to determine the amount of data to hash.
      // 12.	Add the section’s SizeOfRawData value to SUM_OF_BYTES_HASHED.
      // 13.	Repeat steps 11 and 12 for all of the sections in the sorted table.
      // 14.	Create a value called FILE_SIZE, which is not part of the signature. Set this value to the image’s file size, acquired from the underlying file system. If FILE_SIZE is greater than SUM_OF_BYTES_HASHED, the file contains extra data that must be added to the hash. This data begins at the SUM_OF_BYTES_HASHED file offset, and its length is:
      // (File Size) – ((Size of AttributeCertificateTable) + SUM_OF_BYTES_HASHED)
      //
      // Note: The size of Attribute Certificate Table is specified in the second ULONG value in the Certificate Table entry (32 bit: offset 132, 64 bit: offset 148) in Optional Header Data Directories.
      // 15.	Finalize the hash algorithm context.
      // Note: This procedure uses offset values from the PE/COFF specification, version 8.1 . For authoritative offset values, refer to the most recent version of the PE/COFF specification.

      var sortedHashIncludeRanges = StreamRangeUtil.Invert(sizeOfHeaders, new List<StreamRange>
          {
            checkSumRange,
            securityIddRange
          });

      Array.Sort(ishs, (x, y) =>
        {
          if (x.PointerToRawData < y.PointerToRawData) return -1;
          if (x.PointerToRawData > y.PointerToRawData) return 1;
          return 0;
        });
      foreach (var ish in ishs)
        if (ish.PointerToRawData != 0 && ish.SizeOfRawData != 0)
          sortedHashIncludeRanges.Add(new StreamRange(ish.PointerToRawData, ish.SizeOfRawData));
      var sizeOfSections = sortedHashIncludeRanges[sortedHashIncludeRanges.Count - 1].Position +
                           sortedHashIncludeRanges[sortedHashIncludeRanges.Count - 1].Size;
      var sizeOfFile = stream.Length;

      var hasSignature = false;
      byte[]? cmsSignatureBlob = null;
      if (securityIdd.VirtualAddress != 0 && securityIdd.Size != 0)
      {
        if (securityIdd.VirtualAddress < sizeOfFile)
        {
          sortedHashIncludeRanges.Add(new StreamRange(sizeOfSections, securityIdd.VirtualAddress - sizeOfSections));
          if (securityIdd.VirtualAddress + securityIdd.Size < sizeOfFile)
            sortedHashIncludeRanges.Add(new StreamRange(securityIdd.VirtualAddress + securityIdd.Size, sizeOfFile - (securityIdd.VirtualAddress + securityIdd.Size)));
        }
        else
          sortedHashIncludeRanges.Add(new StreamRange(sizeOfSections, sizeOfFile - sizeOfSections));

        if (securityIdd.VirtualAddress + securityIdd.Size <= stream.Length)
        {
          if ((mode & Mode.ReadCodeSignature) == Mode.ReadCodeSignature)
          {
            stream.Position = securityIdd.VirtualAddress;
            WIN_CERTIFICATE wc;
            StreamUtil.ReadBytes(stream, (byte*)&wc, sizeof(WIN_CERTIFICATE));
            if (MemoryUtil.GetLeU2(wc.wCertificateType) != WinCertificate.WIN_CERT_TYPE_PKCS_SIGNED_DATA)
              throw new FormatException("Unsupported PE certificate type");
            cmsSignatureBlob = StreamUtil.ReadBytes(stream, checked((int)(MemoryUtil.GetLeU4(wc.dwLength) - sizeof(WIN_CERTIFICATE))));
          }

          hasSignature = true;
        }
      }
      else
        sortedHashIncludeRanges.Add(new StreamRange(sizeOfSections, sizeOfFile - sizeOfSections));

      var hasMetadata = TranslateVirtualAddress(ishs, ref corIdd) != 0;

      StreamRangeUtil.MergeNeighbors(sortedHashIncludeRanges);
      return new(
        (IMAGE_FILE_MACHINE)MemoryUtil.GetLeU2(ifh.Machine),
        (IMAGE_FILE)MemoryUtil.GetLeU2(ifh.Characteristics),
        subsystem, dllCharacteristics, hasSignature, cmsSignatureBlob, hasMetadata, securityIddRange,
        new ComputeHashInfo(0, sortedHashIncludeRanges, 0));
    }

    private static uint TranslateVirtualAddress(IMAGE_SECTION_HEADER[] ishs, ref IMAGE_DATA_DIRECTORY idd)
    {
      foreach (var ish in ishs)
        if (ish.VirtualAddress <= idd.VirtualAddress && idd.VirtualAddress + idd.Size < ish.VirtualAddress + ish.VirtualSize)
          return ish.PointerToRawData + (idd.VirtualAddress - ish.VirtualAddress);

      return 0;
    }
  }
}