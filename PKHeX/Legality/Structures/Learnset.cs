﻿using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PKHeX.Core
{
    public abstract class Learnset
    {
        protected int Count;
        protected int[] Moves;
        protected int[] Levels;

        public int[] getMoves(int level)
        {
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i] > level)
                    return Moves.Take(i).ToArray();
            return Moves;
        }
    }

    public class Learnset1 : Learnset
    {
        private Learnset1(byte[] data, ref int offset)
        {
            var moves = new List<int>();
            var levels = new List<int>();
            while (data[offset] != 0)
            {
                levels.Add(data[offset++]);
                moves.Add(data[offset++]);
            }
            ++offset;

            Moves = moves.ToArray();
            Levels = levels.ToArray();
            Count = Moves.Length;
        }
        public static Learnset[] getArray(byte[] input, int maxSpecies)
        {
            var data = new Learnset[maxSpecies + 1];

            int offset = 0;
            for (int s = 0; s < data.Length; s++)
                data[s] = new Learnset1(input, ref offset);

            return data;
        }
    }
    public class Learnset6 : Learnset
    {
        private Learnset6(byte[] data)
        {
            if (data.Length < 4 || data.Length % 4 != 0)
            { Count = 0; Levels = new int[0]; Moves = new int[0]; return; }
            Count = data.Length / 4 - 1;
            Moves = new int[Count];
            Levels = new int[Count];
            using (BinaryReader br = new BinaryReader(new MemoryStream(data)))
                for (int i = 0; i < Count; i++)
                {
                    Moves[i] = br.ReadInt16();
                    Levels[i] = br.ReadInt16();
                }
        }
        public static Learnset[] getArray(byte[][] entries)
        {
            Learnset[] data = new Learnset[entries.Length];
            for (int i = 0; i < data.Length; i++)
                data[i] = new Learnset6(entries[i]);
            return data;
        }
    }
    public class Learnset7 : Learnset
    {
        private Learnset7(byte[] data)
        {
            if (data.Length < 4 || data.Length % 4 != 0)
            { Count = 0; Levels = new int[0]; Moves = new int[0]; return; }
            Count = data.Length / 4 - 1;
            Moves = new int[Count];
            Levels = new int[Count];
            using (BinaryReader br = new BinaryReader(new MemoryStream(data)))
                for (int i = 0; i < Count; i++)
                {
                    Moves[i] = br.ReadInt16();
                    Levels[i] = br.ReadInt16();
                }
        }
        public static Learnset[] getArray(byte[][] entries)
        {
            Learnset[] data = new Learnset[entries.Length];
            for (int i = 0; i < data.Length; i++)
                data[i] = new Learnset7(entries[i]);
            return data;
        }
    }
}
