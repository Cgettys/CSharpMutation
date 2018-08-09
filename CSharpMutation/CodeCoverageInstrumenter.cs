using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using Interops;
using Microsoft.CodeAnalysis.Editing;

namespace CSharpMutation
{
    public class CodeCoverageInstrumenter : CSharpSyntaxWalker
    {
        private readonly SyntaxEditor _editor;
        private const string REPLACE_SIZE = "replacesize";
        private readonly CoverageData _coverageData = CoverageData.GetInstance();

        public CodeCoverageInstrumenter(SyntaxEditor editor)
        {
            _editor = editor;
        }
        
        internal void InstrumentStatement(SyntaxNode statement)
        {
            StatementSyntax increment = SyntaxFactory.ParseStatement("Interops.CoverageData.GetInstance().IncrementLineCount(" + _coverageData.GetLineID(statement.GetLocation().ToString()) + ");");
            _editor.InsertBefore(statement, new SyntaxNode[] {increment});
        }

        internal ExpressionSyntax InstrumentCondition(ExpressionSyntax condition)
        {
            return SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression,
                    SyntaxFactory.ParseExpression("(Interops.CoverageData.GetInstance().IncrementLineCount(" +
                                                  _coverageData.GetLineID(condition.GetLocation().ToString()) +
                                                  ")) != 0"), condition);
        }

        #region VisitStatement

        // FIXME: a dynamic proxy might be better than all this selective copypasta
        public override void VisitContinueStatement(ContinueStatementSyntax node)
        {
            base.VisitContinueStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            base.VisitExpressionStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            base.VisitForStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            base.VisitForEachStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            base.VisitIfStatement(node);
            
            IfStatementSyntax newNode = node;
            SyntaxNode condition = newNode.Condition;

            //Console.WriteLine(condition.GetText());
            _editor.ReplaceNode(condition, InstrumentCondition(newNode.Condition));
            //Console.WriteLine(_editor.GetChangedRoot().GetText());

        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            base.VisitLocalDeclarationStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            base.VisitReturnStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            base.VisitWhileStatement(node);
            InstrumentStatement(node);
        }

        public override void VisitThrowStatement(ThrowStatementSyntax node)
        {
            base.VisitThrowStatement(node);
            InstrumentStatement(node);
        }

        #endregion

    }
}