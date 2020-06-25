﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppHeaderCreator
    {
        private SerializationConfig _config;
        private CppContextSerializer _serializer;

        public CppHeaderCreator(SerializationConfig config, CppContextSerializer serializer)
        {
            _config = config;
            _serializer = serializer;
        }

        private Dictionary<CppTypeContext, string> templateAliases = new Dictionary<CppTypeContext, string>();

        private void AliasNestedTemplates(CppStreamWriter writer, CppTypeContext context)
        {
            if (context.DeclaringContext != null && context.LocalType.This.IsGeneric)
            {
                var templateLine = context.GetTemplateLine(false);
                if (string.IsNullOrEmpty(templateLine))
                    throw new Exception("context.GetTemplateLine(false) failed???");
                writer.WriteLine(templateLine);
                var typeStr = context.GetCppName(context.LocalType.This, false, true, CppTypeContext.NeedAs.Declaration, CppTypeContext.ForceAsType.Literal);
                var alias = Regex.Replace(typeStr, @"<[^>]*>", "").Replace("::", "_");
                templateAliases.Add(context, alias);
                writer.WriteLine($"using {alias} = typename {typeStr};");
            }
            foreach (var nested in context.NestedContexts.Where(n => n.InPlace))
                AliasNestedTemplates(writer, nested);
        }

        // Outputs a DEFINE_IL2CPP_ARG_TYPE call for every type defined by this file
        private void DefineIl2CppArgTypes(CppStreamWriter writer, CppTypeContext context)
        {
            var type = context.LocalType;
            // DEFINE_IL2CPP_ARG_TYPE
            var (ns, il2cppName) = type.This.GetIl2CppName();
            // For Name and Namespace here, we DO want all the `, /, etc
            if (!type.This.IsGeneric)
            {
                string fullName = context.GetCppName(context.LocalType.This, true, true, CppTypeContext.NeedAs.Definition);
                writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE({fullName}, \"{ns}\", \"{il2cppName}\");");
            }
            else
            {
                string templateName;
                if (!templateAliases.TryGetValue(context, out templateName))
                    templateName = context.GetCppName(context.LocalType.This, false, false, CppTypeContext.NeedAs.Declaration, CppTypeContext.ForceAsType.Literal);
                templateName = context.LocalType.This.GetNamespace() + "::" + templateName;

                var structStr = context.LocalType.Info.TypeFlags.HasFlag(TypeFlags.ReferenceType) ? "CLASS" : "STRUCT";

                writer.WriteLine($"DEFINE_IL2CPP_ARG_TYPE_GENERIC_{structStr}({templateName}, \"{ns}\", \"{il2cppName}\");");
            }
            foreach (var nested in context.NestedContexts.Where(n => n.InPlace))
                DefineIl2CppArgTypes(writer, nested);
        }

        public void Serialize(CppTypeContext context)
        {
            var data = context.LocalType;
            var headerLocation = Path.Combine(_config.OutputDirectory, _config.OutputHeaderDirectory, context.HeaderFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(headerLocation));
            using (var ms = new MemoryStream())
            {
                var rawWriter = new StreamWriter(ms);
                var writer = new CppStreamWriter(rawWriter, "  ");
                // Write header
                writer.WriteComment($"Autogenerated from {nameof(CppHeaderCreator)} on {DateTime.Now}");
                writer.WriteComment("Created by Sc2ad");
                writer.WriteComment("=========================================================================");
                writer.WriteLine("#pragma once");
                // TODO: determine when/if we need this
                writer.WriteLine("#pragma pack(push, 8)");
                // Write SerializerContext and actual type
                try
                {
                    _serializer.Serialize(writer, context, true);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteComment("Unresolved type exception!");
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.SkipIssue)
                        return;
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw new InvalidOperationException($"Cannot elevate {e} to a parent type- there is no parent type!");
                }
                AliasNestedTemplates(writer, context);
                // End the namespace
                writer.CloseDefinition();

                if (data.This.Namespace == "System" && data.This.Name == "ValueType")
                {
                    writer.WriteLine("template<class T>");
                    writer.WriteLine("struct is_value_type<T, typename std::enable_if_t<std::is_base_of_v<System::ValueType, T>>> : std::true_type{};");
                }

                DefineIl2CppArgTypes(writer, context);
                writer.Flush();

                writer.WriteLine("#pragma pack(pop)");
                writer.Flush();
                if (File.Exists(headerLocation))
                    throw new InvalidOperationException($"Was about to overwrite existing file: {headerLocation} with context: {context.LocalType.This}");
                using (var fs = File.OpenWrite(headerLocation))
                {
                    rawWriter.BaseStream.Position = 0;
                    rawWriter.BaseStream.CopyTo(fs);
                }
            }
        }
    }
}