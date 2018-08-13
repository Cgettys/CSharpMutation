using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace CSharpMutation
{
    public class MutantChain : MutantEnumerator
    {
        private readonly MutantEnumerator[] _parents;
        public MutantChain(params MutantEnumerator[] parents)
        {
            _parents = parents;
        }
        public virtual IEnumerable<MutantInfo> GetMutants(SyntaxNode node, int lineID)
        {
            foreach (var mutantEnumerator in _parents)
            {
                foreach (var mutantInfo in mutantEnumerator.GetMutants(node, lineID))
                {
                    yield return mutantInfo;
                }
            }
        }
    }
}
