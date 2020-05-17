﻿using System.Collections.Generic;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IProperty
    {
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        TypeDefinition Type { get; }
        TypeDefinition DeclaringType { get; }
        string Name { get; }
        bool GetMethod { get; }
        bool SetMethod { get; }
    }
}