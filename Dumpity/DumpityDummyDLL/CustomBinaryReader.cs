using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DumpityDummyDLL
{
    public class CustomBinaryReader : BinaryReader
    {
        public CustomBinaryReader(Stream input) : base(input)
        {
        }

        public int ReadInt32BE()
        {
            var buff = ReadBytes(4);
            Array.Reverse(buff);
            return BitConverter.ToInt32(buff, 0);
        }

        public void AlignStream()
        {
            AlignStream(4);
        }

        public void AlignStream(int alignment)
        {
            var pos = BaseStream.Position;
            var mod = pos % alignment;
            if (mod != 0)
            {
                BaseStream.Position += alignment - mod;
            }
        }

        public byte ReadAlignedByte()
        {
            byte b = (byte)Read();
            AlignStream();
            return b;
        }

        public char ReadAlignedChar()
        {
            return (char)ReadAlignedByte();
        }

        public bool ReadAlignedBool()
        {
            return ReadAlignedByte() > 0;
        } 

        public string ReadAlignedString()
        {
            var length = ReadInt32();
            if (length > 0 && length <= BaseStream.Length - BaseStream.Position)
            {
                var stringData = ReadBytes(length);
                var result = Encoding.UTF8.GetString(stringData);
                AlignStream(4);
                return result;
            }
            return "";
        }

        public string ReadStringToNull(int maxLength = 32767)
        {
            var bytes = new List<byte>();
            int count = 0;
            while (BaseStream.Position != BaseStream.Length && count < maxLength)
            {
                var b = ReadByte();
                if (b == 0)
                {
                    break;
                }
                bytes.Add(b);
                count++;
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public byte[] ReadPrefixedBytes()
        {
            int length = ReadInt32();
            var bytes = ReadBytes(length);
            AlignStream();
            return bytes;
        }

        public List<T> ReadPrefixedList<T>(Func<CustomBinaryReader, T> del)
        {
            int length = ReadInt32();
            var list = new List<T>(length);
            for (int i = 0; i < length; i++)
            {
                list.Add(del(this));
            }
            return list;
        }

        public T[] ReadPrefixedArray<T>(Func<CustomBinaryReader, T> action)
        {
            return ReadPrefixedList<T>(action).ToArray();
        }

        public T[] ReadPrefixedArray<T>()
        {
            return ReadPrefixedArray<T>(r => (T)typeof(T).GetMethod(Constants.ReadMethodName,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, new object[] { r, 0 }));
        }

        public bool ReadAllZeros(int len)
        {
            byte[] padding = ReadBytes(len);
            foreach (byte b in padding)
            {
                if (b != 0) return false;
            }
            return true;
        }
    }
}
