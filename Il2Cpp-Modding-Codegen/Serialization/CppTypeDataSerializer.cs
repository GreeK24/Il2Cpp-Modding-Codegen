﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppTypeDataSerializer : Serializer<ITypeData>
    {
        private bool _asHeader;

        private struct State
        {
            internal string type;
            internal string parentName;
        }

        // Uses TypeRef instead of ITypeData because nested types have different pointers
        private Dictionary<TypeRef, State> map = new Dictionary<TypeRef, State>();

        private Dictionary<TypeRef, CppTypeDataSerializer> nestedSerializers = new Dictionary<TypeRef, CppTypeDataSerializer>();
        private CppFieldSerializer fieldSerializer;
        private CppStaticFieldSerializer staticFieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;
        public readonly CppContextSerializer serializer;

        public CppSerializerContext Context { get; private set; }

        public CppTypeDataSerializer(SerializationConfig config, CppContextSerializer serializer, bool asHeader = true)
        {
            _config = config;
            _asHeader = asHeader;
            this.serializer = serializer;
        }

        public void AddNestedSerializer(TypeRef type, CppTypeDataSerializer serializer)
        {
            nestedSerializers.Add(type, serializer);
        }

        public override void PreSerialize(CppSerializerContext context, ITypeData type)
        {
            if (_asHeader)
            {
                var resolved = context.GetCppName(type.This, false, false, CppSerializerContext.ForceAsType.Literal);
                if (resolved is null)
                    throw new InvalidOperationException($"Could not resolve provided type: {type.This}!");
                var s = new State
                {
                    type = resolved,
                };
                if (type.Parent != null)
                {
                    // System::ValueType should be the 1 type where we want to extend System::Object without the Il2CppObject fields
                    if (_asHeader && type.This.Namespace == "System" && type.This.Name == "ValueType")
                        s.parentName = "Object";
                    else
                        s.parentName = context.GetCppName(type.Parent, true, false, CppSerializerContext.ForceAsType.Literal);
                }
                map.Add(type.This, s);

                if (fieldSerializer is null)
                    fieldSerializer = new CppFieldSerializer();
            }
            if (type.Type != TypeEnum.Interface)
            {
                if (methodSerializer is null)
                    methodSerializer = new CppMethodSerializer(_config, _asHeader);
                foreach (var m in type.Methods)
                    methodSerializer?.PreSerialize(context, m);
                foreach (var f in type.Fields)
                {
                    // If the field is a static field, we want to create two methods, (get and set for the static field)
                    // and make a call to GetFieldValue and SetFieldValue for those methods
                    if (f.Specifiers.IsStatic())
                    {
                        if (staticFieldSerializer is null)
                            staticFieldSerializer = new CppStaticFieldSerializer(_asHeader, _config);
                        staticFieldSerializer.PreSerialize(context, f);
                    }
                    // Otherwise, if we are a header, preserialize the field
                    else if (_asHeader)
                        fieldSerializer.PreSerialize(context, f);
                }
            }
            Resolved(type);
            // TODO: Add a specific interface method serializer here, or provide more state to the original method serializer to support it

            // TODO: Add back PreSerialization of nested types instead of our weird header map stuff
            //// PreSerialize any nested types
            //foreach (var nested in type.NestedTypes)
            //    PreSerialize(context, nested);
            Context = context;
        }

        private void WriteNestedType(CppStreamWriter writer, ITypeData type)
        {
            // TODO: Have some more comparison for true nested in place here
            var comment = "Nested type: " + type.This.GetQualifiedName();
            string typeStr = null;
            bool nestedInPlace = false;
            switch (type.Type)
            {
                case TypeEnum.Class:
                case TypeEnum.Interface:
                    typeStr = "class";
                    break;

                case TypeEnum.Struct:
                    typeStr = "struct";
                    break;

                case TypeEnum.Enum:
                    // For now, serialize enums as structs
                    typeStr = "struct";
                    nestedInPlace = true;
                    break;

                default:
                    throw new InvalidOperationException($"Cannot nested declare {type.This}! It is an: {type.Type}!");
            }
            // TODO: Actually add nestedInPlace
            if (!nestedInPlace || nestedInPlace)
            {
                // Only write the template for the declaration if and only if we are not nested in place
                // Because nested in place will write the template for itself.
                if (type.This.IsGenericTemplate)
                {
                    // If the type being resolved is generic, we must template it.
                    var generics = "template<";
                    bool first = true;
                    foreach (var g in type.This.Generics)
                    {
                        if (!first)
                            generics += ", ";
                        else
                            first = false;
                        generics += "typename " + g.GetName();
                    }
                    writer.WriteComment(comment + "<" + string.Join(", ", type.This.Generics.Select(tr => tr.Name)) + ">");
                    writer.WriteLine(generics + ">");
                }
                else
                    writer.WriteComment(comment);
                writer.WriteDeclaration(typeStr + " " + type.This.GetName());
            }
            //if (nestedInPlace)
            //    // We need to serialize the nested type using the nested type's context/serializer.
            //    // We should perform some sort of lookup here in order to accomplish this, since making a new one means we would have to preserialize again
            //    // We want to map from type --> CppTypeDataSerializer
            //    if (nestedSerializers.TryGetValue(type.This, out var s))
            //        s.Serialize(writer, type);
            //    else
            //        throw new UnresolvedTypeException(type.This.DeclaringType, type.This);
        }

        // Should be provided a file, with all references resolved:
        // That means that everything is already either forward declared or included (with included files "to be built")
        // That is the responsibility of our parent serializer, who is responsible for converting the context into that
        public override void Serialize(CppStreamWriter writer, ITypeData type)
        {
            // Populated only for headers; contains the e.g. `struct X` or `class Y` for type
            string typeHeader = "";
            if (_asHeader)
            {
                if (!map.TryGetValue(type.This, out var state))
                    throw new UnresolvedTypeException(type.This.DeclaringType, type.This);
                // Write the actual type definition start
                var specifiers = "";
                foreach (var spec in type.Specifiers)
                    specifiers += spec + " ";
                writer.WriteComment("Autogenerated type: " + specifiers + type.This);
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
                if (state.parentName != null)
                    s = $" : public {state.parentName}";
                // TODO: add implementing interfaces to s
                if (type.This.IsGenericTemplate)
                {
                    var templateStr = "template<";
                    bool first = true;
                    foreach (var genParam in type.This.Generics)
                    {
                        if (!first)
                            templateStr += ", ";
                        else
                            first = false;
                        templateStr += "typename " + genParam.Name;
                    }
                    writer.WriteLine(templateStr + ">");
                }

                // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
                typeHeader = (type.Type == TypeEnum.Struct ? "struct " : "class ") + state.type;
                writer.WriteDefinition(typeHeader + s);
                if (type.Fields.Count > 0 || type.Methods.Count > 0 || type.NestedTypes.Count > 0)
                    writer.WriteLine("public:");
                writer.Flush();

                //// now write any nested types
                //foreach (var nested in type.NestedTypes)
                //{
                //    Serialize(writer, nested);
                //}

                // write any class forward declares
                // We use the context serializer here, once more.
                if (_asHeader && type.NestedTypes.Count > 0)
                {
                    // Write a type declaration for each of these nested types, unless we want to write them as literals.
                    // We can even use this.Serialize for in-place-nested.
                    foreach (var nt in type.NestedTypes)
                    {
                        WriteNestedType(writer, nt);
                    }
                }
                writer.Flush();
            }

            if (type.Type != TypeEnum.Interface)
            {
                // Write fields if not an interface
                foreach (var f in type.Fields)
                {
                    try
                    {
                        if (f.Specifiers.IsStatic())
                            staticFieldSerializer.Serialize(writer, f);
                        else if (_asHeader)
                            // Only write standard fields if this is a header
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
            }
            // Write type closing "};"
            if (_asHeader)
            {
                writer.CloseDefinition($"; // {typeHeader}");
            }
            writer.Flush();
            Serialized(type);
        }
    }
}