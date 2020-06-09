﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Serialization.Interfaces;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeDataSerializer : ISerializer<ITypeData>
    {
        private bool _asHeader;

        private string _typeName;
        private string _parentName;
        private string _qualifiedName;
        private CppFieldSerializer fieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;

        public CppTypeDataSerializer(SerializationConfig config, bool asHeader = true)
        {
            _config = config;
            _asHeader = asHeader;
        }

        public void PreSerialize(ISerializerContext context, ITypeData type)
        {
            _qualifiedName = context.QualifiedTypeName;
            if (_asHeader)
            {
                _typeName = context.GetNameFromReference(type.This, ForceAsType.Literal, false, false);
                if (type.Parent != null)
                {
                    // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                    if (_asHeader && type.This.Namespace == "System" && type.This.Name == "ValueType")
                        _parentName = "Object";
                    else
                        _parentName = context.GetNameFromReference(type.Parent, ForceAsType.Literal, genericArgs: true);
                }

                if (fieldSerializer is null) fieldSerializer = new CppFieldSerializer();
                foreach (var f in type.Fields)
                    fieldSerializer.PreSerialize(context, f);
            }
            if (type.Type != TypeEnum.Interface)
            {
                if (methodSerializer is null) methodSerializer = new CppMethodSerializer(_config, _asHeader);
            }
            else // TODO: Add a specific interface method serializer here, or provide more state to the original method serializer to support it
                methodSerializer = null;

            foreach (var m in type.Methods)
                methodSerializer?.PreSerialize(context, m);

            // PreSerialize any nested types
            foreach (var nested in type.NestedTypes)
                PreSerialize(context, nested);
        }

        // Should be provided a file, with all references resolved:
        // That means that everything is already either forward declared or included (with included files "to be built")
        // That is the responsibility of our parent serializer, who is responsible for converting the context into that
        public void Serialize(IndentedTextWriter writer, ITypeData type)
        {
            // Populated only for headers; contains the e.g. `struct X` or `class Y` for type
            string typeHeader = "";
            if (_asHeader)
            {
                // Write the actual type definition start
                var specifiers = "";
                foreach (var spec in type.Specifiers)
                    specifiers += spec + " ";
                writer.WriteLine($"// Autogenerated type: {specifiers + type.This}");
                if (type.ImplementingInterfaces.Count > 0)
                {
                    writer.Write($"// Implementing Interfaces: ");
                    for (int i = 0; i < type.ImplementingInterfaces.Count; i++)
                    {
                        writer.Write(type.ImplementingInterfaces[i]);
                        if (i != type.ImplementingInterfaces.Count - 1)
                            writer.Write(", ");
                    }
                    writer.WriteLine();
                }
                string s = "";
                if (_parentName != null)
                    s = $" : public {_parentName}";
                // TODO: add implementing interfaces to s
                if (type.This.Generic)
                {
                    var templateStr = "template<";
                    bool first = true;
                    foreach (var genParam in type.This.GenericParameters)
                    {
                        if (!first) templateStr += ", ";
                        templateStr += "typename " + genParam.Name;
                        first = false;
                    }
                    writer.WriteLine(templateStr + ">");
                }

                // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
                typeHeader = (type.Type == TypeEnum.Struct ? "struct " : "class ") + _typeName;
                writer.WriteLine(typeHeader + s + " {");
                writer.Flush();
                writer.Indent++;

                // now write any nested types
                foreach (var nested in type.NestedTypes)
                {
                    Serialize(writer, nested);
                }

                // now write the fields
                if (type.Type != TypeEnum.Interface)
                {
                    // Write fields if not an interface
                    foreach (var f in type.Fields)
                    {
                        try
                        {
                            fieldSerializer.Serialize(writer, f);
                        }
                        catch (UnresolvedTypeException e)
                        {
                            if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                            {
                                writer.WriteLine("/*");
                                writer.WriteLine(e);
                                writer.WriteLine("*/");
                                writer.Flush();
                            }
                            else if (_config.UnresolvedTypeExceptionHandling.FieldHandling == UnresolvedTypeExceptionHandling.Elevate)
                                throw;
                        }
                    }
                    writer.Flush();
                }
            }  // end of if (_asHeader)

            // Finally, we write the methods
            foreach (var m in type.Methods)
            {
                try
                {
                    methodSerializer?.Serialize(writer, m);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                        writer.Flush();
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.MethodHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw;
                }
            }
            // Write type closing "};"
            if (_asHeader)
            {
                writer.Indent--;
                writer.WriteLine($"}};  // {typeHeader}");
            }
            writer.Flush();
        }
    }
}