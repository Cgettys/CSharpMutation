using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Interops;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpMutation
{
    class MutatingWalker : CSharpSyntaxWalker
    {
        public delegate Boolean OnMutant(MutantInfo info);

        private BaseMutatorImpl _mutator;
        private OnMutant _onMutant;
        private CoverageData _coverage;
        private readonly Dictionary<int, List<String>> _testCaseCoverageByLineID;
        
        public MutatingWalker(BaseMutatorImpl mutator, OnMutant callback, CoverageData coverage, Dictionary<int, List<String>> testCaseCoverageByLineID)
        {
            _mutator = mutator;
            _onMutant = callback;
            _coverage = coverage;
            _testCaseCoverageByLineID = testCaseCoverageByLineID;
        }

        //public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        //{
        //    // you can trivially visit each member of a class in parallel
        //    node.Members.AsParallel().WithDegreeOfParallelism(2).ForAll(Visit);
        //}

        public override void VisitBlock(BlockSyntax node)
        {
            // you can trivially mutate every statement in parallel
            // TODO: try a binary search type approach to deletion.
            //node.Statements.AsParallel().WithDegreeOfParallelism(8).ForAll(Visit);
            foreach (var statementSyntax in node.Statements)
            {
                Visit(statementSyntax);
            }
        }

        public override void Visit(SyntaxNode node)
        {
            bool _mutantLives = false;
            
            String location = node.GetLocation().ToString();
            if (_coverage.LineLocatorIDs.ContainsKey(location))
            {
                int id = _coverage.LineLocatorIDs[location];
                if (_testCaseCoverageByLineID.ContainsKey(id))
                {
                    // little point in parallelizing here because the first mutant is the biggest possible (delete entire statement)
                    foreach (MutantInfo mutant in _mutator.GetMutants(node, id))
                    {
                        _mutantLives |= _onMutant(mutant);
                        if (_mutantLives) return;
                    }
                }
            }

            base.Visit(node);
        }

        private SyntaxNode getCoveringNode(SyntaxNode node)
        {
            String location = node.GetLocation().ToString();
            
            if (_coverage.LineLocatorIDs.ContainsKey(location)) return node;
            return null;
        }
    }
}
