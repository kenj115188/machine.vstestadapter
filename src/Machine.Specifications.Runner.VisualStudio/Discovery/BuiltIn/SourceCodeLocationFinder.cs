﻿using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Machine.VSTestAdapter.Discovery.BuiltIn
{
    public class SourceCodeLocationFinder : IDisposable
    {
        private readonly Lazy<AssemblyDefinition> assemblyDefinition;

        public SourceCodeLocationFinder(string assemblyFilePath)
        {
            assemblyDefinition = new Lazy<AssemblyDefinition>(() => { return LoadAssembly(assemblyFilePath); });
        }

        public SourceCodeLocationInfo GetFieldLocation(string fullTypeName, string fieldName)
        {
            TypeDefinition type = Assembly.MainModule.GetType(HandleNestedTypeName(fullTypeName));
            if (type == null)
                return null;

            FieldDefinition field = type.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field == null)
                return null;

            return GetFieldLocationCore(type, fieldName);
        }

        private AssemblyDefinition LoadAssembly(string assemblyFilePath)
        {
            return AssemblyDefinition.ReadAssembly(assemblyFilePath, new ReaderParameters() {
                ReadSymbols = true,
            });
        }

        public void Dispose()
        {
            Assembly?.Dispose();
        }

        private AssemblyDefinition Assembly {
            get { return assemblyDefinition.Value; }
        }

        private string HandleNestedTypeName(string type)
        {
            return type.Replace("+", "/");
        }

        /// <summary>
        /// Field assignments get converted to assignments in the .ctor, so if we find that - we get the line info from there.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="fieldFullName"></param>
        /// <returns></returns>
        private SourceCodeLocationInfo GetFieldLocationCore(TypeDefinition type, string fieldFullName)
        {
            if (!type.HasMethods)
                return null;

            MethodDefinition constructorDefinition = type.Methods
                .SingleOrDefault(x => x.IsConstructor && !x.Parameters.Any() && x.Name.EndsWith(".ctor", StringComparison.Ordinal));

            if (!constructorDefinition.HasBody)
                return null;

            if (constructorDefinition.DebugInformation == null)
                return null;

            Instruction instruction = constructorDefinition.Body.Instructions
                .SingleOrDefault(x => x.Operand != null &&
                            x.Operand.GetType().IsAssignableFrom(typeof(FieldDefinition)) &&
                            ((MemberReference)x.Operand).Name == fieldFullName);

            while (instruction != null)
            {
                SequencePoint sequencePoint = constructorDefinition.DebugInformation?.GetSequencePoint(instruction);

                if (sequencePoint != null && !IsHidden(sequencePoint))
                {
                    return new SourceCodeLocationInfo()
                    {
                        CodeFilePath = sequencePoint.Document.Url,
                        LineNumber = sequencePoint.StartLine
                    };
                }

                instruction = instruction.Previous;
            }

            return null;
        }

        private bool IsHidden(SequencePoint sequencePoint)
        {
            return sequencePoint.IsHidden;
        }
    }

    public class SourceCodeLocationInfo
    {
        public string CodeFilePath { get; set; }
        public int LineNumber { get; set; }
    }
}
