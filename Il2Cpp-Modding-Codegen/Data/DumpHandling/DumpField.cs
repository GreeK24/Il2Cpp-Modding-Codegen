﻿using Il2Cpp_Modding_Codegen.Parsers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data.DumpHandling
{
    internal class DumpField : IField
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeDefinition Type { get; }
        public TypeDefinition DeclaringType { get; }
        public string Name { get; }
        public int Offset { get; }

        public DumpField(TypeDefinition declaring, PeekableStreamReader fs)
        {
            DeclaringType = declaring;
            string line = fs.PeekLine().Trim();
            while (line.StartsWith("["))
            {
                Attributes.Add(new DumpAttribute(fs));
                line = fs.PeekLine().Trim();
            }
            line = fs.ReadLine().Trim();
            var split = line.Split(' ');
            // Offset is at the end
            if (split.Length < 4)
            {
                throw new InvalidOperationException($"Field cannot be created from: {line}");
            }
            Offset = Convert.ToInt32(split[split.Length - 1], 16);
            int start = split.Length - 3;
            for (int i = start; i > 1; i--)
            {
                if (split[i] == "=")
                {
                    start = i - 1;
                    break;
                }
            }
            Name = split[start].TrimEnd(';');
            Type = new TypeDefinition(split[start - 1]);
            for (int i = 0; i < start - 1; i++)
            {
                Specifiers.Add(new DumpSpecifier(split[i]));
            }
        }

        public override string ToString()
        {
            var s = "";
            foreach (var atr in Attributes)
            {
                s += $"{atr}\n\t";
            }
            foreach (var spec in Specifiers)
            {
                s += $"{spec} ";
            }
            s += $"{Type} {Name}; // 0x{Offset:X}";
            return s;
        }
    }
}