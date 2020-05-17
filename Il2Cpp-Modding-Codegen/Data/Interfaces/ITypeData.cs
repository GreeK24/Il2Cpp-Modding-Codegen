﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Data
{
    public interface ITypeData
    {
        TypeDefinition This { get; }
        TypeEnum Type { get; }
        TypeInfo Info { get; }
        TypeDefinition Parent { get; }
        List<TypeDefinition> ImplementingInterfaces { get; }
        int TypeDefIndex { get; }
        List<IAttribute> Attributes { get; }
        List<ISpecifier> Specifiers { get; }
        List<IField> Fields { get; }
        List<IProperty> Properties { get; }
        List<IMethod> Methods { get; }
    }
}