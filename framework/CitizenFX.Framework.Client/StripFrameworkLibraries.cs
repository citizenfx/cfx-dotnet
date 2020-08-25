#if NETSTANDARD
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CitizenFX.BuildInfrastructure
{
    public static class Extensions
    {
        public static ITypeDefOrRef ToBasicTypeDefOrRef(this TypeSig typeSig) {
            while (typeSig.Next != null)
                typeSig = typeSig.Next;

            if (typeSig is GenericInstSig)
                return ((GenericInstSig)typeSig).GenericType.TypeDefOrRef;
            if (typeSig is TypeDefOrRefSig)
                return ((TypeDefOrRefSig)typeSig).TypeDefOrRef;

            return null;
        }
    }

    public class StripFrameworkLibraries : Task
    {
        [Required]
		public ITaskItem[] FrameworkLibraries  { get; set; }
        
        [Required]
		public ITaskItem OutputDirectory  { get; set; }

        public override bool Execute()
        {
            void ExecuteFile(ITaskItem item)
            {
                Log.LogWarning("Writing file {0}", item.ItemSpec);

                var ar = new AssemblyResolver();
                ar.PreSearchPaths.Add(Path.GetDirectoryName(item.ItemSpec));

                ModuleDefMD module = ModuleDefMD.Load(item.ItemSpec, new ModuleContext(ar));

                List<TypeDef> typesToRemove = new List<TypeDef>();

                foreach (TypeDef type in module.GetTypes())
                {
                    // keep <Module>
                    if (type.IsGlobalModuleType)
                    {
                        continue;
                    }

                    if (!type.IsPublic && !type.IsNestedPublic)
                    {
                        typesToRemove.Add(type);
                        continue;
                    }

                    List<PropertyDef> propertiesToRemove = new List<PropertyDef>();
                    List<MethodDef> methodsToRemove = new List<MethodDef>();
                    List<FieldDef> fieldsToRemove = new List<FieldDef>();

                    foreach (MethodDef method in type.Methods)
                    {
                        if (!method.IsPublic ||
                            (method.HasReturnType &&
                                !(method.ReturnType.ToBasicTypeDefOrRef().ResolveTypeDef()?.IsPublic ?? true) &&
                                !(method.ReturnType.ToBasicTypeDefOrRef().ResolveTypeDef()?.IsNestedPublic ?? true)) ||
                            method.CustomAttributes.Any(attr => attr.AttributeType?.Name?.Contains("SecurityCritical") ?? false))
                        {
                            methodsToRemove.Add(method);
                        }
                        else
                        {
                            if (method.Parameters.Any(arg =>
                                !(arg.Type.ToBasicTypeDefOrRef().ResolveTypeDef()?.IsPublic ?? true) &&
                                !(arg.Type.ToBasicTypeDefOrRef().ResolveTypeDef()?.IsNestedPublic ?? true)))
                            {
                                methodsToRemove.Add(method);
                                continue;
                            }

                            method.Body = new dnlib.DotNet.Emit.CilBody();
                        }
                    }

                    foreach (var method in methodsToRemove)
                    {
                        type.Methods.Remove(method);
                    }

                    foreach (var field in type.Fields)
                    {
                        if (!field.IsPublic)
                        {
                            fieldsToRemove.Add(field);
                        }
                    }

                    foreach (var field in fieldsToRemove)
                    {
                        type.Fields.Remove(field);
                    }

                    foreach (var property in type.Properties)
                    {
                        var remove = true;

                        if (property.GetMethod != null && property.GetMethod.IsPublic)
                        {
                            remove = false;
                        }

                        if (property.SetMethod != null && property.SetMethod.IsPublic)
                        {
                            remove = false;
                        }

                        if (remove)
                        {
                            propertiesToRemove.Add(property);
                        }
                    }

                    foreach (var field in fieldsToRemove)
                    {
                        type.Fields.Remove(field);
                    }

                    foreach (var property in propertiesToRemove)
                    {
                        type.Properties.Remove(property);
                    }
                }

                foreach (var type in typesToRemove)
                {
                    if (type.IsNested)
                    {
                        type.DeclaringType.NestedTypes.Remove(type);
                    }
                    else
                    {
                        module.Types.Remove(type);
                    }
                }
                
                var attributesToRemove = new List<CustomAttribute>();

                foreach (var attr in module.Assembly.CustomAttributes)
                {
                    if (!(attr.AttributeType.ResolveTypeDef()?.IsPublic ?? false) &&
                        !(attr.AttributeType.ResolveTypeDef()?.IsNestedPublic ?? false))
                    {
                        attributesToRemove.Add(attr);
                    }
                }

                foreach (var attr in attributesToRemove)
                {
                    module.Assembly.CustomAttributes.Remove(attr);
                }

                string savePath = Path.Combine(OutputDirectory.ItemSpec, Path.GetFileName(item.ItemSpec));

                module.Write(savePath, new ModuleWriterOptions(module)
                {
                    Logger = DummyLogger.NoThrowInstance
                });
            }

            foreach (var item in FrameworkLibraries)
            {
                ExecuteFile(item);
            }

            return true;
        }
    }
}
#endif
