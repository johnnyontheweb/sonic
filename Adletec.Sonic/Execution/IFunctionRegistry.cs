using System;
using System.Collections.Generic;

namespace Adletec.Sonic.Execution
{
    public interface IFunctionRegistry : IEnumerable<FunctionInfo>
    {
        FunctionInfo GetFunctionInfo(string functionName);
        bool IsFunctionName(string functionName);
        //bool caseSensitive();
        void RegisterFunction(string functionName, Delegate function);
        void RegisterFunction(string functionName, Delegate function, bool isIdempotent);
    }
}
