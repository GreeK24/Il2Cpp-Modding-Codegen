﻿using Il2Cpp_Modding_Codegen.Config;
using Il2Cpp_Modding_Codegen.Data;
using Il2Cpp_Modding_Codegen.Data.DllHandling;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class CppMethodSerializer : Serializer<IMethod>
    {
        private static readonly HashSet<string> IgnoredMethods = new HashSet<string>() { "op_Implicit", "op_Explicit" };
        private bool _asHeader;
        private SerializationConfig _config;

        private Dictionary<IMethod, string> _resolvedReturns = new Dictionary<IMethod, string>();
        private Dictionary<IMethod, List<(string, ParameterFlags)>> _parameterMaps = new Dictionary<IMethod, List<(string, ParameterFlags)>>();
        private string _declaringFullyQualified;
        private bool _isInterface;
        private bool _pureVirtual;

        private HashSet<(TypeRef, string)> _signatures = new HashSet<(TypeRef, string)>();
        private bool _ignoreSignatureMap;
        private HashSet<IMethod> _aborted = new HashSet<IMethod>();

        public CppMethodSerializer(SerializationConfig config)
        {
            _config = config;
        }

        private bool NeedDefinitionInHeader(IMethod method)
        {
            return method.DeclaringType.IsGenericTemplate;
        }

        /// <summary>
        /// Returns whether the given method should be written as a definition or a declaration
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private CppTypeContext.NeedAs NeedTypesAs(IMethod method, bool returnType = false)
        {
            if (NeedDefinitionInHeader(method)) return CppTypeContext.NeedAs.BestMatch;
            if (returnType && _pureVirtual && method.HidesBase)
                // Prevents `error: return type of virtual function [name] is not covariant with the return type of the function
                //   it overrides ([return type] is incomplete)`
                return CppTypeContext.NeedAs.Definition;
            return CppTypeContext.NeedAs.Declaration;
        }

        public override void PreSerialize(CppTypeContext context, IMethod method)
        {
            if (method.Generic)
                // Skip generic methods
                return;

            // Get the fully qualified name of the context
            bool success = true;
            _declaringFullyQualified = context.QualifiedTypeName;
            var resolved = context.ResolveAndStore(method.DeclaringType, CppTypeContext.ForceAsType.Literal, CppTypeContext.NeedAs.Definition);
            _isInterface = resolved?.Type == TypeEnum.Interface;
            _pureVirtual = _isInterface && !method.DeclaringType.IsGeneric;
            var needAs = NeedTypesAs(method);
            // We need to forward declare everything used in methods (return types and parameters)
            // If we are writing the definition, we MUST define it
            var resolvedReturn = context.GetCppName(method.ReturnType, true, needAs: NeedTypesAs(method, true));
            if (resolvedReturn is null)
                // If we fail to resolve the return type, we will simply add a null item to our dictionary.
                // However, we should not call Resolved(method)
                success = false;
            _resolvedReturns.Add(method, resolvedReturn);
            var parameterMap = new List<(string, ParameterFlags)>();
            foreach (var p in method.Parameters)
            {
                // If this is not a header, we MUST define it
                var s = context.GetCppName(p.Type, true, needAs: needAs);
                if (s is null)
                    // If we fail to resolve a parameter, we will simply add a (null, p.Flags) item to our mapping.
                    // However, we should not call Resolved(method)
                    success = false;
                parameterMap.Add((s, p.Flags));
            }
            _parameterMaps.Add(method, parameterMap);
            if (success)
                Resolved(method);
        }

        private string WriteMethod(bool staticFunc, IMethod method, bool namespaceQualified)
        {
            var ns = "";
            var preRetStr = "";
            var overrideStr = "";
            var impl = "";
            if (namespaceQualified)
                ns = _declaringFullyQualified + "::";
            else
            {
                if (staticFunc)
                    preRetStr += "static ";

                // TODO: apply override correctly? It basically requires making all methods virtual
                // and if you miss any override the compiler gives you warnings
                //if (IsOverride(method))
                //    overrideStr += " override";
                if (_pureVirtual)
                {
                    preRetStr += "virtual ";
                    impl += " = 0";
                }
            }
            // Returns an optional
            // TODO: Should be configurable
            var retStr = _resolvedReturns[method];
            if (!method.ReturnType.IsVoid())
            {
                if (_config.OutputStyle == OutputStyle.Normal)
                    retStr = "std::optional<" + retStr + ">";
            }
            // Handles i.e. ".ctor"
            // Instead of using method.Name, we will instead use method.Il2CppName.
            // This allows us to write out interface declarations as needed.
            var nameStr = method.Il2CppName.Replace('<', '$').Replace('>', '$').Replace('.', '_');

            string paramString = method.Parameters.FormatParameters(_parameterMaps[method], FormatParameterMode.Names | FormatParameterMode.Types);
            var signature = $"{nameStr}({paramString})";

            if (!_ignoreSignatureMap && !_signatures.Add((method.DeclaringType, signature)))
            {
                preRetStr = "// ABORTED: conflicts with another method. " + preRetStr;
                _aborted.Add(method);
            }
            return $"{preRetStr}{retStr} {ns}{signature}{overrideStr}{impl}";
        }

        // Write the method here
        public override void Serialize(CppStreamWriter writer, IMethod method, bool asHeader)
        {
            if (_asHeader && !asHeader)
                _ignoreSignatureMap = true;
            _asHeader = asHeader;
            if (!_resolvedReturns.ContainsKey(method))
                // In the event we have decided to not parse this method (in PreSerialize) don't even bother.
                return;
            if (_resolvedReturns[method] == null)
                throw new UnresolvedTypeException(method.DeclaringType, method.ReturnType);
            var val = _parameterMaps[method].FindIndex(s => s.Item1 is null);
            if (val != -1)
                throw new UnresolvedTypeException(method.DeclaringType, method.Parameters[val].Type);
            // Blacklist and IgnoredMethods should still use method.Name, not method.Il2CppName
            if (IgnoredMethods.Contains(method.Name) || _config.BlacklistMethods.Contains(method.Name) || _aborted.Contains(method))
                return;
            if (method.DeclaringType.IsGeneric && !_asHeader)
                // Need to create the method ENTIRELY in the header, instead of split between the C++ and the header
                return;

            bool writeContent = !_asHeader || NeedDefinitionInHeader(method);
            if (_asHeader)
            {
                var methodComment = "";
                bool staticFunc = false;
                foreach (var spec in method.Specifiers)
                {
                    methodComment += $"{spec} ";
                    if (spec.Static)
                        staticFunc = true;
                }
                // Method comment should also use the Il2CppName whenever possible
                methodComment += $"{method.ReturnType} {method.Il2CppName}({method.Parameters.FormatParameters(csharp: true)})";
                methodComment += $" // Offset: 0x{method.Offset:X}";
                writer.WriteComment(methodComment);
                if (method.ImplementedFrom != null)
                    writer.WriteComment("Implemented from: " + method.ImplementedFrom);
                if (!writeContent)
                    writer.WriteDeclaration(WriteMethod(staticFunc, method, false));
            }
            else
            {
                // Comment for autogenerated method should use Il2CppName whenever possible
                writer.WriteComment("Autogenerated method: " + method.DeclaringType + "." + method.Il2CppName);
            }
            if (writeContent)
            {
                bool isStatic = method.Specifiers.IsStatic();
                // Write the qualified name if not in the header
                var methodStr = WriteMethod(isStatic, method, !_asHeader);
                if (methodStr.StartsWith("/"))
                {
                    writer.WriteLine(methodStr);
                    writer.Flush();
                    return;
                }
                writer.WriteDefinition(methodStr);

                var s = "";
                var innard = "";
                var macro = "RET_V_UNLESS(";
                if (_config.OutputStyle == OutputStyle.CrashUnless)
                    macro = "CRASH_UNLESS(";
                if (!method.ReturnType.IsVoid())
                {
                    s = "return ";
                    innard = $"<{_resolvedReturns[method]}>";
                    if (_config.OutputStyle != OutputStyle.CrashUnless) macro = "";
                }

                var macroEnd = string.IsNullOrEmpty(macro) ? "" : ")";
                if (!string.IsNullOrEmpty(macro) && innard.Contains(","))
                {
                    macro += "(";
                    macroEnd += ")";
                }

                // TODO: Replace with RET_NULLOPT_UNLESS or another equivalent (perhaps literally just the ret)
                s += $"{macro}il2cpp_utils::RunMethod{innard}(";
                if (!isStatic)
                {
                    s += "this, ";
                }
                else
                {
                    // TODO: Check to ensure this works with non-generic methods in a generic type
                    s += $"\"{method.DeclaringType.Namespace}\", \"{method.DeclaringType.Name}\", ";
                }
                var paramString = method.Parameters.FormatParameters(_parameterMaps[method], FormatParameterMode.Names);
                if (!string.IsNullOrEmpty(paramString))
                    paramString = ", " + paramString;
                // Macro string should use Il2CppName (of course, without _ replacement)
                s += $"\"{method.Il2CppName}\"{paramString}){macroEnd};";
                // Write method with return
                writer.WriteLine(s);
                // Close method
                writer.CloseDefinition();
            }
            writer.Flush();
            Serialized(method);
        }
    }
}