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
    public class BaseMutatorImpl : CSharpSyntaxVisitor<IEnumerable<MutantInfo>>, MutantEnumerator
    {
        private CSharpCompilation _compiler;
        private Dictionary<SyntaxKind, IEnumerable<SyntaxKind>> _kindsToMutate;
        private int lineID;

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
        public IEnumerable<MutantInfo> GetMutants(SyntaxNode node, int lineID)
        {
            this.lineID = lineID;
            var result = Visit(node);
            foreach (var mutantInfo in result)
            {
                yield return mutantInfo;
            }
        }

        public override IEnumerable<MutantInfo> Visit(SyntaxNode node)
        {

            // delete first - no point in mutating if statement is ignored!
            if (node is StatementSyntax)
            {
                foreach (var mutantInfo1 in DeleteStatement(node)) yield return mutantInfo1;
            }
            
            // if statements are instrumented in the condition itself, not before the if
            if (node is ExpressionSyntax && node.Parent is IfStatementSyntax)
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
            IEnumerable<MutantInfo> baseResult = base.Visit(node);

            if (baseResult != null)
            {
                foreach (var mutantInfo in baseResult)
                {
                    yield return mutantInfo;
                }
            }

        }

        private IEnumerable<MutantInfo> DeleteStatement(SyntaxNode node)
        {

            var _semanticModel = _compiler.GetSemanticModel(node.SyntaxTree);
            // Note: Roslyn does not seem to like empty block statements.
            StatementSyntax nop = SyntaxFactory.ParseStatement(";");
            DataFlowAnalysis dataFlow = _semanticModel.AnalyzeDataFlow(node);
            ControlFlowAnalysis controlFlow = _semanticModel.AnalyzeControlFlow(node);
            // statements with only side effects and no returns or exceptions trivially can be erased
            if (!controlFlow.ExitPoints.Any() && controlFlow.EndPointIsReachable)
            {
                if (!dataFlow.VariablesDeclared.Any() || node is BlockSyntax) // || !dataFlow.ReadOutside.Any())
                {
                    yield return new MutantInfo(lineID, node, nop, "Deleted statement executed by test");
                }
            }

            if (node is ForStatementSyntax && controlFlow.EndPointIsReachable)
            {
                yield return new MutantInfo(lineID, node, nop, "Deleted entire for loop");
            }
        }

        public override IEnumerable<MutantInfo> VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            foreach (var variableMutantStreams in node.Declaration.Variables.Where(v => v.Initializer != null).Select(v => v.Initializer.Value).Select(expr => GenerateExpressionMutants(expr, lineID)))
            {
                foreach (var mutantInfo in variableMutantStreams)
                {
                    yield return mutantInfo;
                }
            }
        }

        public override IEnumerable<MutantInfo> VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression != null)
            {
                return GenerateExpressionMutants(node.Expression, lineID);
            }
            return new List<MutantInfo>();
        }

        public override IEnumerable<MutantInfo> VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            return GenerateExpressionMutants(node.Expression, lineID);
        }

        public override IEnumerable<MutantInfo> VisitBinaryExpression(BinaryExpressionSyntax expressionSyntax)
        {
            var _semanticModel = _compiler.GetSemanticModel(expressionSyntax.SyntaxTree);
            ITypeSymbol type = _semanticModel.GetTypeInfo(expressionSyntax).ConvertedType;

            if (IsMathy(type))
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
        }

        public override IEnumerable<MutantInfo> VisitConditionalExpression(ConditionalExpressionSyntax ternary)
        {
            yield return
                new MutantInfo(lineID, ternary, ternary.WhenTrue,
                    "Replaced tenary operator with true condition");

            yield return
                new MutantInfo(lineID, ternary, ternary.WhenFalse,
                    "Replaced tenary operator with false condition");
        }

        public override IEnumerable<MutantInfo> VisitAssignmentExpression(AssignmentExpressionSyntax expressionSyntax)
        {
            // only mutate right side
            return GenerateExpressionMutants(expressionSyntax.Right, lineID);
        }

        public override IEnumerable<MutantInfo> VisitMemberAccessExpression(MemberAccessExpressionSyntax expressionSyntax)
        {
            return ConvertToConstant(expressionSyntax);
        }

        public override IEnumerable<MutantInfo> VisitLiteralExpression(LiteralExpressionSyntax expressionSyntax)
        {
            return ConvertToConstant(expressionSyntax);
        }

        private IEnumerable<MutantInfo> ConvertToConstant(ExpressionSyntax expressionSyntax)
        {
            var _semanticModel = _compiler.GetSemanticModel(expressionSyntax.SyntaxTree);
            ITypeSymbol type = _semanticModel.GetTypeInfo(expressionSyntax).ConvertedType;

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
                // FIXME: append appropriate base to constants, ie 0m, 0d, 0f, ...
                // .NET is very sensitive to this issue.
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
        }

        private IEnumerable<MutantInfo> GenerateExpressionMutants(ExpressionSyntax expressionSyntax, int lineID)
        {
            var baseResult = base.Visit(expressionSyntax);
            if (baseResult != null)
            {
                foreach (var mutantInfo in baseResult)
                {
                    yield return mutantInfo;
                }
            }

            if (!(expressionSyntax is AssignmentExpressionSyntax) && !(expressionSyntax is LambdaExpressionSyntax))
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
