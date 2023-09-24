﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adletec.Sonic.Util;

namespace Adletec.Sonic.Execution
{
    public class FunctionRegistry : IFunctionRegistry

    {
        private const string DynamicFuncName = "Adletec.Sonic.DynamicFunc";

        private readonly Dictionary<string, FunctionInfo> functions;
        private readonly bool guardedMode;
        private readonly bool caseSensitive;

        public FunctionRegistry(bool caseSensitive, bool guardedMode)
        {
            functions = caseSensitive
                ? new Dictionary<string, FunctionInfo>()
                : new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

            this.guardedMode = guardedMode;
            this.caseSensitive = caseSensitive;
        }

        public IEnumerator<FunctionInfo> GetEnumerator()
        {
            return functions.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public FunctionInfo GetFunctionInfo(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                throw new ArgumentNullException(nameof(functionName));

            return functions.TryGetValue(functionName, out var functionInfo) ? functionInfo : null;
        }

        public void RegisterFunction(string functionName, Delegate function)
        {
            RegisterFunction(functionName, function, true);
        }

        public void RegisterFunction(string functionName, Delegate function, bool isIdempotent)
        {
            if (string.IsNullOrEmpty(functionName))
                throw new ArgumentNullException(nameof(functionName));

            if (function == null)
                throw new ArgumentNullException(nameof(function));

            Type funcType = function.GetType();

            var isDynamicFunc = false;
            var numberOfParameters = -1;

            if (funcType.FullName != null && funcType.FullName.StartsWith("System.Func"))
            {
                foreach (Type genericArgument in funcType.GenericTypeArguments)
                    if (genericArgument != typeof(double))
                        throw new ArgumentException("Only doubles are supported as function arguments.",
                            nameof(function));

                numberOfParameters = function
                    .GetMethodInfo()
                    .GetParameters()
                    .Count(p => p.ParameterType == typeof(double));
            }
            else if (funcType.FullName != null && funcType.FullName.StartsWith(DynamicFuncName))
            {
                isDynamicFunc = true;
            }
            else
                throw new ArgumentException($"Only System.Func and {DynamicFuncName} delegates are permitted.",
                    nameof(function));

            if (guardedMode && functions.ContainsKey(functionName))
            {
                var message = $"The function \"{functionName}\" cannot be overwritten.";
                throw new ArgumentException(message);
            }

            if (functions.ContainsKey(functionName) && functions[functionName].NumberOfParameters != numberOfParameters)
            {
                throw new ArgumentException("The number of parameters cannot be changed when overwriting a method.");
            }

            if (functions.ContainsKey(functionName) && functions[functionName].IsDynamicFunc != isDynamicFunc)
            {
                throw new ArgumentException(
                    "A Func can only be overwritten by another Func and a DynamicFunc can only be overwritten by another DynamicFunc.");
            }

            var functionInfo = new FunctionInfo(functionName, numberOfParameters, isIdempotent,
                isDynamicFunc, function);

            functions[functionName] = functionInfo;
        }

        public bool IsFunctionName(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                throw new ArgumentNullException(nameof(functionName));

            return functions.ContainsKey(ConvertFunctionName(functionName));
        }
        private string ConvertFunctionName(string functionName)
        {
            return caseSensitive ? functionName : functionName.ToLowerFast();
        }

        bool IFunctionRegistry.caseSensitive()
        {
            return caseSensitive;
        }
    }
}