using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpMutation
{
    class SyntaxFixer : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            IfStatementSyntax newNode = (IfStatementSyntax) base.VisitIfStatement(node);
            if (!(newNode.Statement is BlockSyntax))
            {
                BlockSyntax block = SyntaxFactory.Block(newNode.Statement);
                newNode = node.WithStatement(block);
            }

            return newNode;
        }
        
        // TODO: what other one-line statements might need their curly braces added? for? while?

        // Open classes/methods up for using in an unsigned environment.
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var original = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            var newModifiers = RemoveInternalSealedModifiers(original.Modifiers);

            return original.WithModifiers(newModifiers);
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            MethodDeclarationSyntax original = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            var newModifiers = RemoveInternalSealedModifiers(original.Modifiers);

            return original.WithModifiers(newModifiers);
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            PropertyDeclarationSyntax original = (PropertyDeclarationSyntax) base.VisitPropertyDeclaration(node);
            var newModifiers = RemoveInternalSealedModifiers(original.Modifiers);

            return original.WithModifiers(newModifiers);
        }

        private static SyntaxTokenList RemoveInternalSealedModifiers(SyntaxTokenList originalModifiers)
        {
            SyntaxTokenList newModifiers = originalModifiers;
            foreach (SyntaxToken modifier in originalModifiers)
            {
                if (modifier.Text.Contains("sealed"))
                {
                    newModifiers = newModifiers.Remove(modifier);
                }
                else if (modifier.Text.Contains("internal"))
                {
                    newModifiers = newModifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                }
            }
            return newModifiers;
        }
    }
}
