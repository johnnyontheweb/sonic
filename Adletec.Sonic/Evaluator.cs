﻿using System;
using System.Collections.Generic;
using System.Linq;
using Adletec.Sonic.Execution;
using Adletec.Sonic.Operations;
using Adletec.Sonic.Parsing;
using Adletec.Sonic.Parsing.Tokenizing;
using Adletec.Sonic.Util;

namespace Adletec.Sonic
{
    public delegate TResult DynamicFunc<T, TResult>(params T[] values);

    /// <inheritdoc/>
    public class Evaluator : IEvaluator
    {
        private readonly TokenReader tokenReader;
        private readonly Optimizer optimizer;
        private readonly IExecutor executor;
        private readonly Validator validator;
        
        private readonly MemoryCache<string, Func<IDictionary<string, double>, double>> executionFormulaCache;
        private readonly bool cacheEnabled;
        private readonly bool optimizerEnabled;
        private readonly bool guardedModeEnabled;
        private readonly bool validationEnabled;

        private readonly Random random;

        /// <summary>
        /// Create a new instance of the evaluator with default settings.
        /// For more control over the evaluator use the Create() method.
        /// </summary>
        /// <returns></returns>
        public static Evaluator CreateWithDefaults()
        {
            return new EvaluatorBuilder().Build();
        }

        /// <summary>
        /// Create a new builder instance for the evaluator. The builder can be
        /// used to configure the evaluator, and add custom functions and constants.
        /// </summary>
        /// <returns></returns>
        public static EvaluatorBuilder Create()
        {
            return new EvaluatorBuilder();
        }

        internal Evaluator(EvaluatorBuilder options)
        {
            var caseSensitive = options.CaseSensitive;
            this.executionFormulaCache =
                new MemoryCache<string, Func<IDictionary<string, double>, double>>(options.CacheMaximumSize,
                    options.CacheReductionSize);
            this.FunctionRegistry = new FunctionRegistry(caseSensitive, options.GuardedModeEnabled);
            this.ConstantRegistry = new ConstantRegistry(caseSensitive, options.GuardedModeEnabled);
            
            var cultureInfo = options.CultureInfo;
            var argumentSeparator = options.ArgumentSeparator;
            tokenReader = new TokenReader(cultureInfo, argumentSeparator);
            
            this.cacheEnabled = options.CacheEnabled;
            this.optimizerEnabled = options.OptimizerEnabled;
            this.guardedModeEnabled = options.GuardedModeEnabled;
            this.validationEnabled = options.ValidationEnabled;

            this.random = new Random();


            switch (options.ExecutionMode)
            {
                case ExecutionMode.Interpreted:
                    executor = new Interpreter(caseSensitive, guardedModeEnabled);
                    break;
                case ExecutionMode.Compiled:
                    executor = new DynamicCompiler(caseSensitive, guardedModeEnabled);
                    break;
                default:
                    throw new ArgumentException($"Unsupported execution mode \"{options.ExecutionMode}\".",
                        nameof(options.ExecutionMode));
            }

            optimizer = new Optimizer(new Interpreter()); // We run the optimizer with the interpreter 

            // Register the default constants of sonic into the constant registry
            if (options.DefaultConstants)
                RegisterDefaultConstants();

            // Register the default functions of sonic into the function registry
            if (options.DefaultFunctions)
                RegisterDefaultFunctions();

            // Register the user defined constants
            if (options.Constants != null)
            {
                foreach (var constant in options.Constants)
                {
                    if (guardedModeEnabled && FunctionRegistry.IsFunctionName(constant.Name))
                    {
                        throw new ArgumentException("The constant name cannot be the same as a function name.");
                    }

                    ConstantRegistry.RegisterConstant(constant.Name, constant.Value);
                }
            }

            // Register the user defined functions
            if (options.Functions != null)
            {
                foreach (var function in options.Functions)
                {
                    if (guardedModeEnabled && ConstantRegistry.IsConstantName(function.Name))
                    {
                        throw new ArgumentException("The function name cannot be the same as a constant name.");
                    }

                    FunctionRegistry.RegisterFunction(function.Name, function.Function, function.IsIdempotent);
                }
            }
            
            this.validator = new Validator(FunctionRegistry, cultureInfo);
        }

        internal IFunctionRegistry FunctionRegistry { get; }

        internal IConstantRegistry ConstantRegistry { get; }

        public IEnumerable<FunctionInfo> Functions => FunctionRegistry;

        public IEnumerable<ConstantInfo> Constants => ConstantRegistry;

        public double Evaluate(string expression)
        {
            return Evaluate(expression, new Dictionary<string, double>());
        }

        public double Evaluate(string expression, IDictionary<string, double> variables)
        {
            var function = CreateDelegate(expression);
            return function(variables);
        }

        public Func<IDictionary<string, double>, double> CreateDelegate(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                throw new ArgumentNullException(nameof(expression));

            if (IsInFormulaCache(expression, out var result))
            {
                return result;
            }

            Operation operation = BuildAbstractSyntaxTree(expression, ConstantRegistry, optimizerEnabled, validationEnabled);
            return BuildEvaluator(expression, operation);
        }

        /// <summary>
        /// Validates the given expression. If the expression is invalid, a matching subtype of ParseException is thrown.
        /// </summary>
        /// <param name="expression">The expression to check.</param>
        public void Validate(string expression)
        {
            BuildAbstractSyntaxTree(expression, ConstantRegistry, false, true);
        }

