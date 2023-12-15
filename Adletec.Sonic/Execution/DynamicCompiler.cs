﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Adletec.Sonic.Operations;
using Adletec.Sonic.Util;

namespace Adletec.Sonic.Execution
{
    public class DynamicCompiler : IExecutor
    {
        private readonly string funcAssemblyQualifiedName;
        private readonly bool caseSensitive;
        private readonly bool guardedMode;

        public DynamicCompiler() : this(false, false)
        {
        }

        public DynamicCompiler(bool caseSensitive, bool guardedMode)
        {
            this.caseSensitive = caseSensitive;
            this.guardedMode = guardedMode;
            // The lower func reside in mscorelib, the higher ones in another assembly.
            // This is  an easy cross platform way to to have this AssemblyQualifiedName.
            funcAssemblyQualifiedName =
                typeof(Func<double, double, double, double, double, double, double, double, double, double>)
                    .GetTypeInfo().Assembly.FullName;
        }

        public double Execute(Operation operation, IFunctionRegistry functionRegistry,
            IConstantRegistry constantRegistry)
        {
            return Execute(operation, functionRegistry, constantRegistry, new Dictionary<string, double>());
        }

        public double Execute(Operation operation, IFunctionRegistry functionRegistry,
            IConstantRegistry constantRegistry,
            IDictionary<string, double> variables)
        {
            return BuildFormula(operation, functionRegistry, constantRegistry)(variables);
        }

        public Func<IDictionary<string, double>, double> BuildFormula(Operation operation,
            IFunctionRegistry functionRegistry, IConstantRegistry constantRegistry)
        {
            Func<FormulaContext, double> func = BuildFormulaInternal(operation, functionRegistry);
            
            // the multiplication of modes is makes the code a bit more complex, but this way we can avoid pretty
            // much all of the performance penalty for the additional checks in case they are disabled.
            if (caseSensitive)
            {
                if (guardedMode)
                {
                    return variables =>
                    {
                        VariableVerifier.VerifyVariableNames(variables,constantRegistry, functionRegistry);
                        var context = new FormulaContext(variables, functionRegistry, constantRegistry);
                        return func(context);
                    };
                }

                return variables =>
                {
                    var context = new FormulaContext(variables, functionRegistry, constantRegistry);
                    return func(context);
                };
            }

            if (guardedMode)
            {
                return variables =>
                {
                    variables = EngineUtil.ConvertVariableNamesToLowerCase(variables);
                    VariableVerifier.VerifyVariableNames(variables, constantRegistry, functionRegistry);
                    var context = new FormulaContext(variables, functionRegistry, constantRegistry);
                    return func(context);
                };
            }

            return variables =>
            {
                variables = EngineUtil.ConvertVariableNamesToLowerCase(variables);
                var context = new FormulaContext(variables, functionRegistry, constantRegistry);
                return func(context);
            };
        }

        private Func<FormulaContext, double> BuildFormulaInternal(Operation operation,
            IFunctionRegistry functionRegistry)
        {
            ParameterExpression contextParameter = Expression.Parameter(typeof(FormulaContext), "context");

            var lambda = Expression.Lambda<Func<FormulaContext, double>>(
                GenerateMethodBody(operation, contextParameter, functionRegistry),
                contextParameter
            );
            return lambda.Compile();
        }


        private Expression GenerateMethodBody(Operation operation, ParameterExpression contextParameter,
            IFunctionRegistry functionRegistry)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            if (operation.GetType() == typeof(IntegerConstant))
            {
                var constant = (IntegerConstant)operation;

                double value = constant.Value;
                return Expression.Constant(value, typeof(double));
            }

            if (operation.GetType() == typeof(FloatingPointConstant))
            {
                var constant = (FloatingPointConstant)operation;

                return Expression.Constant(constant.Value, typeof(double));
            }

            if (operation.GetType() == typeof(Variable))
            {
                var variable = (Variable)operation;

                Func<string, FormulaContext, double> getVariableValueOrThrow =
                    PrecompiledMethods.GetVariableValueOrThrow;
                return Expression.Call(null,
                    getVariableValueOrThrow.GetMethodInfo(),
                    Expression.Constant(variable.Name),
                    contextParameter);
            }

