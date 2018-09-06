// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See License.txt in the repo root for full license information.
// ------------------------------------------------------------------------------------------------

using Microsoft.CodeAnalysis.CSharp.DataFlowAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.PSharp.StaticAnalysis
{
    /// <summary>
    /// The P# on event goto machine action.
    /// </summary>
    internal sealed class OnEventGotoMachineAction : MachineAction
    {
        #region constructors

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="methodDecl">MethodDeclarationSyntax</param>
        /// <param name="state">MachineState</param>
        /// <param name="context">AnalysisContext</param>
        internal OnEventGotoMachineAction(MethodDeclarationSyntax methodDecl, MachineState state,
            AnalysisContext context)
            : base(methodDecl, state, context)
        {

        }

        #endregion
    }
}
