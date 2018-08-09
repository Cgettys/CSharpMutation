using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpMutation
{
    public class BaseMutatorImpl
    {
        private CSharpCompilation _compiler;
        private Dictionary<SyntaxKind, IEnumerable<SyntaxKind>> _kindsToMutate;

        public BaseMutatorImpl(CSharpCompilation compiler)
        {
            _compiler = compiler;
            // Stryker's most excellent binary op taxonomy
            // https://github.com/stryker-mutator/stryker-net/blob/master/src/Stryker.Core/Stryker.Core/Mutators/BinaryExpressionMutator.cs
            // TODO: you could create a mutant schemata for binary mutants in Interops that you compile only once.
            // You could generate code that asks the MSTestRunner globally for the current "dynamic mutant state."
            // This would still require blowing away the AppDomain and recreating the MSTestRunner in the domain.
            // So leftArg && rightArg becomes Interops.DynamicMutants.MutateAnd(leftArg, rightArg, position).

            // ERROR: this mutation model assumes that operators are completely defined for all types!
            _kindsToMutate = new Dictionary<SyntaxKind, IEnumerable<SyntaxKind>>
            {
                {SyntaxKind.SubtractExpression, new List<SyntaxKind> { SyntaxKind.AddExpression } },
                {SyntaxKind.AddExpression, new List<SyntaxKind> {SyntaxKind.SubtractExpression } },
                {SyntaxKind.MultiplyExpression, new List<SyntaxKind> {SyntaxKind.DivideExpression } },
                {SyntaxKind.DivideExpression, new List<SyntaxKind> {SyntaxKind.MultiplyExpression } },
                {SyntaxKind.ModuloExpression, new List<SyntaxKind> {SyntaxKind.MultiplyExpression } },
                {SyntaxKind.GreaterThanExpression, new List<SyntaxKind> {SyntaxKind.LessThanExpression, SyntaxKind.GreaterThanOrEqualExpression } },
                {SyntaxKind.LessThanExpression, new List<SyntaxKind> {SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanOrEqualExpression } },
                {SyntaxKind.GreaterThanOrEqualExpression, new List<SyntaxKind> { SyntaxKind.LessThanExpression, SyntaxKind.GreaterThanExpression } },
                {SyntaxKind.LessThanOrEqualExpression, new List<SyntaxKind> { SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression } },
                {SyntaxKind.EqualsExpression, new List<SyntaxKind> {SyntaxKind.NotEqualsExpression } },
                {SyntaxKind.NotEqualsExpression, new List<SyntaxKind> {SyntaxKind.EqualsExpression } },
                {SyntaxKind.LogicalAndExpression, new List<SyntaxKind> {SyntaxKind.LogicalOrExpression } },
                {SyntaxKind.LogicalOrExpression, new List<SyntaxKind> {SyntaxKind.LogicalAndExpression } },
            };
        }

        // FIXME: break this huge chain up into separate Decorators
        internal IEnumerable<MutantInfo> GetMutants(SyntaxNode node, int lineID)
        {
            var _semanticModel = _compiler.GetSemanticModel(node.SyntaxTree);

            // delete first - no point in mutating if statement is ignored!
            if (node is StatementSyntax)
            {
                // Note: Roslyn does not seem to like empty block statements.
                StatementSyntax nop = SyntaxFactory.ParseStatement(";");
                DataFlowAnalysis dataFlow = _semanticModel.AnalyzeDataFlow(node);
                ControlFlowAnalysis controlFlow = _semanticModel.AnalyzeControlFlow(node);
                // statements with only side effects and no returns or exceptions trivially can be erased
                if (!controlFlow.ExitPoints.Any() && controlFlow.EndPointIsReachable)
                {
                    if (!dataFlow.VariablesDeclared.Any() || node is BlockSyntax)// || !dataFlow.ReadOutside.Any())
                    {
                        yield return new MutantInfo(lineID, node, nop, "Deleted statement executed by test");
                    }

                }

                if (node is ForStatementSyntax && controlFlow.EndPointIsReachable)
                {
                    yield return new MutantInfo(lineID, node, nop, "Deleted entire for loop");
                }
            }
            if ((node as ReturnStatementSyntax)?.Expression != null)
            {
                foreach (var mutantInfo in GenerateExpressionMutants(((ReturnStatementSyntax)node).Expression, lineID))
                {
                    yield return mutantInfo;
                }
            }
            if (node is ExpressionSyntax) // NOTE: likely a covered expression in an if statement
            {
                if (node.Parent is IfStatementSyntax)
                {
                    // should always be boolean
                    ExpressionSyntax newCondition = SyntaxFactory.ParseExpression("true");
                    yield return new MutantInfo(lineID, node, newCondition, "Replaced if condition with true");

                    newCondition = SyntaxFactory.ParseExpression("false");
                    yield return new MutantInfo(lineID, node, newCondition, "Replaced if condition with false");


                    // mutate if condition itself
                    foreach (var mutantInfo in GenerateExpressionMutants((ExpressionSyntax)node, lineID))
                    {
                        yield return mutantInfo;
                    }
                }

                //else if (node is IdentifierNameSyntax)
                //{
                //    IdentifierNameSyntax identifier = (IdentifierNameSyntax) node;
                //    SemanticModel _model = _compiler.GetSemanticModel(node.SyntaxTree);
                //    var symbolInfo = _model.GetSymbolInfo(identifier);
                //    if (symbolInfo.Symbol is ILocalSymbol)
                //    {
                //        if (((ILocalSymbol) symbolInfo.Symbol).Type.IsReferenceType)
                //        {
                //            yield return SyntaxFactory.ParseExpression("null");
                //        }
                //    }
                //}
            }

            if (node is LocalDeclarationStatementSyntax)
            {
                // one set of mutants per variable defined on this line
                foreach (var variableMutantStreams in ((LocalDeclarationStatementSyntax) node).Declaration.Variables.Select(v => v.Initializer.Value).Select(expr => GenerateExpressionMutants(expr, lineID)))
                {
                    foreach (var mutantInfo in variableMutantStreams)
                    {
                        yield return mutantInfo;
                    }
                }
            }

            if (node is ExpressionStatementSyntax)
            {
                foreach (var mutantInfo in GenerateExpressionMutants(((ExpressionStatementSyntax)node).Expression, lineID))
                {
                    yield return mutantInfo;
                }
            }

        }

        private IEnumerable<MutantInfo> GenerateExpressionMutants(ExpressionSyntax expressionSyntax, int lineID)
        {
            var _semanticModel = _compiler.GetSemanticModel(expressionSyntax.SyntaxTree);
            ITypeSymbol type = _semanticModel.GetTypeInfo(expressionSyntax).ConvertedType;
            if (expressionSyntax is BinaryExpressionSyntax && IsMathy(type))
            {
                BinaryExpressionSyntax binaryNode = (BinaryExpressionSyntax)expressionSyntax;
                // have to be more careful here - is it a comparison operator, logical operator, or something else?
                if (_kindsToMutate.ContainsKey(binaryNode.Kind()))
                {
                    foreach (SyntaxKind replacement in _kindsToMutate[binaryNode.Kind()])
                    {
                        yield return new MutantInfo(lineID, expressionSyntax, SyntaxFactory.BinaryExpression(replacement, binaryNode.Left, binaryNode.Right), "Replaced binary operator condition with " + replacement);
                    }
                }
                else
                {
                    Console.WriteLine("Unknown binary operator: " + binaryNode.Kind());
                }
            }
            
            // FIXME: these will NEVER execute because they are not instrumented!
            if (expressionSyntax is ConditionalExpressionSyntax)
            {
                ConditionalExpressionSyntax ternary = (ConditionalExpressionSyntax)expressionSyntax;
                yield return
                    new MutantInfo(lineID, expressionSyntax, ternary.WhenTrue,
                        "Replaced tenary operator with true condition");

                yield return
                    new MutantInfo(lineID, expressionSyntax, ternary.WhenFalse,
                        "Replaced tenary operator with false condition");
            }
            if (expressionSyntax.Kind() == SyntaxKind.StringLiteralExpression && expressionSyntax.ToString() != "\"\""
                || expressionSyntax is MemberAccessExpressionSyntax && type != null && type.SpecialType == SpecialType.System_String)
            {
                yield return
                    new MutantInfo(lineID, expressionSyntax,
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression)
                            .WithToken(SyntaxFactory.Literal("")), "Emptied string");
            }
            if (expressionSyntax.Kind() == SyntaxKind.NumericLiteralExpression
                || expressionSyntax is MemberAccessExpressionSyntax && IsNumeric(type))
            {
                if (expressionSyntax.ToString() == "0")
                {
                    yield return
                        new MutantInfo(lineID, expressionSyntax,
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression)
                                .WithToken(SyntaxFactory.Literal("1", 1)), "Replaced 0 with 1");
                }
                else
                {
                    yield return
                        new MutantInfo(lineID, expressionSyntax,
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression)
                                .WithToken(SyntaxFactory.Literal("0", 0)),
                            "Replaced " + expressionSyntax.ToString() + " with 0");
                }
                
            }

            // manually descend tree because they will not have coverage
            // might not be true of lambda expressions though
            if (expressionSyntax is AssignmentExpressionSyntax)
            {
                // only mutate right side
                foreach (var generateExpressionMutant in GenerateExpressionMutants(((AssignmentExpressionSyntax)expressionSyntax).Right, lineID))
                {
                    yield return generateExpressionMutant;
                }
            }
            else if (! (expressionSyntax is LambdaExpressionSyntax))
            {
                foreach (var syntax in expressionSyntax.ChildNodes().OfType<ExpressionSyntax>())
                {
                    foreach (var generateExpressionMutant in GenerateExpressionMutants(syntax, lineID))
                    {
                        yield return generateExpressionMutant;
                    }
                }
            }
        }

        internal bool IsMathy(ITypeSymbol type)
        {
            return GetTypedConstantKind(type, _compiler) == TypedConstantKind.Primitive
                   && type.SpecialType != SpecialType.System_String
                   && type.SpecialType != SpecialType.System_Object;
        }

        internal bool IsNumeric(ITypeSymbol type)
        {
            return IsMathy(type)
                && type.SpecialType != SpecialType.System_Boolean;
        }

        // http://source.roslyn.io/#Microsoft.CodeAnalysis/Symbols/TypedConstant.cs,5aa4528d694ad774,references
        internal static TypedConstantKind GetTypedConstantKind(ITypeSymbol type, Compilation compilation)
        {
            if (type == null)
            {
                return TypedConstantKind.Error;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Char:
                case SpecialType.System_String:
                case SpecialType.System_Object:
                    return TypedConstantKind.Primitive;
                default:
                    switch (type.TypeKind)
                    {
                        case TypeKind.Array:
                            return TypedConstantKind.Array;
                        case TypeKind.Enum:
                            return TypedConstantKind.Enum;
                        case TypeKind.Error:
                            return TypedConstantKind.Error;
                    }

                    //if (compilation != null &&
                    //    compilation.IsSystemTypeReference(type))
                    //{
                    //    return TypedConstantKind.Type;
                    //}

                    return TypedConstantKind.Error;
            }
        }
    }
}
