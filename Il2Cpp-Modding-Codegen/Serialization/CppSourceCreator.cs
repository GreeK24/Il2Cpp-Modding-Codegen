﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppSourceCreator
    {
        private SerializationConfig _config;
        private CppSerializerContext _context;

        public CppSourceCreator(SerializationConfig config, CppSerializerContext context)
        {
            _config = config;
            _context = context;
        }

        public void Serialize(Serializer<ITypeData> serializer, ITypeData data)
        {
            if (data.Type == TypeEnum.Interface || data.Methods.Count == 0 || data.This.IsGeneric)
            {
                // Don't create C++ for types with no methods, or if it is an interface, or if it is generic
                return;
            }

            var headerLocation = _context.FileName + ".hpp";
            var sourceLocation = Path.Combine(_config.OutputDirectory, _config.OutputSourceDirectory, _context.FileName) + ".cpp";
            Directory.CreateDirectory(Path.GetDirectoryName(sourceLocation));
            using (var ms = new MemoryStream())
            {
                var rawWriter = new StreamWriter(ms);
                var writer = new CppStreamWriter(rawWriter);
                // Write header
                writer.WriteComment($"Autogenerated from {nameof(CppSourceCreator)} on {DateTime.Now}");
                writer.WriteComment($"Created by Sc2ad");
                writer.WriteComment("=========================================================================");
                // Write includes
                writer.WriteComment("Includes");
                writer.WriteLine($"#include \"{headerLocation}\"");
                writer.WriteLine("#include \"utils/il2cpp-utils.hpp\"");
                writer.WriteLine("#include \"utils/utils.h\"");
                if (_config.OutputStyle == OutputStyle.Normal)
                    writer.WriteLine("#include <optional>");
                foreach (var include in _context.Includes)
                {
                    writer.WriteLine($"#include \"{include}\"");
                }
                writer.WriteComment("End Includes");
                writer.Flush();
                // Write actual type
                try
                {
                    serializer.Serialize(writer, data);
                }
                catch (UnresolvedTypeException e)
                {
                    if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.DisplayInFile)
                    {
                        writer.WriteLine("// Unresolved type exception!");
                        writer.WriteLine("/*");
                        writer.WriteLine(e);
                        writer.WriteLine("*/");
                    }
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.SkipIssue)
                        return;
                    else if (_config.UnresolvedTypeExceptionHandling.TypeHandling == UnresolvedTypeExceptionHandling.Elevate)
                        throw new InvalidOperationException($"Cannot elevate {e} to a parent type- there is no parent type!");
                }
                writer.Flush();
                using (var fs = File.OpenWrite(sourceLocation))
                {
                    rawWriter.BaseStream.Position = 0;
                    rawWriter.BaseStream.CopyTo(fs);
                }
            }
        }
    }
}