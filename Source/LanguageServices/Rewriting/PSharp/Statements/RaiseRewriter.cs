﻿// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt in the repo root for full license information.
// ------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.PSharp.LanguageServices.Rewriting.PSharp
{
    /// <summary>
    /// The raise statement rewriter.
    /// </summary>
    internal sealed class RaiseRewriter : PSharpRewriter
    {
        #region public API

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="program">IPSharpProgram</param>
        internal RaiseRewriter(IPSharpProgram program)
            : base(program)
        {

        }

        /// <summary>
        /// Rewrites the raise statements in the program.
        /// </summary>
        internal void Rewrite()
        {
            var statements = base.Program.GetSyntaxTree().GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>().
                Where(val => val.Expression is InvocationExpressionSyntax).
                Where(val => (val.Expression as InvocationExpressionSyntax).Expression is IdentifierNameSyntax).
                Where(val => ((val.Expression as InvocationExpressionSyntax).Expression as IdentifierNameSyntax).
                    Identifier.ValueText.Equals("raise")).
                ToList();

            if (statements.Count == 0)
            {
                return;
            }

            var root = base.Program.GetSyntaxTree().GetRoot().ReplaceNodes(
                nodes: statements,
                computeReplacementNode: (node, rewritten) => this.RewriteStatement(rewritten));

            base.UpdateSyntaxTree(root.ToString());
        }

        #endregion

        #region private methods

        /// <summary>
        /// Rewrites the statement with a raise statement.
        /// </summary>
        /// <param name="node">ExpressionStatementSyntax</param>
        /// <returns>SyntaxNode</returns>
        private SyntaxNode RewriteStatement(ExpressionStatementSyntax node)
        {
            var invocation = node.Expression as InvocationExpressionSyntax;

            var arguments = new List<ArgumentSyntax>();
            arguments.Add(invocation.ArgumentList.Arguments[0]);

            string payload = string.Empty;
            for (int i = 1; i < invocation.ArgumentList.Arguments.Count; i++)
            {
                if (i == invocation.ArgumentList.Arguments.Count - 1)
                {
                    payload += invocation.ArgumentList.Arguments[i].ToString();
                }
                else
                {
                    payload += invocation.ArgumentList.Arguments[i].ToString() + ", ";
                }
            }

            arguments[0] = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(
                "new " + arguments[0].ToString() + "(" + payload + ")"));
            invocation = invocation.WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments)));

            var text = node.WithExpression(invocation.WithExpression(SyntaxFactory.IdentifierName("this.Raise"))).ToString();
            var rewritten = SyntaxFactory.ParseStatement(text);
            rewritten = rewritten.WithTriviaFrom(node);

            return rewritten;
        }

        #endregion
    }
}
