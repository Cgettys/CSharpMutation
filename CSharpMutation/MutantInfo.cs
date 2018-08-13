using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace CSharpMutation
{
    public class MutantInfo
    {
        public int lineID;
        public SyntaxNode original;
        public SyntaxNode mutant;
        public int lineNumber;
        public int column;
        public string fileName;
        public string mutantDesc;

        public MutantInfo(int lineID, SyntaxNode original, SyntaxNode mutant, string mutantDesc)
        {
            this.lineID = lineID;
            this.original = original;
            this.mutant = mutant;
            this.lineNumber = original.GetLocation().GetLineSpan().StartLinePosition.Line;
            this.fileName = original.GetLocation().SourceTree.FilePath;
            this.column = original.GetLocation().GetLineSpan().StartLinePosition.Character;
            this.mutantDesc = mutantDesc;
        }

        public override string ToString()
        {
            return fileName + ":" + lineNumber + " " + mutantDesc+ "\r\n"
                   +"    Original: "+ original + "\r\n"
                   + "    Mutant: "+ mutant + "\r\n";
        }
    }
}
