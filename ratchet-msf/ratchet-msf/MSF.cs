/*                                                                           *
 * Copyright © 2018, Raphaël Boissel                                         *
 * Permission is hereby granted, free of charge, to any person obtaining     *
 * a copy of this software and associated documentation files, to deal in    *
 * the Software without restriction, including without limitation the        *
 * rights to use, copy, modify, merge, publish, distribute, sublicense,      *
 * and/or sell copies of the Software, and to permit persons to whom the     *
 * Software is furnished to do so, subject to the following conditions:      *
 *                                                                           *
 * - The above copyright notice and this permission notice shall be          *
 *   included in all copies or substantial portions of the Software.         *
 * - The Software is provided "as is", without warranty of any kind,         *
 *   express or implied, including but not limited to the warranties of      *
 *   merchantability, fitness for a particular purpose and noninfringement.  *
 *   In no event shall the authors or copyright holders. be liable for any   *
 *   claim, damages or other liability, whether in an action of contract,    *
 *   tort or otherwise, arising from, out of or in connection with the       *
 *   software or the use or other dealings in the Software.                  *
 * - Except as contained in this notice, the name of Raphaël Boissel shall   *
 *   not be used in advertising or otherwise to promote the sale, use or     *
 *   other dealings in this Software without prior written authorization     *
 *   from Raphaël Boissel.                                                   *
 *                                                                           */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ratchet.IO.Format
{
    /// <summary>
    /// This class provides a basic parser for the MSF format. This is mainly used as a base tool
    /// to implement support for PDB and other PDB-like debugging format
    /// </summary>
    public class MSF
    {
        public class Stream : System.IO.Stream
        {
            long _Length = 0;
            long _Position = 0;
            System.IO.Stream _Parent;
            long _BlockSize = 0;
            long[] _Blocks;

            public override bool CanRead { get { return true; } }

            public override bool CanSeek { get { return true; } }

            public override bool CanWrite { get { return false; } }

            public override long Length { get { return _Length; } }

            public override long Position { get { return _Position; } set { _Position = value; } }

            public override void Flush() { _Parent.Flush(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int readCount = 0;
                int blockReadCount = 0;
                while (count > 0)
                {
                    long blockToRead = _Blocks[_Position / _BlockSize];
                    long offsetInBlock = _Position - ((_Position / _BlockSize) * _BlockSize);
                    long sizeToRead = offsetInBlock + count > _BlockSize ? _BlockSize - offsetInBlock : count;

                    blockReadCount = ReadBlock(blockToRead, buffer, offset, (int)offsetInBlock, (int)sizeToRead);
                    if (blockReadCount < 0)
                    {
                        if (readCount > 0) { return readCount; }
                        else { return -1; }
                    }
                    readCount += blockReadCount;

                    count -= (int)sizeToRead;
                    offset += (int)sizeToRead;
                    _Position += sizeToRead;
                }
                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (origin == SeekOrigin.Begin) { _Position = offset; }
                else if (origin == SeekOrigin.Current) { _Position += offset; }
                else if (origin == SeekOrigin.End) { _Position = _Length - offset; }
                return _Position;
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            int ReadBlock(long blockToRead, byte[] buffer, int offset, int offsetInBlock, int count)
            {
                lock (_Parent)
                {
                    _Parent.Seek(blockToRead + offsetInBlock, SeekOrigin.Begin);
                    return _Parent.Read(buffer, offset, count);
                }
            }

            public Stream(System.IO.Stream Parent, long BlockSize, long Length, long[] Blocks)
            {
                _Parent = Parent;
                _Length = Length;
                _Blocks = Blocks;
                _BlockSize = BlockSize;
            }
        }

        static bool ReadMagick(System.IO.Stream Stream, string Text)
        {
            byte[] reference = System.Text.Encoding.ASCII.GetBytes(Text);
            byte[] magick = new byte[reference.Length];
            if (reference.Length != Stream.Read(magick, 0, magick.Length))
            {
                return false;
            }

            for (int n = 0; n < reference.Length; n++)
            {
                if (reference[n] != magick[n])
                {
                    return false;
                }
            }

            return true;
        }

        static bool ReadMagick(System.IO.Stream Stream)
        {
            long position = Stream.Position;
            if (ReadMagick(Stream, "Microsoft C/C++ MSF 7.00\r\n\x1A\x44\x53\x00\x00\x00")) { return true; }
            Stream.Seek(position, SeekOrigin.Begin);
            if (ReadMagick(Stream, "Microsoft C / C++ MSF 7.00\r\n\x1A\x44\x53\x00\x00\x00")) { return true; }
            return false;
        }

        static UInt32 ReadUInt32LE(byte[] data, int offset)
        {
            return (UInt32)data[offset + 0] +
                   (UInt32)data[offset + 1] * (UInt32)0x100 +
                   (UInt32)data[offset + 2] * (UInt32)0x10000 +
                   (UInt32)data[offset + 3] * (UInt32)0x1000000;
        }

        /// <summary>
        /// Open an MSF and return all the streams contains within in. Stream data are not cached in memory
        /// So closing the stream used to create the child streams or accessing it at the same time as the MSF streams
        /// without proper locking will result in undefined behavior.
        /// </summary>
        /// <param name="Stream"></param>
        /// <returns></returns>
        public static Stream[] Open(System.IO.Stream Stream)
        {
            // Read superblock Information
            List<Stream> streams = new List<Stream>();
            long superBlockOffset = Stream.Position;
            if (!ReadMagick(Stream)) { throw new Exception("Invalid file magic. The file might not be a valid MSF"); }
            byte[] superblock = new byte[4 * 6];
            if (Stream.Read(superblock, 0, superblock.Length) != superblock.Length) { throw new Exception("Invalid superblock. The file might be truncated"); }
            UInt32 BlockSize = ReadUInt32LE(superblock, 0);
            UInt32 FreeBlockMapBlock = ReadUInt32LE(superblock, 4);
            UInt32 NumBlock = ReadUInt32LE(superblock, 8);
            UInt32 NumDirectoryBytes = ReadUInt32LE(superblock, 12);
            UInt32 BlockMapAddr = ReadUInt32LE(superblock, 20);
            switch(BlockSize)
            {
                case 512: case 1024: case 2048: case 4096: break;
                default: throw new Exception("Invalid block size specified in the superblock (expected 512, 1024, 2048, 4096, got " + BlockSize.ToString() + ")");
            }

            long streamDirectoryBlocksOffset = BlockSize * BlockMapAddr;
            Stream.Seek(streamDirectoryBlocksOffset, System.IO.SeekOrigin.Begin);
            UInt32 streamDirectoryBlockCount = (NumDirectoryBytes + BlockSize - 1) / BlockSize;
            byte[] streamDirectoryBlocksBytes = new byte[streamDirectoryBlockCount * 4];
            if (Stream.Read(streamDirectoryBlocksBytes, 0, streamDirectoryBlocksBytes.Length) != streamDirectoryBlocksBytes.Length) { throw new Exception("Can't read the stream directory info. File might be corrupted or truncated"); }
            long[] streamDirectoryBlocks = new long[streamDirectoryBlockCount];
            for (int n = 0; n < streamDirectoryBlockCount; n++)
            {
                streamDirectoryBlocks[n] = ReadUInt32LE(streamDirectoryBlocksBytes, n * 4) * BlockSize + superBlockOffset;
            }
            Stream StreamDirectory = new Stream(Stream, BlockSize, NumDirectoryBytes, streamDirectoryBlocks);

            // Read the stream directory
            byte[] singleWord = new byte[4];
            if (StreamDirectory.Read(singleWord, 0, 4) != 4) { throw new Exception("Can't read the stream directory. File might be corrupted or truncated"); }
            UInt32 numStream = ReadUInt32LE(singleWord, 0);
            byte[] streamSizeBytes = new byte[numStream * 4];
            if (StreamDirectory.Read(streamSizeBytes, 0, streamSizeBytes.Length) != streamSizeBytes.Length) { throw new Exception("Can't read the stream directory. File might be corrupted or truncated"); }
            for (int n = 0; n < numStream; n++)
            {
                UInt32 length = ReadUInt32LE(streamSizeBytes, n * 4);
                long[] blocks = new long[(length + BlockSize - 1) / BlockSize];
                byte[] blocksBytes = new byte[blocks.Length * 4];
                if (StreamDirectory.Read(blocksBytes, 0, blocksBytes.Length) != blocksBytes.Length) { throw new Exception("Can't read the stream directory. File might be corrupted or truncated"); }

                for (int i = 0; i < blocks.Length; i++)
                {
                    blocks[i] = (long)ReadUInt32LE(blocksBytes, i * 4) * (long)BlockSize + superBlockOffset;
                }

                Stream stream = new Stream(Stream, BlockSize, length, blocks);
                streams.Add(stream);
            }

            // All good now we can return the streams from this MSF
            return streams.ToArray();
        }
    }
}
