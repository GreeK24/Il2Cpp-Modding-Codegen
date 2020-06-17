﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
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

        private Dictionary<ITypeData, State> map = new Dictionary<ITypeData, State>();
        private CppFieldSerializer fieldSerializer;
        private CppStaticFieldSerializer staticFieldSerializer;
        private CppMethodSerializer methodSerializer;
        private SerializationConfig _config;
        public readonly CppContextSerializer serializer;

        public CppTypeDataSerializer(SerializationConfig config, CppContextSerializer serializer, bool asHeader = true)
        {
            _config = config;
            _asHeader = asHeader;
            this.serializer = serializer;
        }

        public override void PreSerialize(CppSerializerContext context, ITypeData type)
        {
            if (_asHeader)
            {
                var resolved = context.GetCppName(type.This);
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
                        s.parentName = context.GetCppName(type.Parent);
                }
                map[type] = s;

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

            //// PreSerialize any nested types
            //foreach (var nested in type.NestedTypes)
            //    PreSerialize(context, nested);
        }

        private CppHeaderCreator _header;
        private CppSerializerContext _context;

        public void Serialize(CppStreamWriter writer, ITypeData type, CppHeaderCreator header, CppSerializerContext context)
        {
            _header = header;
            _context = context;
            Serialize(writer, type);
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
                var state = map[type];
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
                        if (!first) templateStr += ", ";
                        templateStr += "typename " + genParam.Name;
                        first = false;
                    }
                    writer.WriteLine(templateStr + ">");
                }

                // TODO: print enums as actual C++ smart enums? backing type is type of _value and A = #, should work for the lines inside the enum
                typeHeader = (type.Type == TypeEnum.Struct ? "struct " : "class ") + state.type;
                writer.WriteDefinition(typeHeader + s);
                writer.WriteLine("public:");
                writer.Flush();

                //// now write any nested types
                //foreach (var nested in type.NestedTypes)
                //{
                //    Serialize(writer, nested);
                //}

                // write any class forward declares
                // We use the context serializer here, once more.
                if (_asHeader)
                    serializer.WriteNestedForwardDeclares();
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