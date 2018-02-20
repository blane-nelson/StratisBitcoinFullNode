﻿using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Stratis.SmartContracts.ContractValidation
{
    /// <summary>
    /// TODO: Before this is ever close to being used in a test or production environment, 
    /// ensure that NO P/INVOKE OR INTEROP or other outside calls can be made.
    /// Also check there is no way around these rules, including recursion, funky namespaces,
    /// partial classes and extension methods, attributes
    /// </summary>
    public class SmartContractDeterminismValidator : ISmartContractValidator
    {
        /// <summary>
        /// System calls where we don't need to check any deeper - we just allow them.
        /// Sometimes contain 'non-deterministic' calls - e.g. if Resources file was changed.
        /// We assume all resource files are the same, as set in the CompiledSmartContract constructor.
        /// </summary>
        private static readonly HashSet<string> GreenLightMethods = new HashSet<string>
        {
            "System.String System.SR::GetResourceString(System.String,System.String)"
        };

        private static readonly HashSet<string> GreenLightTypes = new HashSet<string>
        {
            "System.Boolean",
            "System.Byte",
            "System.SByte",
            "System.Char",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Object",
            "System.String",
            "System.Array",
            "System.Exception",
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.List`1",
            "System.Linq.Enumerable",
            "Stratis.SmartContracts.SmartContractList`1",
            "Stratis.SmartContracts.SmartContractDictionary`2",
            "Stratis.SmartContracts.SmartContractMapping`2",
            typeof(PersistentState).FullName,
            typeof(SmartContract).FullName
        };

        private static readonly IEnumerable<IMethodDefinitionValidator> UserDefinedMethodValidators = new List<IMethodDefinitionValidator>
        {
            new ReferencedMethodReturnTypeValidator(),
            new PInvokeImplFlagValidator(),
            new UnmanagedFlagValidator(),
            new InternalFlagValidator(),
            new NativeMethodFlagValidator(),
            new MethodAllowedTypeValidator(),
            new GetHashCodeValidator(),
            new MethodInstructionValidator(),
            new AnonymousTypeValidator()
        };

        private static readonly IEnumerable<IMethodDefinitionValidator> NonUserMethodValidators = new List<IMethodDefinitionValidator>
        {
            new PInvokeImplFlagValidator(),
            new UnmanagedFlagValidator(),
            new InternalFlagValidator(),
            new NativeMethodFlagValidator(),
            new MethodAllowedTypeValidator(),
            new GetHashCodeValidator(),
            new MethodInstructionValidator()
        };

        public SmartContractValidationResult Validate(SmartContractDecompilation decompilation)
        {
            List<SmartContractValidationError> errors = new List<SmartContractValidationError>();

            IEnumerable<MethodDefinition> userDefinedMethods = decompilation.ContractType.Methods.Where(method => method.Body != null);
            Dictionary<string, MethodDefinition> allMethods = new Dictionary<string, MethodDefinition>();

            // Build a dict of all referenced methods
            foreach (MethodDefinition method in userDefinedMethods)
            {
                GetMethods(method, allMethods);
                errors.AddRange(ValidateUserDefinedMethod(method));
            }

            foreach (KeyValuePair<string, MethodDefinition> method in allMethods)
            {
                errors.AddRange(ValidateNonUserMethod(method.Value));
            }

            return new SmartContractValidationResult(errors);
        }

        private static IEnumerable<SmartContractValidationError> ValidateUserDefinedMethod(MethodDefinition method)
        {
            return ValidateWith(UserDefinedMethodValidators, method);
        }

        private static void GetMethods(MethodDefinition methodDefinition, IDictionary<string, MethodDefinition> visitedMethods)
        {
            if (methodDefinition.Body == null)
                return;

            IEnumerable<MethodReference> referencedMethods = methodDefinition.Body.Instructions
                .Select(instr => instr.Operand)
                .OfType<MethodReference>()
                .Where(referencedMethod =>
                    !(GreenLightMethods.Contains(methodDefinition.FullName)
                        || GreenLightTypes.Contains(methodDefinition.DeclaringType.FullName))
                );

            foreach (MethodReference method in referencedMethods)
            {
                MethodDefinition newMethod = method.Resolve();

                if (visitedMethods.ContainsKey(newMethod.FullName))
                {
                    continue;
                }

                visitedMethods.Add(newMethod.FullName, newMethod);
                GetMethods(newMethod, visitedMethods);
            }
        }

        private static IEnumerable<SmartContractValidationError> ValidateNonUserMethod(MethodDefinition method)
        {
            return ValidateWith(NonUserMethodValidators, method);
        }

        private static IEnumerable<SmartContractValidationError> ValidateWith(IEnumerable<IMethodDefinitionValidator> validators, MethodDefinition method)
        {
            var errors = new List<SmartContractValidationError>();

            foreach (IMethodDefinitionValidator validator in validators)
            {
                IEnumerable<SmartContractValidationError> validationResult = validator.Validate(method);
                errors.AddRange(validationResult);
            }

            return errors;
        }
    }
}