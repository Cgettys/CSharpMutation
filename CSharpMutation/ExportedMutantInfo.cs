using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace CSharpMutation
{
    public class ExportedMutantInfo
    {
        public int lineID;
        public string original;
        public string mutant;
        public int lineNumber;
        public int column;
        public string fileName;
        public string mutantDesc;

        internal ExportedMutantInfo(MutantInfo info)
        {
            this.lineID = info.lineID;
            this.original = info.original.ToString();
            this.mutant = info.mutant.ToString();
            this.lineNumber = info.original.GetLocation().GetLineSpan().StartLinePosition.Line;
            this.fileName = info.original.GetLocation().SourceTree.FilePath;
            this.column = info.original.GetLocation().GetLineSpan().StartLinePosition.Character;
            this.mutantDesc = info.mutantDesc;
        }

        public override string ToString()
        {
            return fileName + ":" + lineNumber + " " + mutantDesc+ "\r\n"
                   +"    Original: "+ original + "\r\n"
                   + "    Mutant: "+ mutant + "\r\n";
        }
    }
}
