﻿using Il2CppModdingCodegen.Parsers;
using System;
using System.Collections.Generic;

namespace Il2CppModdingCodegen.Data.DumpHandling
{
    internal class DumpProperty : IProperty
    {
        public List<IAttribute> Attributes { get; } = new List<IAttribute>();
        public List<ISpecifier> Specifiers { get; } = new List<ISpecifier>();
        public TypeRef Type { get; }
        public TypeRef DeclaringType { get; }
        public string Name { get; }
        public bool GetMethod { get; }
        public bool SetMethod { get; }

        internal DumpProperty(TypeRef declaring, PeekableStreamReader fs)
        {
            DeclaringType = declaring;
            var line = fs.PeekLine()?.Trim();
            while (line != null && line.StartsWith("["))
            {
                Attributes.Add(new DumpAttribute(fs));
                line = fs.PeekLine()?.Trim();
            }
            line = fs.ReadLine()?.Trim() ?? "";
            var split = line.Split(' ');
            if (split.Length < 5)
                throw new InvalidOperationException($"Line {fs.CurrentLineIndex}: Property cannot be created from: \"{line.Trim()}\"");

            // Start at the end (but before the }), count back until we hit a { (or we have gone 3 steps)
            // Keep track of how far back we count
            int i;
            for (i = 0; i < 3; i++)
            {
                var val = split[split.Length - 2 - i];
                if (val == "{")
                    break;
                else if (val == "get;")
                    GetMethod = true;
                else if (val == "set;")
                    SetMethod = true;
            }
            Name = split[split.Length - 3 - i];
            Type = new DumpTypeRef(DumpTypeRef.FromMultiple(split, split.Length - 4 - i, out int adjust, -1, " "));
            for (int j = 0; j < adjust; j++)
                Specifiers.Add(new DumpSpecifier(split[j]));
        }

        public override string ToString()
        {
            var s = "";
            foreach (var atr in Attributes)
                s += $"{atr}\n\t";
            foreach (var spec in Specifiers)
                s += $"{spec} ";
            s += $"{Type} {Name}";
            s += " { ";
            if (GetMethod)
                s += "get; ";
            if (SetMethod)
                s += "set; ";
            s += "}";
            return s;
        }
    }
}