            if (operation.GetType() == typeof(Multiplication))
            {
                var multiplication = (Multiplication)operation;
                Expression argument1 = GenerateMethodBody(multiplication.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(multiplication.Argument2, contextParameter, functionRegistry);

                return Expression.Multiply(argument1, argument2);
            }

            if (operation.GetType() == typeof(Addition))
            {
                var addition = (Addition)operation;
                Expression argument1 = GenerateMethodBody(addition.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(addition.Argument2, contextParameter, functionRegistry);

                return Expression.Add(argument1, argument2);
            }

            if (operation.GetType() == typeof(Subtraction))
            {
                var addition = (Subtraction)operation;
                Expression argument1 = GenerateMethodBody(addition.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(addition.Argument2, contextParameter, functionRegistry);

                return Expression.Subtract(argument1, argument2);
            }

            if (operation.GetType() == typeof(Division))
            {
                var division = (Division)operation;
                Expression dividend = GenerateMethodBody(division.Dividend, contextParameter, functionRegistry);
                Expression divisor = GenerateMethodBody(division.Divisor, contextParameter, functionRegistry);

                return Expression.Divide(dividend, divisor);
            }

            if (operation.GetType() == typeof(Modulo))
            {
                var modulo = (Modulo)operation;
                Expression dividend = GenerateMethodBody(modulo.Dividend, contextParameter, functionRegistry);
                Expression divisor = GenerateMethodBody(modulo.Divisor, contextParameter, functionRegistry);

                return Expression.Modulo(dividend, divisor);
            }

            if (operation.GetType() == typeof(Exponentiation))
            {
                var exponentiation = (Exponentiation)operation;
                Expression @base = GenerateMethodBody(exponentiation.Base, contextParameter, functionRegistry);
                Expression exponent = GenerateMethodBody(exponentiation.Exponent, contextParameter, functionRegistry);

                return Expression.Call(null,
                    typeof(Math).GetRuntimeMethod("Pow", new[] { typeof(double), typeof(double) }), @base, exponent);
            }

            if (operation.GetType() == typeof(UnaryMinus))
            {
                var unaryMinus = (UnaryMinus)operation;
                Expression argument = GenerateMethodBody(unaryMinus.Argument, contextParameter, functionRegistry);
                return Expression.Negate(argument);
            }

            if (operation.GetType() == typeof(And))
            {
                var and = (And)operation;
                Expression argument1 =
                    Expression.NotEqual(GenerateMethodBody(and.Argument1, contextParameter, functionRegistry),
                        Expression.Constant(0.0));
                Expression argument2 =
                    Expression.NotEqual(GenerateMethodBody(and.Argument2, contextParameter, functionRegistry),
                        Expression.Constant(0.0));

                return Expression.Condition(Expression.And(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(Or))
            {
                var and = (Or)operation;
                Expression argument1 =
                    Expression.NotEqual(GenerateMethodBody(and.Argument1, contextParameter, functionRegistry),
                        Expression.Constant(0.0));
                Expression argument2 =
                    Expression.NotEqual(GenerateMethodBody(and.Argument2, contextParameter, functionRegistry),
                        Expression.Constant(0.0));

                return Expression.Condition(Expression.Or(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(LessThan))
            {
                var lessThan = (LessThan)operation;
                Expression argument1 = GenerateMethodBody(lessThan.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(lessThan.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.LessThan(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(LessOrEqualThan))
            {
                var lessOrEqualThan = (LessOrEqualThan)operation;
                Expression argument1 =
                    GenerateMethodBody(lessOrEqualThan.Argument1, contextParameter, functionRegistry);
                Expression argument2 =
                    GenerateMethodBody(lessOrEqualThan.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.LessThanOrEqual(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(GreaterThan))
            {
                GreaterThan greaterThan = (GreaterThan)operation;
                Expression argument1 = GenerateMethodBody(greaterThan.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(greaterThan.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.GreaterThan(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(GreaterOrEqualThan))
            {
                GreaterOrEqualThan greaterOrEqualThan = (GreaterOrEqualThan)operation;
                Expression argument1 =
                    GenerateMethodBody(greaterOrEqualThan.Argument1, contextParameter, functionRegistry);
                Expression argument2 =
                    GenerateMethodBody(greaterOrEqualThan.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.GreaterThanOrEqual(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(Equal))
            {
                Equal equal = (Equal)operation;
                Expression argument1 = GenerateMethodBody(equal.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(equal.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.Equal(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(NotEqual))
            {
                NotEqual notEqual = (NotEqual)operation;
                Expression argument1 = GenerateMethodBody(notEqual.Argument1, contextParameter, functionRegistry);
                Expression argument2 = GenerateMethodBody(notEqual.Argument2, contextParameter, functionRegistry);

                return Expression.Condition(Expression.NotEqual(argument1, argument2),
                    Expression.Constant(1.0),
                    Expression.Constant(0.0));
            }

            if (operation.GetType() == typeof(Function))
            {
                Function function = (Function)operation;

                FunctionInfo functionInfo = functionRegistry.GetFunctionInfo(function.FunctionName);
                Type funcType;
                Type[] parameterTypes;
                Expression[] arguments;

                if (functionInfo.IsDynamicFunc)
                {
                    funcType = typeof(DynamicFunc<double, double>);
                    parameterTypes = new[] { typeof(double[]) };


                    Expression[] arrayArguments = new Expression[function.Arguments.Count];
                    for (int i = 0; i < function.Arguments.Count; i++)
                        arrayArguments[i] =
                            GenerateMethodBody(function.Arguments[i], contextParameter, functionRegistry);

                    arguments = new Expression[1];
                    arguments[0] = Expression.NewArrayInit(typeof(double), arrayArguments);
                }
                else
                {
                    funcType = GetFuncType(functionInfo.NumberOfParameters);
                    parameterTypes = (from i in Enumerable.Range(0, functionInfo.NumberOfParameters)
                        select typeof(double)).ToArray();

                    arguments = new Expression[functionInfo.NumberOfParameters];
                    for (int i = 0; i < functionInfo.NumberOfParameters; i++)
                        arguments[i] = GenerateMethodBody(function.Arguments[i], contextParameter, functionRegistry);
                }

                Expression getFunctionRegistry = Expression.Property(contextParameter, "FunctionRegistry");

                Expression funcInstance = Expression.Convert(
                    Expression.Property(
                        Expression.Call(
                            getFunctionRegistry,
                            typeof(IFunctionRegistry).GetRuntimeMethod("GetFunctionInfo", new[] { typeof(string) }),
                            Expression.Constant(function.FunctionName)),
                        "Function"),
                    funcType);

                return Expression.Call(
                    funcInstance,
                    funcType.GetRuntimeMethod("Invoke", parameterTypes),
                    arguments);
            }

            throw new ArgumentException(
                $"Unsupported operation \"{operation.GetType().FullName}\".", nameof(operation));
        }

        private Type GetFuncType(int numberOfParameters)
        {
            var funcTypeName = numberOfParameters < 9
                ? $"System.Func`{numberOfParameters + 1}"
                : $"System.Func`{numberOfParameters + 1}, {funcAssemblyQualifiedName}";

            Type funcType = Type.GetType(funcTypeName);
            if (funcType == null)
            {
                throw new InvalidOperationException($"Couldn't get type of ${funcTypeName}.");
            }

            var typeArguments = new Type[numberOfParameters + 1];
            for (var i = 0; i < typeArguments.Length; i++)
                typeArguments[i] = typeof(double);

            return funcType.MakeGenericType(typeArguments);
        }
        
        private static class PrecompiledMethods
        {
            public static double GetVariableValueOrThrow(string variableName, FormulaContext context)
            {
                //if (context.Variables.TryGetValue(context.FunctionRegistry.caseSensitive() ? variableName : variableName.ToLowerFast(), out double result))
                if (context.Variables.TryGetValue(variableName, out double result))
                    return result;
                else if (context.ConstantRegistry.IsConstantName(variableName))
                    return context.ConstantRegistry.GetConstantInfo(variableName).Value;
                else
                    throw new VariableNotDefinedException($"The variable \"{variableName}\" used is not defined.", variableName);
            }
        }
    }
}