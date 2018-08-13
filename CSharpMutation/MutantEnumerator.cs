using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace CSharpMutation
{
    public interface MutantEnumerator
    {
        IEnumerable<MutantInfo> GetMutants(SyntaxNode node, int lineID);
    }
}