        private void RegisterDefaultFunctions()
        {
            FunctionRegistry.RegisterFunction("sin", (Func<double, double>)Math.Sin, true);
            FunctionRegistry.RegisterFunction("cos", (Func<double, double>)Math.Cos, true);
            FunctionRegistry.RegisterFunction("csc", (Func<double, double>)MathUtil.Csc, true);
            FunctionRegistry.RegisterFunction("sec", (Func<double, double>)MathUtil.Sec, true);
            FunctionRegistry.RegisterFunction("asin", (Func<double, double>)Math.Asin, true);
            FunctionRegistry.RegisterFunction("acos", (Func<double, double>)Math.Acos, true);
            FunctionRegistry.RegisterFunction("tan", (Func<double, double>)Math.Tan, true);
            FunctionRegistry.RegisterFunction("cot", (Func<double, double>)MathUtil.Cot, true);
            FunctionRegistry.RegisterFunction("atan", (Func<double, double>)Math.Atan, true);
            FunctionRegistry.RegisterFunction("acot", (Func<double, double>)MathUtil.Acot, true);
            FunctionRegistry.RegisterFunction("loge", (Func<double, double>)Math.Log, true);
            FunctionRegistry.RegisterFunction("log10", (Func<double, double>)Math.Log10, true);
            FunctionRegistry.RegisterFunction("logn", (Func<double, double, double>)Math.Log, true);
            FunctionRegistry.RegisterFunction("sqrt", (Func<double, double>)Math.Sqrt, true);
            FunctionRegistry.RegisterFunction("abs", (Func<double, double>)Math.Abs, true);
            FunctionRegistry.RegisterFunction("if",
                (Func<double, double, double, double>)((a, b, c) => a != 0.0 ? b : c), true);
            FunctionRegistry.RegisterFunction("ifless",
                (Func<double, double, double, double, double>)((a, b, c, d) => a < b ? c : d), true);
            FunctionRegistry.RegisterFunction("ifmore",
                (Func<double, double, double, double, double>)((a, b, c, d) => a > b ? c : d), true);
            FunctionRegistry.RegisterFunction("ifequal",
                (Func<double, double, double, double, double>)((a, b, c, d) => a == b ? c : d), true);
            FunctionRegistry.RegisterFunction("ceiling", (Func<double, double>)Math.Ceiling, true);
            FunctionRegistry.RegisterFunction("floor", (Func<double, double>)Math.Floor, true);
            FunctionRegistry.RegisterFunction("truncate", (Func<double, double>)Math.Truncate, true);
            FunctionRegistry.RegisterFunction("round", (Func<double, double>)Math.Round, true);

            // Dynamic based arguments Functions
            FunctionRegistry.RegisterFunction("max", (DynamicFunc<double, double>)(a => a.Max()), true);
            FunctionRegistry.RegisterFunction("min", (DynamicFunc<double, double>)(a => a.Min()), true);
            FunctionRegistry.RegisterFunction("avg", (DynamicFunc<double, double>)(a => a.Average()), true);
            FunctionRegistry.RegisterFunction("median", (DynamicFunc<double, double>)(a => a.Median()), true);
            FunctionRegistry.RegisterFunction("sum", (DynamicFunc<double, double>)(a => a.Sum()), true);

            // Non Idempotent Functions
            FunctionRegistry.RegisterFunction("random", (Func<double>)random.NextDouble, false);
        }

        private void RegisterDefaultConstants()
        {
            ConstantRegistry.RegisterConstant("e", Math.E);
            ConstantRegistry.RegisterConstant("pi", Math.PI);
        }

        /// <summary>
        /// Build the abstract syntax tree for a given formula. The formula string will
        /// be first tokenized.
        /// </summary>
        /// <param name="expression">A string containing the mathematical expression to be parsed.</param>
        /// <param name="compiledConstants">The constants which are to be available in the given formula.</param>
        /// <param name="optimize">If the abstract syntax tree should be optimized.</param>
        /// <param name="validate">If the expression should be checked for syntax errors.</param>
        /// <returns>The abstract syntax tree of the expression.</returns>
        private Operation BuildAbstractSyntaxTree(string expression, IConstantRegistry compiledConstants, bool optimize, bool validate)
        {
            List<Token> tokens = tokenReader.Read(expression);
            if (validate)
            {
                validator.Validate(tokens, expression);
            }
            
            var astBuilder = new AstBuilder(FunctionRegistry, compiledConstants);
            Operation operation = astBuilder.Build(tokens);

            return optimize
                ? optimizer.Optimize(operation, this.FunctionRegistry, this.ConstantRegistry)
                : operation;
        }
        
        private Func<IDictionary<string, double>, double> BuildEvaluator(string formulaText, Operation operation)
        {
            return cacheEnabled ? executionFormulaCache.GetOrAdd(formulaText, Evaluator) : Evaluator(formulaText);

            // can be external function
            Func<IDictionary<string, double>, double> Evaluator(string s)
            {
                // If the operation is a constant, we can just return the constant value
                if (operation is Constant<double> constant)
                {
                    if (guardedModeEnabled)
                    {
                        return values =>
                        {
                            VariableVerifier.VerifyVariableNames(values, ConstantRegistry, FunctionRegistry);
                            return constant.Value;
                        };
                    }

                    return _ => constant.Value;
                }

                return executor.BuildFormula(operation, this.FunctionRegistry, this.ConstantRegistry);
            }
        }

        private bool IsInFormulaCache(string formulaText, out Func<IDictionary<string, double>, double> function)
        {
            function = null;
            return cacheEnabled && executionFormulaCache.TryGetValue(formulaText, out function);
        }
    }
}